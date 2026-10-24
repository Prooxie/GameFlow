using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Runtime.Slots;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Effects;

/// <summary>
/// Decides what every assigned pad should currently be showing, and
/// publishes it to the effects queue.
///
/// <para>
/// This is the link that turns saved settings into output. Without it the
/// rest of the chain — resolver, queue, mailbox, SDL writer — is complete
/// and does nothing, because nothing produces. That is exactly the state
/// it shipped in once.
/// </para>
///
/// <para>
/// Runs on a timer rather than inside the mapping tick. The mapping tick
/// can be 1000 Hz and must not perform blocking output work. A 16 ms pass
/// matches the effect queue's per-device write interval, keeps returned
/// game rumble responsive, and remains smooth for lighting animations.
/// </para>
/// </summary>
public sealed class ControllerEffectProducer(
    SlotRegistry slotRegistry,
    SlotSnapshotStore snapshots,
    DeviceSettingsStore deviceSettings,
    InputDeviceCatalog deviceCatalog,
    RumbleFeedbackStore rumbleFeedback,
    ControllerEffectsService effects,
    ILogger<ControllerEffectProducer> logger,
    ExternalLightingStore? externalLighting = null) : BackgroundService
{
    private static readonly TimeSpan ProduceInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// Charge at or below which the pad is treated as low. 20% is roughly
    /// where a DualSense has under an hour left.
    /// </summary>
    private const int LowBatteryPercent = 20;

    /// <summary>Charge that clears the low state. Above the trigger, so a reading hovering on the boundary does not flap.</summary>
    private const int LowBatteryClearPercent = 25;

    private readonly Dictionary<string, bool> lowBatteryByDevice = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> publishedDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> seenDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> staleDevices = [];

    /// <summary>Which layer last drove each pad's lightbar. Producer thread only.</summary>
    private readonly Dictionary<string, LightingSource> lightingSourceByDevice = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

    /// <summary>
    /// Devices currently reporting a low battery. The UI reads this to
    /// show a warning without polling SDL itself.
    /// </summary>
    public IReadOnlyCollection<string> LowBatteryDevices
    {
        get
        {
            lock (lowBatteryByDevice)
            {
                return lowBatteryByDevice.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        startedAtUtc = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(ProduceInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }

                ProduceOnce();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // One bad frame must not kill lighting for the session.
                logger.LogDebug(exception, "Effect production pass failed.");
            }
        }
    }

    /// <summary>Produces one effects frame. Call this method from one producer thread only.</summary>
    internal void ProduceOnce()
    {
        var elapsed = (DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds;
        seenDevices.Clear();

        foreach (var slot in slotRegistry.GetSlots())
        {
            if (!slot.Enabled)
            {
                continue;
            }

            var pair = snapshots.Get(slot.Id);

            foreach (var deviceId in slot.InputDeviceIds)
            {
                if (string.IsNullOrEmpty(deviceId) || !seenDevices.Add(deviceId))
                {
                    continue;
                }

                // A pad-specific entry wins; otherwise effects inherit the
                // slot defaults that can be edited before the pad is online.
                var settings = deviceSettings.GetEffective(slot.Id, deviceId);
                _ = deviceCatalog.TryGetById(deviceId, out var info);

                UpdateLowBatteryState(deviceId, info);

                var physical = pair.Physical;
                var requestedRumble = rumbleFeedback.Get(slot.Id);
                var (low, high) = EffectSceneResolver.ResolveRumble(
                    settings.Rumble,
                    requestedRumble.LowFrequency,
                    requestedRumble.HighFrequency);

                // A low battery used to override every mode unconditionally.
                // It is still the default — a pad that dies mid-session is a
                // worse outcome than a lightbar briefly ignoring its setting
                // — but the resolver now honours LowBatteryAlert, so a pad
                // that lives on its cable can opt out.
                var context = new EffectSceneContext(
                    ElapsedSeconds: elapsed,
                    PlayerIndex: slot.Index,
                    BatteryPercent: info?.BatteryPercentage,
                    IsCharging: info?.BatteryState == DeviceBatteryState.Charging,
                    RumbleLevel: Math.Max(low, high),
                    TriggerLevel: Math.Max(physical.LeftTrigger, physical.RightTrigger),
                    LowBattery: IsLow(deviceId),
                    Game: rumbleFeedback.Lighting.Get(slot.Id),
                    External: externalLighting?.Resolve(slot.Id, deviceId));

                var lighting = settings.Lighting;
                var resolved = EffectSceneResolver.ResolveLighting(lighting, context);
                var features = info?.EffectiveFeatures ?? ControllerFeatures.None;
                var (leftTriggerRumble, rightTriggerRumble) = ResolveTriggerRumble(settings.Rumble, features, low, high);

                effects.Queue.Publish(deviceId, BuildState(
                    features,
                    low,
                    high,
                    leftTriggerRumble,
                    rightTriggerRumble,
                    resolved,
                    ToCommand(EffectSceneResolver.ResolveAdaptiveTrigger(settings.LeftAdaptiveTrigger, context)),
                    ToCommand(EffectSceneResolver.ResolveAdaptiveTrigger(settings.RightAdaptiveTrigger, context))));

                LogLightingSourceChange(deviceId, info?.DisplayName, resolved.Source);

                if (publishedDevices.Add(deviceId))
                {
                    // Says plainly that effects ARE being produced for a
                    // specific pad. Effects only apply to a device
                    // assigned to an enabled slot, so the common "nothing
                    // happens" case is an unassigned pad — and without
                    // this line that is indistinguishable from a broken
                    // backend.
                    logger.LogInformation(
                        "Producing effects for {Device} on slot {Slot}: lighting {Mode}, rumble {Rumble}.",
                        info?.DisplayName ?? deviceId,
                        slot.Name,
                        lighting.Mode,
                        settings.Rumble.Enabled ? "enabled" : "disabled");
                }
            }
        }

        // A pad that left a slot must be told to stop. Without this it
        // keeps whatever rumble and colour it had when it was unassigned.
        staleDevices.Clear();
        foreach (var deviceId in publishedDevices)
        {
            if (!seenDevices.Contains(deviceId))
            {
                staleDevices.Add(deviceId);
            }
        }

        foreach (var stale in staleDevices)
        {
            effects.Queue.Retire(stale);
            _ = publishedDevices.Remove(stale);

            lock (lowBatteryByDevice)
            {
                _ = lowBatteryByDevice.Remove(stale);
            }

            _ = lightingSourceByDevice.Remove(stale);
        }
    }

    /// <summary>
    /// Assembles the state for one pad, sending each light only to hardware
    /// that has it.
    ///
    /// <para>
    /// Gated on capability rather than sent to everything because the
    /// queue's dedupe compares whole states: an animated colour published
    /// to an Xbox pad changes the state every frame, and each "change"
    /// re-sends motor values to a pad that has no light to show it. When
    /// nothing is known about a pad (<see cref="ControllerFeatures.None"/>)
    /// the colour still goes out, as it always did; the DualSense-only
    /// lights do not, because they cannot reach anything else anyway.
    /// </para>
    /// </summary>
    internal static ControllerEffectState BuildState(
        ControllerFeatures features,
        double lowFrequencyRumble,
        double highFrequencyRumble,
        double leftTriggerRumble,
        double rightTriggerRumble,
        in ResolvedLighting lighting,
        AdaptiveTriggerCommand leftTrigger,
        AdaptiveTriggerCommand rightTrigger)
    {
        var bar = lighting.Lightbar;
        var hasPlayerLeds = features.HasAny(ControllerFeatures.PlayerLeds);

        return new ControllerEffectState
        {
            LowFrequencyRumble = lowFrequencyRumble,
            HighFrequencyRumble = highFrequencyRumble,
            LeftTriggerRumble = leftTriggerRumble,
            RightTriggerRumble = rightTriggerRumble,
            LedColor = features.Allows(ControllerFeatureSet.AnyMainLight)
                ? new EffectColor(bar.R, bar.G, bar.B)
                : null,
            PlayerLeds = hasPlayerLeds ? lighting.PlayerLeds : null,
            PlayerLedBrightness = hasPlayerLeds && features.HasAny(ControllerFeatures.MicLed)
                ? lighting.PlayerLedBrightness
                : null,
            MicLed = features.HasAny(ControllerFeatures.MicLed) ? lighting.MicLed : null,
            LeftTrigger = leftTrigger,
            RightTrigger = rightTrigger,
        };
    }

    /// <summary>
    /// Impulse-trigger levels for this pad, 0–1 each. Mirrors the already
    /// resolved body rumble (so disabling rumble or turning the gain down
    /// quietens the triggers too), heavy motor to the left trigger and
    /// light motor to the right, then applies the trigger gain.
    /// </summary>
    internal static (double Left, double Right) ResolveTriggerRumble(
        RumbleSettings settings, ControllerFeatures features, double low, double high)
    {
        if (settings.TriggerRumbleSource != TriggerRumbleSource.Motors
            || !features.HasAny(ControllerFeatures.TriggerRumble))
        {
            return (0d, 0d);
        }

        var gain = Math.Clamp(settings.TriggerRumbleGain, 0f, 2f);
        return (Math.Clamp(low * gain, 0d, 1d), Math.Clamp(high * gain, 0d, 1d));
    }

    /// <summary>
    /// Logs when a pad's lightbar changes hands — the game takes over, an
    /// external program does, the battery alert starts. "The light did
    /// something I did not set" is otherwise unexplainable from outside.
    /// </summary>
    private void LogLightingSourceChange(string deviceId, string? displayName, LightingSource source)
    {
        if (lightingSourceByDevice.TryGetValue(deviceId, out var previous) && previous == source)
        {
            return;
        }

        lightingSourceByDevice[deviceId] = source;
        if (previous == default && source == LightingSource.Configured)
        {
            return;
        }

        logger.LogInformation(
            "Lighting for {Device} now comes from {Source}.",
            displayName ?? deviceId,
            source switch
            {
                LightingSource.Game => "the game",
                LightingSource.External => "the external lighting API",
                LightingSource.LowBattery => "the low-battery alert",
                _ => "its configured mode",
            });
    }

    /// <summary>
    /// Tracks the low-battery flag with hysteresis and logs each
    /// transition. Separate thresholds for entering and leaving so a
    /// reading sitting on the boundary does not flap the warning on and
    /// off — SDL reports these in coarse steps.
    /// </summary>
    private void UpdateLowBatteryState(string deviceId, InputDeviceInfo? info)
    {
        if (info is null || !info.HasBattery || info.BatteryState == DeviceBatteryState.Charging)
        {
            lock (lowBatteryByDevice)
            {
                if (lowBatteryByDevice.TryGetValue(deviceId, out var wasLow) && wasLow)
                {
                    lowBatteryByDevice[deviceId] = false;
                    logger.LogInformation("{Device} is charging; low-battery warning cleared.", info?.DisplayName ?? deviceId);
                }
            }

            return;
        }

        var percent = info.BatteryPercentage ?? 100;

        lock (lowBatteryByDevice)
        {
            var wasLow = lowBatteryByDevice.TryGetValue(deviceId, out var existing) && existing;

            if (!wasLow && percent <= LowBatteryPercent)
            {
                lowBatteryByDevice[deviceId] = true;
                logger.LogWarning(
                    "{Device} battery is low: {Percent}%. With the low-battery alert on, the light pulses red until it is charged.",
                    info.DisplayName, percent);
            }
            else if (wasLow && percent >= LowBatteryClearPercent)
            {
                lowBatteryByDevice[deviceId] = false;
                logger.LogInformation("{Device} battery recovered to {Percent}%.", info.DisplayName, percent);
            }
        }
    }

    /// <summary>
    /// Turns a resolved adaptive-trigger effect into the device-neutral
    /// command the queue carries.
    ///
    /// <para>
    /// Takes the RESOLVED effect rather than the saved settings so that a
    /// trigger linked to rumble and a trigger tuned statically travel the
    /// same path from here on. The link is a Core decision; this is the
    /// unit conversion.
    /// </para>
    ///
    /// <para>
    /// <see cref="AdaptiveTriggerMode.Off"/> is an explicit command, not
    /// a null/absent section: the firmware retains its previous effect
    /// until a release report reaches it.
    /// </para>
    /// </summary>
    internal static AdaptiveTriggerCommand ToCommand(ResolvedAdaptiveTrigger settings)
    {
        // One effect per mode. This used to narrow seven modes onto four
        // effects and the plan widened them back, so the round trip was
        // lossy in the middle of the pipeline: choosing Multiple-position
        // feedback or Slope feedback produced plain Constant resistance,
        // and choosing Multiple-position vibration produced plain
        // Vibration. The firmware distinguishes all of them and the
        // DualSense encoder already writes distinct effect ids, so the
        // narrowing threw away detail nothing downstream wanted lost —
        // silently, with the UI still showing the mode that had been
        // picked.
        var effect = settings.Mode switch
        {
            AdaptiveTriggerMode.Off => AdaptiveTriggerEffect.Off,
            AdaptiveTriggerMode.Feedback => AdaptiveTriggerEffect.Constant,
            AdaptiveTriggerMode.Weapon => AdaptiveTriggerEffect.Section,
            AdaptiveTriggerMode.Vibration => AdaptiveTriggerEffect.Vibration,
            AdaptiveTriggerMode.SlopeFeedback => AdaptiveTriggerEffect.SlopeFeedback,
            AdaptiveTriggerMode.MultiplePositionFeedback
                => AdaptiveTriggerEffect.MultiplePositionFeedback,
            AdaptiveTriggerMode.MultiplePositionVibration
                => AdaptiveTriggerEffect.MultiplePositionVibration,
            _ => AdaptiveTriggerEffect.Constant,
        };

        static byte Scale(float unit) => (byte)Math.Clamp(Math.Round(unit * 255), 0, 255);

        return new AdaptiveTriggerCommand(
            effect,
            Scale(settings.StartPosition),
            Scale(settings.EndPosition),
            Scale(settings.Strength),
            (byte)Math.Clamp(settings.FrequencyHz, 1, 255));
    }

    private bool IsLow(string deviceId)
    {
        lock (lowBatteryByDevice)
        {
            return lowBatteryByDevice.TryGetValue(deviceId, out var low) && low;
        }
    }
}
