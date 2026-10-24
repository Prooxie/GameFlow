namespace GameFlow.Core.Models;

/// <summary>
/// What one physical controller can actually do beyond sticks and
/// buttons — the hardware a tuning setting would need in order to mean
/// anything.
///
/// <para>
/// <see cref="None"/> is "not known", NOT "can do nothing". A pad that has
/// never been opened, or hardware nobody recognises, answers None and every
/// consumer treats that as "show everything": hiding a lightbar section
/// from a DualSense because detection had not run yet would read as the
/// feature being missing, which is worse than briefly offering a setting a
/// pad lacks. Known hardware is never None — even a bare generic pad
/// answers <see cref="Rumble"/>.
/// </para>
///
/// <para>
/// Bits are pinned. The value is never persisted today, but it crosses the
/// Core/Infrastructure/App boundary and is cheap to keep stable.
/// </para>
/// </summary>
[Flags]
public enum ControllerFeatures : uint
{
    /// <summary>Unknown — not yet detected and not in the hardware catalog.</summary>
    None = 0,

    /// <summary>The two body motors (low / high frequency).</summary>
    Rumble = 1u << 0,

    /// <summary>Separate motors in each trigger — Xbox One / Series impulse triggers.</summary>
    TriggerRumble = 1u << 1,

    /// <summary>Motorised trigger resistance — DualSense and DualSense Edge.</summary>
    AdaptiveTriggers = 1u << 2,

    /// <summary>A colour lightbar or LED (DualSense, DualShock 4).</summary>
    RgbLight = 1u << 3,

    /// <summary>A single-colour LED whose brightness can be set (Switch Home button, some Xbox guide LEDs).</summary>
    MonoLight = 1u << 4,

    /// <summary>A row of player-number LEDs (DualSense, DualShock 3, Switch).</summary>
    PlayerLeds = 1u << 5,

    /// <summary>The DualSense microphone-mute LED.</summary>
    MicLed = 1u << 6,

    /// <summary>
    /// The DualShock 4's hardware lightbar flash — games ask for a blink
    /// rate rather than animating the colour themselves, so honouring it
    /// is what makes "flash red at low health" work on that pad.
    /// </summary>
    LightbarFlash = 1u << 7,

    /// <summary>A finger-tracking touch surface.</summary>
    Touchpad = 1u << 8,

    /// <summary>Gyroscope and/or accelerometer.</summary>
    Motion = 1u << 9,

    /// <summary>A battery whose charge the pad reports.</summary>
    Battery = 1u << 10,
}

/// <summary>Helpers for reading <see cref="ControllerFeatures"/> without repeating the "None means unknown" rule.</summary>
public static class ControllerFeatureSet
{
    /// <summary>Every lighting-related capability.</summary>
    public const ControllerFeatures AnyLighting =
        ControllerFeatures.RgbLight | ControllerFeatures.MonoLight
        | ControllerFeatures.PlayerLeds | ControllerFeatures.MicLed | ControllerFeatures.LightbarFlash;

    /// <summary>A lightbar or LED whose colour or brightness can be driven.</summary>
    public const ControllerFeatures AnyMainLight = ControllerFeatures.RgbLight | ControllerFeatures.MonoLight;

    /// <summary>True when the set was actually detected or catalogued.</summary>
    public static bool IsKnown(this ControllerFeatures features) => features != ControllerFeatures.None;

    /// <summary>
    /// True when the pad has <paramref name="feature"/> — or when nothing is
    /// known about the pad, so the caller should not hide anything.
    /// </summary>
    public static bool Allows(this ControllerFeatures features, ControllerFeatures feature) =>
        features == ControllerFeatures.None || (features & feature) != 0;

    /// <summary>True only when the pad is KNOWN to have any of <paramref name="feature"/>.</summary>
    public static bool HasAny(this ControllerFeatures features, ControllerFeatures feature) =>
        (features & feature) != 0;

    /// <summary>
    /// Human names in a stable order, for the "this controller supports"
    /// line. Kept in Core so the wording is the same wherever a pad is
    /// described, and testable without a UI.
    /// </summary>
    public static IReadOnlyList<string> Describe(ControllerFeatures features)
    {
        var names = new List<string>();
        if (features == ControllerFeatures.None)
        {
            return names;
        }

        void Add(ControllerFeatures flag, string name)
        {
            if ((features & flag) != 0)
            {
                names.Add(name);
            }
        }

        Add(ControllerFeatures.Rumble, "rumble");
        Add(ControllerFeatures.TriggerRumble, "impulse-trigger rumble");
        Add(ControllerFeatures.AdaptiveTriggers, "adaptive triggers");
        Add(ControllerFeatures.RgbLight, "colour lightbar");
        Add(ControllerFeatures.LightbarFlash, "lightbar flash");
        Add(ControllerFeatures.MonoLight, "LED brightness");
        Add(ControllerFeatures.PlayerLeds, "player LEDs");
        Add(ControllerFeatures.MicLed, "mic-mute LED");
        Add(ControllerFeatures.Touchpad, "touchpad");
        Add(ControllerFeatures.Motion, "motion sensors");
        Add(ControllerFeatures.Battery, "battery");
        return names;
    }
}
