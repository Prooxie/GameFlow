using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Templates;
#if HIDMAESTRO_SDK
using HIDMaestro; // HMContext, HMController, HMProfile, HMGamepadState, HMButton, HMHat, HMOutputPacket
#endif

namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// Output sink that emits a virtual controller through HIDMaestro
/// (https://github.com/hifihedgehog/HIDMaestro) — a user-mode virtual
/// game-controller platform for Windows. It presents as real hardware
/// to DirectInput, XInput, SDL3, the browser Gamepad API and
/// WGI/GameInput, with no kernel driver, EV certificate, or reboot
/// (UMDF2 + a locally-trusted self-signed cert). MIT-licensed.
///
/// <para><b>Activation has two tiers</b> (this is the important part —
/// earlier builds of this sink could silently produce no output at all,
/// which is exactly what the diagnostics below eliminate):</para>
/// <list type="number">
/// <item><b>Compile-time SDK</b> (<c>#if HIDMAESTRO_SDK</c>, below) — used
/// when the project is built with the HIDMaestro.Core assembly referenced
/// and the compile symbol defined. Fastest path, no reflection.</item>
/// <item><b>Runtime dynamic bridge</b> (<see cref="HidMaestroDynamic"/>,
/// the tier that actually matters for ordinary builds) — if
/// <c>HIDMaestro.Core.dll</c> is simply dropped next to the executable,
/// it's loaded via reflection and driven the same way. No rebuild.</item>
/// </list>
/// <para>
/// By explicit product decision this sink does <b>not</b> fall back to
/// ViGEm: if neither tier can activate, the slot has no output, and both
/// the log and <see cref="DisplayName"/> say exactly why. Anyone who
/// wants ViGEm selects one of the "vigem-*" output providers directly —
/// this sink never substitutes one on their behalf.
/// </para>
/// </summary>
#if HIDMAESTRO_SDK
// ── Tier 1: compile-time SDK implementation ──
// Active only when the project defines the HIDMAESTRO_SDK compile symbol
// AND references the HIDMaestro.Core assembly. Modeled on the verified
// example/SdkDemo/Program.cs:
//   ctx.LoadDefaultProfiles(); ctx.InstallDriver();
//   using var ctrl = ctx.CreateController(ctx.GetProfile("xbox-360-wired"));
//   ctrl.OutputReceived += (controller, packet) => { ... }; // rumble/FFB
//   ctrl.SubmitState(in state);  // sticks [-1,1], triggers [0,1]
// Verified: HMGamepadState.{LeftStickX/Y,RightStickX/Y,LeftTrigger,
// RightTrigger,Buttons(HMButton flags),Hat(HMHat)}; HMHat.{None,North,
// NorthEast,East,SouthEast,South,SouthWest,West,NorthWest}.
// STILL INFERRED (flagged inline): HMButton spellings for Back/Start/
// thumb-clicks, the HMOutputPacket type name + OutputReceived delegate
// shape, and the rumble byte offsets. Confirm against your SDK / PadForge.
// Most builds never define HIDMAESTRO_SDK, so this branch gets far less
// day-to-day exercise than tier 2/3 below — if you're maintaining this,
// trust the runtime dynamic bridge (HidMaestroDynamic.cs) as the
// reference implementation first.

public sealed class HidMaestroOutputSink : IOutputSink, GameFlow.Infrastructure.Runtime.Slots.IConfigurableOutputSink, IRumbleFeedbackSource
{
    private readonly ILogger<HidMaestroOutputSink> logger;
    private readonly DeviceTemplateStore templateStore;
    private readonly object gate = new();

    private HMContext? context;
    private HMController? controller;
    private HMProfile? profile;
    private VirtualControllerKind currentKind = VirtualControllerKind.Xbox360;
    private bool connected;
    private bool disposed;

    public HidMaestroOutputSink(ILogger<HidMaestroOutputSink> logger, DeviceTemplateStore templateStore)
    {
        this.logger = logger;
        this.templateStore = templateStore;
    }

    public string DisplayName => "HIDMaestro virtual controller";

    /// <summary>Raised when the consuming game sends rumble (low, high) in 0–1.</summary>
    public event Action<double, double>? RumbleReceived;

    public (ushort Vid, ushort Pid)? OwnedHardwareSignature =>
        profile is null ? null : (profile.VendorId, profile.ProductId);

    /// <summary>
    /// Applies a device's output template — picks the virtual-controller
    /// profile to present. Called by the runtime/factory when it knows
    /// which device/slot this sink serves (per-device routing lands in
    /// Phase 3). Switching kind tears down and recreates the controller.
    /// </summary>
    public void Configure(DeviceOutputTemplate template)
    {
        if (template is null)
        {
            return;
        }
        lock (gate)
        {
            if (template.OutputKind == currentKind && connected)
            {
                return;
            }
            currentKind = template.OutputKind;
            TeardownController();
        }
    }

    public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        HMController? active;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }
            EnsureConnected();
            active = controller;
        }

        if (active is null)
        {
            return ValueTask.CompletedTask;
        }

        // HMGamepadState: sticks [-1,1], triggers [0,1] (direct fields).
        var state = BuildState(snapshot);
        active.SubmitState(in state);
        return ValueTask.CompletedTask;
    }

    private void EnsureConnected()
    {
        if (connected)
        {
            return;
        }
        try
        {
            context ??= new HMContext();
            _ = context.LoadDefaultProfiles();   // load embedded catalog before GetProfile
            context.InstallDriver();             // no-op when already installed; needs elevation on first run
            var profileId = HidMaestroProfiles.ResolveProfileId(currentKind);
            profile = context.GetProfile(profileId)
                ?? throw new InvalidOperationException($"HIDMaestro profile '{profileId}' not found.");
            controller = context.CreateController(profile);
            controller.OutputReceived += OnOutputReceived;   // game rumble/haptics/FFB → physical pad
            connected = true;
            logger.LogInformation("HIDMaestro controller created for profile {ProfileId}.", profile.Id);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to create HIDMaestro controller; output disabled this tick.");
            TeardownController();
        }
    }

    // ── State mapping (verified HMGamepadState surface) ──

    private static HMGamepadState BuildState(ControllerSnapshot s)
    {
        // Sticks [-1,+1] (Autofire already uses this, Y+ = up); triggers
        // [0,1]. If a target shows inverted Y on hardware, negate the
        // LeftStickY/RightStickY lines.
        return new HMGamepadState
        {
            LeftStickX  = Math.Clamp(s.LeftStick.X,  -1f, 1f),
            LeftStickY  = Math.Clamp(s.LeftStick.Y,  -1f, 1f),
            RightStickX = Math.Clamp(s.RightStick.X, -1f, 1f),
            RightStickY = Math.Clamp(s.RightStick.Y, -1f, 1f),
            LeftTrigger  = Math.Clamp(s.LeftTrigger,  0f, 1f),
            RightTrigger = Math.Clamp(s.RightTrigger, 0f, 1f),
            Buttons = MapButtons(s),
            Hat = MapHat(s),
        };
    }

    private static HMButton MapButtons(ControllerSnapshot s)
    {
        // A/B/X/Y, bumpers, Guide, Share are confirmed HMButton names.
        // Back/Start and the thumb clicks follow XInput-standard naming —
        // CONFIRM these four against the HMButton enum in your SDK build.
        HMButton b = HMButton.None;
        void Set(ButtonId id, HMButton flag) { if (s.IsPressed(id)) b |= flag; }
        Set(ButtonId.South, HMButton.A);
        Set(ButtonId.East,  HMButton.B);
        Set(ButtonId.West,  HMButton.X);
        Set(ButtonId.North, HMButton.Y);
        Set(ButtonId.LeftShoulder,  HMButton.LeftBumper);
        Set(ButtonId.RightShoulder, HMButton.RightBumper);
        Set(ButtonId.Guide, HMButton.Guide);
        Set(ButtonId.Touchpad, HMButton.Share);     // DualSense Create/Share
        Set(ButtonId.Back,  HMButton.Back);          // ← confirm (View/Menu?)
        Set(ButtonId.Start, HMButton.Start);         // ← confirm
        Set(ButtonId.LeftStick,  HMButton.LeftThumb);   // ← confirm
        Set(ButtonId.RightStick, HMButton.RightThumb);  // ← confirm
        return b;
    }

    private static HMHat MapHat(ControllerSnapshot s)
    {
        bool up = s.IsPressed(ButtonId.DpadUp);
        bool down = s.IsPressed(ButtonId.DpadDown);
        bool left = s.IsPressed(ButtonId.DpadLeft);
        bool right = s.IsPressed(ButtonId.DpadRight);
        if (up && right) return HMHat.NorthEast;
        if (down && right) return HMHat.SouthEast;
        if (down && left) return HMHat.SouthWest;
        if (up && left) return HMHat.NorthWest;
        if (up) return HMHat.North;
        if (right) return HMHat.East;
        if (down) return HMHat.South;
        if (left) return HMHat.West;
        return HMHat.None;
    }

    // ── Output (rumble / haptics / FFB) ──
    // OutputReceived delivers the raw wire bytes the game sent to the
    // virtual pad; the consumer decodes + forwards. This is a best-effort
    // decode of the common rumble layout into normalized (low, high) →
    // RumbleReceived, which the slot runtime forwards to the physical
    // device. VERIFY the packet type name, the (controller, packet)
    // delegate shape, and these byte offsets against PadForge's output
    // handler for the profiles you emit.
    private void OnOutputReceived(HMController sender, HMOutputPacket packet)
    {
        var data = packet.Data;
        if (data is null || data.Length == 0)
        {
            return;
        }

        double low = 0, high = 0;
        if (data.Length >= 5)
        {
            // Common XUSB SET_STATE vibration: [type, size, 0, big, small, …]
            low  = data[3] / 255.0;
            high = data[4] / 255.0;
        }
        else if (data.Length >= 2)
        {
            low  = data[0] / 255.0;
            high = data[1] / 255.0;
        }

        if (low > 0 || high > 0)
        {
            RumbleReceived?.Invoke(low, high);
        }
    }

    private void TeardownController()
    {
        try
        {
            if (controller is not null)
            {
                controller.OutputReceived -= OnOutputReceived;
            }
            controller?.Dispose();
        }
        catch (Exception exception) { logger.LogDebug(exception, "HIDMaestro controller dispose error."); }
        controller = null;
        connected = false;
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }
            disposed = true;
            TeardownController();
            try { context?.Dispose(); }
            catch (Exception exception) { logger.LogDebug(exception, "HIDMaestro context dispose error."); }
            context = null;
        }
        return ValueTask.CompletedTask;
    }
}
#else
// ── Tier 2/3: what an ordinary (non-SDK) build actually runs ──
// This is the class that compiles for essentially every real install,
// since referencing a private/inferred SDK assembly is not something a
// normal build does.
//
// Per explicit product decision: this sink does NOT fall back to ViGEm.
// HIDMaestro is either active (dynamic bridge found a working
// HIDMaestro.Core.dll) or it is not — in which case DisplayName and the
// log say exactly why, and WriteAsync is a documented no-op. Users who
// want ViGEm select one of the "vigem-*" output providers directly;
// this sink will never silently substitute one.
public sealed class HidMaestroOutputSink : IOutputSink, GameFlow.Infrastructure.Runtime.Slots.IConfigurableOutputSink
{
    private readonly ILogger<HidMaestroOutputSink> logger;
    private readonly object gate = new();

    private VirtualControllerKind currentKind = VirtualControllerKind.Xbox360;
    private bool configured;
    private bool disposed;

    // Resolved lazily on first write after each Configure(). Non-null
    // only while HIDMaestro is genuinely active and healthy.
    private DynamicHidMaestroController? dynamicController;
    private string activeState = "unresolved"; // "unresolved" | "active" | "unavailable"
    private string? unavailableReason;

    public HidMaestroOutputSink(ILogger<HidMaestroOutputSink> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Reflects the real state so the slots list and dashboard show the
    /// truth at a glance: active, or exactly why it isn't.
    /// </summary>
    public string DisplayName => activeState switch
    {
        "active" => "HIDMaestro virtual controller",
        "unavailable" => $"HIDMaestro unavailable — no output ({unavailableReason})",
        _ => "HIDMaestro virtual controller",
    };

    // Closes the same input-hiding gap ViGEm's sinks never had: without
    // this, HIDMaestro's own emitted device could be selected right back
    // in as input, direct or via another slot — the exact class of bug
    // the hardware-signature filtering exists to prevent. See
    // HidMaestroProfiles.ResolveHardwareSignature for why the well-known
    // pair is the right answer here (HIDMaestro impersonates the real
    // controller identity, same as ViGEm does).
    public (ushort Vid, ushort Pid)? OwnedHardwareSignature =>
        HidMaestroProfiles.ResolveHardwareSignature(currentKind);

    public void Configure(DeviceOutputTemplate template)
    {
        if (template is null)
        {
            return;
        }

        DynamicHidMaestroController? old;
        lock (gate)
        {
            if (configured && template.OutputKind == currentKind)
            {
                return;
            }
            currentKind = template.OutputKind;
            configured = true;
            old = dynamicController;
            dynamicController = null;
            activeState = "unresolved";
            unavailableReason = null;
        }
        old?.Dispose();
    }

    public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DynamicHidMaestroController? dynamic;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }
            EnsureActiveLocked();
            dynamic = dynamicController;
        }

        if (dynamic is null)
        {
            // Unavailable — EnsureActiveLocked already logged exactly why,
            // once, the first time resolution failed for this
            // configuration. No output; nothing silently substituted.
            return ValueTask.CompletedTask;
        }

        var ok = SubmitDynamic(dynamic, snapshot);
        if (!ok && !dynamic.IsHealthy)
        {
            // The controller itself gave up after too many consecutive
            // reflection failures (logged there). Stop holding a
            // reference to a proven-broken instance so the NEXT write
            // doesn't keep trying it — Configure() (a kind switch) or a
            // process restart are the paths back to "unresolved".
            lock (gate)
            {
                if (ReferenceEquals(dynamicController, dynamic))
                {
                    dynamicController = null;
                    activeState = "unavailable";
                    unavailableReason = "submit failed repeatedly — see log";
                }
            }
            dynamic.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Resolves the dynamic HIDMaestro bridge once per Configure() call.
    /// Callers must hold <see cref="gate"/>.
    /// </summary>
    private void EnsureActiveLocked()
    {
        if (dynamicController is not null || activeState == "unavailable")
        {
            return; // already resolved (success or terminal failure) for this configuration
        }

        if (!HidMaestroDynamic.IsAvailable(logger))
        {
            unavailableReason = HidMaestroDynamic.StatusDescription;
            activeState = "unavailable";
            logger.LogWarning(
                "HIDMaestro is not available ({Status}) and this sink no longer falls back to ViGEm — " +
                "this slot has NO output until HIDMaestro.Core.dll is placed next to the executable " +
                "(or select a ViGEm output provider directly for this slot instead).",
                HidMaestroDynamic.StatusDescription);
            return;
        }

        var profileId = HidMaestroProfiles.ResolveProfileId(currentKind);
        var controller = HidMaestroDynamic.TryCreateController(profileId, logger, out var creationFailure);
        if (controller is null)
        {
            unavailableReason = creationFailure;
            activeState = "unavailable";
            logger.LogError(
                "HIDMaestro controller creation failed for profile '{ProfileId}' ({Failure}) — this slot has " +
                "no output. This sink no longer falls back to ViGEm; select a ViGEm output provider directly " +
                "for this slot if you want output while this is investigated.",
                profileId, creationFailure);
            return;
        }

        dynamicController = controller;
        activeState = "active";
        logger.LogInformation("HIDMaestro (dynamic) active for profile {ProfileId}.", profileId);
    }

    /// <summary>Maps a snapshot onto the dynamic bridge's button-name/hat-name submit call.</summary>
    private static bool SubmitDynamic(DynamicHidMaestroController controller, ControllerSnapshot s)
    {
        // Same HMButton name mapping as the compile-time tier (see the
        // #if HIDMAESTRO_SDK branch's MapButtons for the confirmed vs.
        // inferred split); the dynamic bridge logs once per session if
        // any of these names don't exist on the real HMButton enum.
        var buttons = new List<(string ButtonName, bool Down)>(12)
        {
            ("A", s.IsPressed(ButtonId.South)),
            ("B", s.IsPressed(ButtonId.East)),
            ("X", s.IsPressed(ButtonId.West)),
            ("Y", s.IsPressed(ButtonId.North)),
            ("LeftBumper",  s.IsPressed(ButtonId.LeftShoulder)),
            ("RightBumper", s.IsPressed(ButtonId.RightShoulder)),
            ("Guide", s.IsPressed(ButtonId.Guide)),
            ("Share", s.IsPressed(ButtonId.Touchpad)),
            ("Back",  s.IsPressed(ButtonId.Back)),
            ("Start", s.IsPressed(ButtonId.Start)),
            ("LeftThumb",  s.IsPressed(ButtonId.LeftStick)),
            ("RightThumb", s.IsPressed(ButtonId.RightStick)),
        };

        return controller.Submit(
            Math.Clamp(s.LeftStick.X,  -1f, 1f),
            Math.Clamp(s.LeftStick.Y,  -1f, 1f),
            Math.Clamp(s.RightStick.X, -1f, 1f),
            Math.Clamp(s.RightStick.Y, -1f, 1f),
            Math.Clamp(s.LeftTrigger,  0f, 1f),
            Math.Clamp(s.RightTrigger, 0f, 1f),
            buttons,
            ResolveHatName(s));
    }

    private static string ResolveHatName(ControllerSnapshot s)
    {
        bool up = s.IsPressed(ButtonId.DpadUp);
        bool down = s.IsPressed(ButtonId.DpadDown);
        bool left = s.IsPressed(ButtonId.DpadLeft);
        bool right = s.IsPressed(ButtonId.DpadRight);
        if (up && right) return "NorthEast";
        if (down && right) return "SouthEast";
        if (down && left) return "SouthWest";
        if (up && left) return "NorthWest";
        if (up) return "North";
        if (right) return "East";
        if (down) return "South";
        if (left) return "West";
        return "None";
    }

    public ValueTask DisposeAsync()
    {
        DynamicHidMaestroController? dynamic;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }
            disposed = true;
            dynamic = dynamicController;
            dynamicController = null;
        }

        dynamic?.Dispose();
        return ValueTask.CompletedTask;
    }
}
#endif
