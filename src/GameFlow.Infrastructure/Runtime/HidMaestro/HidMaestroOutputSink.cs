using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Templates;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// Output sink that emits a virtual controller through HIDMaestro
/// (https://github.com/hifihedgehog/HIDMaestro) — a user-mode virtual
/// game-controller platform for Windows. It presents as real hardware
/// to DirectInput, XInput, SDL3, the browser Gamepad API and
/// WGI/GameInput, with no kernel driver, EV certificate, or reboot
/// (UMDF2 + a locally-trusted self-signed cert). MIT-licensed.
///
/// <para>
/// <b>Activation:</b> the sink reaches the SDK only through
/// <see cref="IHidMaestroBackend"/>. On Windows the typed adapter in
/// <c>GameFlow.HidMaestro</c> implements it, so dropping
/// <c>HIDMaestro.Core.dll</c> next to the executable (or into a
/// <c>HIDMaestro</c> subfolder, or the folder named by
/// <c>GAMEFLOW_HIDMAESTRO_DIR</c>) activates the real sink with no
/// rebuild. Everywhere else <see cref="UnavailableHidMaestroBackend"/>
/// says why there is no output. The sink decides which pad a slot gets,
/// when to retry and when to give up; the pad decides how a snapshot
/// reaches the wire.
/// </para>
///
/// <para>
/// HIDMaestro is the sole Windows output backend. If it cannot
/// activate, the slot has no output, and both the log and
/// <see cref="DisplayName"/> say exactly why — including the two causes
/// that account for essentially every real "no controller appears"
/// report: the process not running elevated, and a profile id that
/// doesn't exist in the SDK's catalog. What the sink deploys is decided
/// by the slot's <see cref="DeviceOutputTemplate"/>: an explicit catalog
/// profile id when the loaded catalog has it and can deploy it, the verified default
/// profile for the template's <see cref="VirtualControllerKind"/>
/// otherwise, or — for <see cref="VirtualControllerKind.GenericDirectInput"/>
/// — a profile BUILT at runtime from the template's axis/button/POV
/// counts (a <see cref="HidMaestroGenericShape"/>).
/// </para>
/// </summary>
public sealed class HidMaestroOutputSink : IOutputSink,
    GameFlow.Infrastructure.Runtime.Slots.IConfigurableOutputSink,
    GameFlow.Infrastructure.Runtime.Slots.IPostRebuildFinalizer,
    IRumbleFeedbackSource
{
    /// <summary>
    /// How many frames in a row a pad may reject before the sink gives up
    /// on it.
    /// </summary>
    /// <remarks>
    /// About one to three seconds at typical tick rates. One failed frame
    /// is worth retrying. Three hundred in a row is a pad that will not fix
    /// itself frame to frame, and calling it forever only repeats the same
    /// exception at the tick rate.
    /// </remarks>
    internal const int MaxConsecutiveSubmitFailures = 300;

    /// <summary>The profile used when neither the template nor its kind names one.</summary>
    private const string LastResortProfileId = "xbox-360-wired";

    private readonly ILogger<HidMaestroOutputSink> logger;
    private readonly IHidMaestroBackend backend;
    private readonly TimeProvider timeProvider;
    private readonly object gate = new();

    private DeviceOutputTemplate template = new();
    private bool disposed;

    // Resolved lazily on first write after each Configure(). Non-null
    // only while HIDMaestro is genuinely active and healthy.
    private IHidMaestroPad? activePad;
    private string activeState = "unresolved"; // "unresolved" | "active" | "unavailable"
    private string? unavailableReason;

    /// <summary>
    /// Frames the active pad has rejected in a row. Read and written only
    /// on the runtime tick thread, which is the only caller of
    /// <see cref="WriteAsync"/>.
    /// </summary>
    private int consecutiveSubmitFailures;

    /// <summary>
    /// Creates the sink. <paramref name="backend"/> is its only way to
    /// HIDMaestro, so a test can stand in for the SDK.
    /// </summary>
    public HidMaestroOutputSink(ILogger<HidMaestroOutputSink> logger, IHidMaestroBackend backend)
        : this(logger, backend, TimeProvider.System)
    {
    }

    internal HidMaestroOutputSink(
        ILogger<HidMaestroOutputSink> logger, IHidMaestroBackend backend, TimeProvider timeProvider)
    {
        this.logger = logger;
        this.backend = backend;
        this.timeProvider = timeProvider;
    }

    public event Action<double, double>? RumbleReceived;

    /// <summary>
    /// Reflects the real state so the slots list and dashboard show the
    /// truth at a glance: which profile is live, or exactly why none is.
    /// </summary>
    public string DisplayName => activeState switch
    {
        "active" when activePad is { } pad => $"HIDMaestro — {pad.ProfileName}",
        "unavailable" => $"HIDMaestro unavailable — no output ({unavailableReason})",
        _ => "HIDMaestro virtual controller",
    };

    /// <summary>
    /// The identity this sink's emitted device advertises, used to hide
    /// it from the input list (a virtual output selected back in as
    /// input was a real freeze source). While a pad is live this is the
    /// pad's own identity, read from the deployed profile. While a pad is
    /// being created it is the reserved identity. Otherwise it is null,
    /// which is safe: the identity is only ever published together with
    /// <see cref="OwnedSignatureActivatedAt"/>.
    /// </summary>
    public (ushort Vid, ushort Pid)? OwnedHardwareSignature
    {
        get
        {
            lock (gate)
            {
                return activePad?.HardwareSignature ?? reservedSignature;
            }
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? OwnedSignatureActivatedAt
    {
        get
        {
            lock (gate)
            {
                return activePad is not null || reservedSignature is not null ? activatedAtUtc : null;
            }
        }
    }

    private DateTimeOffset activatedAtUtc;

    /// <summary>
    /// The identity reserved from the moment pad creation is attempted
    /// until the pad is live or the attempt is known to have failed. Null
    /// when nothing is reserved — including when the requested profile is
    /// not in the catalog, so there is no identity to read.
    /// </summary>
    /// <remarks>
    /// The signature used to be published only once creation had returned
    /// successfully, which leaves a window: the device exists in Windows,
    /// and can therefore be enumerated by SDL, before anything has claimed
    /// it. A poll landing in that window publishes GameFlow's own pad as
    /// physical hardware, and it is only withdrawn a tick later when the
    /// signature finally arrives — the "appears as a real controller,
    /// then disappears" flicker.
    ///
    /// <para>
    /// Reserving before the call closes the window, at the cost of also
    /// covering the few hundred milliseconds the call takes. A real pad of
    /// the same model plugged in during exactly that window would be
    /// hidden; one plugged in before it still is not, because the catalog
    /// compares against when each device was FIRST seen.
    /// </para>
    /// </remarks>
    private (ushort Vid, ushort Pid)? reservedSignature;

    // Cooldowns use elapsed time; activation identity timestamps remain UTC
    // because the input catalog compares them with device discovery times.
    private long retryStartedAt;
    private TimeSpan retryInterval;

    /// <summary>
    /// How long an "unavailable" verdict is held before it is re-checked.
    ///
    /// <para>
    /// Every path that latches <c>activeState = "unavailable"</c> must also
    /// arm this, because the latch alone does not stop anything: the gate in
    /// <c>EnsureActiveLocked</c> is the cooldown, and an unarmed cooldown is
    /// already in the past. The missing-DLL and non-Windows paths latched
    /// without arming, so every tick fell straight through the latch,
    /// re-probed, and logged again — roughly a thousand identical warnings a
    /// second, to the console and the rolling file sink, on the single most
    /// common failure there is ("HIDMaestro.Core.dll not found").
    /// </para>
    /// </summary>
    private static readonly TimeSpan UnavailableRetryInterval = TimeSpan.FromSeconds(45);

    /// <summary>
    /// How long the sink waits before it creates a new pad, after it gave up
    /// on one that kept rejecting frames. Longer than a creation failure's
    /// cooldown: each retry here creates a NEW device, so cycling must be rare.
    /// </summary>
    private static readonly TimeSpan SubmitGiveUpRetryInterval = TimeSpan.FromMinutes(5);

    public void Configure(DeviceOutputTemplate template)
    {
        if (template is null)
        {
            return;
        }

        IHidMaestroPad? old;
        lock (gate)
        {
            var fingerprintChanged = !string.Equals(
                Fingerprint(this.template), Fingerprint(template), StringComparison.Ordinal);
            this.template = template.Clone();
            if (!fingerprintChanged)
            {
                // FULL no-op for an identical emit-shape: an "unavailable"
                // latch (failed creation, give-up) must survive rebuilds,
                // or every profile save retries and the OS sees a
                // controller-creation storm. The latch clears only when
                // the template genuinely changes what device is emitted.
                return;
            }
            old = activePad;
            activePad = null;
            reservedSignature = null;
            activeState = "unresolved";
            unavailableReason = null;
        }
        if (old is not null)
        {
            TeardownPad(old);
        }
    }

    /// <summary>
    /// Everything about the template that changes WHAT device is
    /// emitted. Lighting/rumble fields deliberately excluded — they
    /// mustn't tear down a live device.
    /// </summary>
    private static string Fingerprint(DeviceOutputTemplate t) =>
        $"{t.OutputKind}|{t.OutputProfileId}|{t.ThumbstickCount}|{t.TriggerCount}|{t.ButtonCount}|{t.PovCount}|{t.ProductString}|{t.GenericVendorId:X4}|{t.GenericProductId:X4}";

    public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IHidMaestroPad? pad;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }
            EnsureActiveLocked();
            pad = activePad;
        }

        if (pad is null)
        {
            // Unavailable — EnsureActiveLocked already logged exactly why,
            // once, the first time resolution failed for this
            // configuration. No output; nothing silently substituted.
            return ValueTask.CompletedTask;
        }

        LogFirstBatterySubmission(snapshot);

        try
        {
            pad.Submit(snapshot);
        }
        catch (Exception exception)
        {
            OnSubmitFailed(pad, exception);
            return ValueTask.CompletedTask;
        }

        if (consecutiveSubmitFailures > 0)
        {
            logger.LogInformation(
                "HIDMaestro: submit recovered after {Count} failed frame(s).", consecutiveSubmitFailures);
            consecutiveSubmitFailures = 0;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Counts one rejected frame, and detaches the pad once
    /// <see cref="MaxConsecutiveSubmitFailures"/> frames in a row have
    /// failed.
    /// </summary>
    /// <remarks>
    /// Only the first failure of a run and the give-up are logged. This
    /// runs at the tick rate, and the same exception every frame says
    /// nothing new.
    /// </remarks>
    private void OnSubmitFailed(IHidMaestroPad pad, Exception exception)
    {
        consecutiveSubmitFailures++;
        if (consecutiveSubmitFailures == 1)
        {
            logger.LogWarning(
                exception,
                "HIDMaestro: submitting a frame to profile {ProfileId} failed. The sink keeps trying and gives up " +
                "after {Limit} failures in a row.",
                pad.ProfileId, MaxConsecutiveSubmitFailures);
        }

        if (consecutiveSubmitFailures < MaxConsecutiveSubmitFailures)
        {
            return;
        }

        // Stop holding a reference to a proven-broken pad so the NEXT
        // write doesn't keep trying it. The cooldown, Configure() (a
        // template change) or a process restart are the paths back.
        var detached = false;
        lock (gate)
        {
            if (ReferenceEquals(activePad, pad))
            {
                activePad = null;
                reservedSignature = null;
                activeState = "unavailable";
                unavailableReason = "submit failed repeatedly — see log";
                DelayRetry(SubmitGiveUpRetryInterval);
                detached = true;
            }
        }

        if (detached)
        {
            logger.LogError(
                "HIDMaestro: submit has failed {Count} consecutive times — giving up on this controller instance " +
                "rather than retrying forever. A new controller is tried in {RetryMinutes:F0} minutes. Last error: {Error}",
                consecutiveSubmitFailures, SubmitGiveUpRetryInterval.TotalMinutes, exception.Message);
            TeardownPad(pad);
        }
    }

    /// <summary>
    /// Creates the pad for the current template, once per Configure() call
    /// and at most once per cooldown after a failure. Callers must hold
    /// <see cref="gate"/>.
    /// </summary>
    private void EnsureActiveLocked()
    {
        if (activePad is not null)
        {
            return; // already active for this configuration
        }
        if (activeState == "unavailable")
        {
            // Latched — but only until the cooldown elapses. A single
            // failed creation (typically: not running as Administrator
            // yet) used to be terminal until the template changed, which
            // read as "no controller is ever created". One retry per
            // 45 s window recovers automatically once the blocker is
            // gone and cannot read as a creation storm.
            if (timeProvider.GetElapsedTime(retryStartedAt) < retryInterval)
            {
                return;
            }
            activeState = "unresolved";
            unavailableReason = null;
        }

        // One path for every reason there is no output: no SDK, an
        // unsupported SDK, no elevation, not Windows. The backend knows
        // which, and says so in its Description.
        var status = backend.GetStatus();
        if (!status.CanCreatePads)
        {
            unavailableReason = status.Description;
            // Armed so the backend is re-checked periodically rather than
            // on every tick: dropping HIDMaestro.Core.dll in beside a
            // running app still recovers within the window, without the
            // log flood.
            DelayRetry(UnavailableRetryInterval);
            activeState = "unavailable";
            logger.LogWarning(
                "HIDMaestro is not available ({Status}) — this slot has no output. There is no fallback output " +
                "provider. Re-checking in {RetrySeconds:F0}s.",
                status.Description,
                UnavailableRetryInterval.TotalSeconds);
            return;
        }

        var request = BuildRequestLocked(out var signature);

        // Reserved before the device can exist, not after it does — see
        // the note on reservedSignature.
        activatedAtUtc = timeProvider.GetUtcNow();
        reservedSignature = signature;

        IHidMaestroPad? pad;
        string? creationFailure;
        Exception? creationException = null;
        try
        {
            if (!backend.TryCreatePad(request, out pad, out creationFailure))
            {
                pad = null;
            }
        }
        catch (Exception exception)
        {
            // The seam promises false with a reason. The sink does not rely
            // on it: a throw that escaped here left the signature reserved and
            // the cooldown unarmed, so every tick tried to create again.
            pad = null;
            creationFailure = exception.Message;
            creationException = exception;
        }

        // A throw and a returned failure take the same path.
        if (pad is null)
        {
            reservedSignature = null;
            activatedAtUtc = default;
            unavailableReason = creationFailure;
            DelayRetry(UnavailableRetryInterval);
            activeState = "unavailable";
            logger.LogError(
                creationException,
                "HIDMaestro controller creation failed ({Failure}) — this slot has no output until this is " +
                "resolved. There is no fallback output provider.",
                creationFailure);
            return;
        }

        // From here on the pad's own identity is published instead.
        reservedSignature = null;
        pad.RumbleReceived += OnRumbleReceived;
        activePad = pad;
        activeState = "active";
        consecutiveSubmitFailures = 0;
        WarnIfOptionalStateCannotReachTheWire(pad);
        logger.LogInformation(
            "HIDMaestro active: profile {ProfileId} ('{ProfileName}', VID/PID {Signature}).",
            pad.ProfileId, pad.ProfileName, $"{pad.HardwareSignature.Vid:X4}:{pad.HardwareSignature.Pid:X4}");
    }

    /// <summary>Starts a monotonic cooldown. Callers must hold <see cref="gate"/>.</summary>
    private void DelayRetry(TimeSpan interval)
    {
        retryStartedAt = timeProvider.GetTimestamp();
        retryInterval = interval;
    }

    /// <summary>
    /// What this template asks the backend for, and the identity to
    /// reserve while the pad is being made. Callers must hold
    /// <see cref="gate"/>.
    /// </summary>
    /// <remarks>
    /// The identity comes from the same place the pad's own will: the
    /// template's pair for a built generic profile, or the catalog entry
    /// for a catalog profile. A per-kind table used to guess it, and it
    /// guessed wrong for the DualShock 4 (<c>054C:09CC</c>, where the
    /// deployed profile is <c>054C:05C4</c>) and the Xbox One
    /// (<c>045E:02EA</c>, where it is <c>045E:02FD</c>) — so for those two
    /// pads the reservation hid a different device from the one being
    /// created.
    /// </remarks>
    private HidMaestroPadRequest BuildRequestLocked(out (ushort Vid, ushort Pid)? signature)
    {
        if (template.OutputKind == VirtualControllerKind.GenericDirectInput
            && string.IsNullOrWhiteSpace(template.OutputProfileId))
        {
            var productString = string.IsNullOrWhiteSpace(template.ProductString)
                ? "GameFlow Game Controller"
                : template.ProductString;
            signature = (template.GenericVendorId, template.GenericProductId);
            return new HidMaestroPadRequest(
                $"gameflow-custom-{template.GenericVendorId:x4}{template.GenericProductId:x4}",
                new HidMaestroGenericShape(
                    productString,
                    template.GenericVendorId,
                    template.GenericProductId,
                    template.ThumbstickCount,
                    template.TriggerCount,
                    template.ButtonCount,
                    template.PovCount));
        }

        var entry = ResolveCatalogProfile(out var profileId);
        signature = entry is null ? null : (entry.VendorId, entry.ProductId);
        return new HidMaestroPadRequest(profileId);
    }

    /// <summary>
    /// The catalog profile this template resolves to: the explicit pick
    /// when the loaded catalog has it and can deploy it, otherwise the kind's verified
    /// default. Null when the catalog does not list the resolved id.
    /// </summary>
    /// <remarks>
    /// Ids match without regard to case, and the request then uses the
    /// catalog's own spelling. A default that the catalog does not list
    /// is still requested: the backend's failure text then says what is
    /// wrong, which is more use than refusing here.
    /// </remarks>
    private HidMaestroCatalogProfile? ResolveCatalogProfile(out string profileId)
    {
        var catalog = backend.GetCatalog();
        var fallback = HidMaestroProfiles.DefaultProfileId(template.OutputKind) ?? LastResortProfileId;

        var chosen = template.OutputProfileId?.Trim();
        if (!string.IsNullOrEmpty(chosen))
        {
            // A profile the SDK lists but cannot deploy counts as not listed.
            // The kind default below has no such check: its failure text is clearer.
            if (FindInCatalog(catalog, chosen) is { } picked && picked.IsDeployable)
            {
                profileId = picked.Id;
                return picked;
            }

            logger.LogWarning(
                "HIDMaestro: this slot's chosen profile '{Chosen}' is not in the loaded catalog or cannot be deployed — " +
                "using '{Resolved}' instead.",
                template.OutputProfileId, fallback);
        }

        var entry = FindInCatalog(catalog, fallback);
        profileId = entry?.Id ?? fallback;
        return entry;
    }

    private static HidMaestroCatalogProfile? FindInCatalog(IReadOnlyList<HidMaestroCatalogProfile> catalog, string id)
    {
        foreach (var profile in catalog)
        {
            if (string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return null;
    }

    /// <summary>
    /// Says once, per pad, when the deployed profile cannot carry the
    /// optional state this sink submits.
    /// </summary>
    /// <remarks>
    /// Battery, motion and touch travel in a Sony report's extended
    /// section, and HIDMaestro runs that section's codec on the input
    /// direction only for a profile that arms it. Every USB Sony profile
    /// leaves both <c>armOn</c> and <c>alwaysArmed</c> unset — checked
    /// directly against the shipped catalogue: <c>dualshock-4-v1-full</c>
    /// and <c>dualsense</c> have neither, while <c>dualsense-bt</c> arms
    /// on a feature read — so the battery byte is never written and the
    /// submitted value goes nowhere. The pad reads this from its profile
    /// (<see cref="IHidMaestroPad.CarriesExtendedInput"/>); the id's
    /// spelling is only used to tell a Sony family apart.
    ///
    /// <para>
    /// This is invisible from inside GameFlow: the submit succeeds, the
    /// SDK accepts the field, and the number simply does not appear on
    /// the wire. It was diagnosed only by changing the submitted charge
    /// from 3 to 25 and watching the reported value not move. Saying so
    /// costs one line and saves that experiment.
    /// </para>
    /// </remarks>
    private void WarnIfOptionalStateCannotReachTheWire(IHidMaestroPad pad)
    {
        if (pad.CarriesExtendedInput)
        {
            return;
        }

        var family = HidMaestroProfiles.ClassifyFamily(pad.ProfileId);
        if (family is not (VirtualControllerKind.DualShock4 or VirtualControllerKind.DualSense))
        {
            return;
        }

        logger.LogWarning(
            "HIDMaestro: profile '{ProfileId}' is a USB Sony profile, which does not arm its extended-report "
            + "codec on the input direction — battery, motion and touch are submitted but never reach the wire, "
            + "so a game or Steam reads the driver's default rather than the source pad's real values. "
            + "Choose a Bluetooth Sony profile (for example dualsense-bt) if those need to pass through.",
            pad.ProfileId);
    }

    private void OnRumbleReceived(double lowFrequency, double highFrequency) =>
        RumbleReceived?.Invoke(lowFrequency, highFrequency);

    /// <summary>
    /// Sends the rumble stop, then removes the pad. A pad that fails to
    /// dispose is logged and dropped: <see cref="Configure"/> and
    /// <see cref="DisposeAsync"/> must not throw because of it.
    /// </summary>
    private void TeardownPad(IHidMaestroPad pad) =>
        HidMaestroRumbleLifecycle.StopAndTeardown(
            RumbleReceived,
            () =>
            {
                pad.RumbleReceived -= OnRumbleReceived;
                try
                {
                    pad.Dispose();
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "HIDMaestro: removing the virtual controller for profile {ProfileId} failed. It may stay " +
                        "visible to Windows until GameFlow exits.",
                        pad.ProfileId);
                }
            },
            exception => logger.LogWarning(
                exception, "HIDMaestro: a rumble subscriber failed while stopping feedback during controller teardown."));

    /// <inheritdoc />
    public void FinalizeAfterRebuild()
    {
        // Only meaningful once a controller actually exists; before that
        // there are no names to settle.
        if (activeState != "active")
        {
            return;
        }

        try
        {
            backend.FinalizeNames();
        }
        catch (Exception exception)
        {
            // Cosmetic step. A rebuild must not fail because Windows would
            // not settle a friendly name.
            logger.LogDebug(exception, "HIDMaestro: finalizing virtual controller names failed.");
        }
    }

    /// <summary>
    /// Says once, per sink, what charge this slot is actually sending.
    /// </summary>
    /// <remarks>
    /// "The virtual pad reads 5%" has two completely different causes that
    /// look identical from outside: the source snapshot carrying no charge
    /// at all (so the write is the no-battery fallback), or the value
    /// travelling on a scale the consumer reads differently. A live capture
    /// pinned the emitted pad at exactly 10% across runs while its source
    /// moved 75% to 65% — which is the fallback constant, not a rescaled
    /// measurement — but the log could not prove which of the two it was.
    /// This line prints both halves so the next run settles it.
    /// </remarks>
    private void LogFirstBatterySubmission(ControllerSnapshot s)
    {
        // Two separate firsts, because the first submission of all is
        // almost always too early to mean anything: a pad is created and
        // starts submitting within milliseconds, while SDL's first battery
        // reading for the SOURCE pad lands over a second later. A capture
        // showed exactly that — "no charge reported" at 28.713, the real
        // 65% arriving at 30.055 — which says nothing about the steady
        // state, and the steady state is the thing in question.
        var known = s.BatteryPercent is not null;

        if (batterySubmissionLogged && (batteryKnownLogged || !known))
        {
            return;
        }

        batterySubmissionLogged = true;
        batteryKnownLogged |= known;

        logger.LogInformation(
            "HIDMaestro: battery submitted ({Which}) — source={Source}, " +
            "written={Written}% (0-{Full}), charging={Charging}, treatedAsFull={Full2}.",
            known ? "first with a real charge — this is the steady state"
                  : "first submission of all — the source may not have been read yet",
            s.BatteryPercent is { } p ? p + "%" : "<none reported by the source pad>",
            HidMaestroSubmission.BatteryLevelFor(s),
            HidMaestroSubmission.FullCharge,
            s.BatteryCharging,
            s.BatteryPercent is null or >= 100);
    }

    private bool batterySubmissionLogged;

    /// <summary>Whether a submission carrying a real charge has been reported.</summary>
    private bool batteryKnownLogged;

    public ValueTask DisposeAsync()
    {
        IHidMaestroPad? pad;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }
            disposed = true;
            pad = activePad;
            activePad = null;
            reservedSignature = null;
        }

        if (pad is not null)
        {
            TeardownPad(pad);
        }
        return ValueTask.CompletedTask;
    }
}
