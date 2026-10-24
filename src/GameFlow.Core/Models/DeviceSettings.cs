using System.Text.Json.Serialization;

namespace GameFlow.Core.Models;

/// <summary>Response curve applied to a stick's magnitude after deadzone shaping.</summary>
public enum StickCurve
{
    /// <summary>1:1 — what the hardware reports is what the game gets.</summary>
    Linear,

    /// <summary>Squared — finer control near centre, full speed still reachable at the edge.</summary>
    Precision,

    /// <summary>Square-rooted — reaches high values sooner; twitchier.</summary>
    Aggressive
}

/// <summary>
/// Per-stick input conditioning. Applied to the PHYSICAL reading before
/// any mapping rule sees it, so every rule downstream operates on an
/// already-cleaned signal.
/// </summary>
public sealed record StickSettings
{
    /// <summary>
    /// Magnitude below this reads as fully centred — kills drift on a worn
    /// stick. Off by default: an untuned device has to be a true
    /// pass-through, so <see cref="DeviceSettings.Default"/> stays identity
    /// and the tick path can skip conditioning outright. Mapping rules and
    /// the games themselves already apply their own deadzones; a silent one
    /// here would stack underneath every one of them.
    /// </summary>
    [JsonPropertyName("deadzone")]
    public float Deadzone { get; init; }

    /// <summary>
    /// Minimum output magnitude once outside the deadzone. Games that
    /// apply their OWN deadzone can swallow small movements entirely;
    /// raising this pushes past that so the stick responds immediately.
    /// </summary>
    [JsonPropertyName("antiDeadzone")]
    public float AntiDeadzone { get; init; }

    /// <summary>Magnitude at which output saturates to full. Below 1.0 means a worn stick that no longer reaches its corners can still hit 100%.</summary>
    [JsonPropertyName("fullAt")]
    public float FullAt { get; init; } = 1.0f;

    [JsonPropertyName("sensitivity")]
    public float Sensitivity { get; init; } = 1.0f;

    [JsonPropertyName("curve")]
    public StickCurve Curve { get; init; } = StickCurve.Linear;

    [JsonPropertyName("invertX")]
    public bool InvertX { get; init; }

    [JsonPropertyName("invertY")]
    public bool InvertY { get; init; }
}

/// <summary>Per-trigger input conditioning, same "applied before rules" contract as <see cref="StickSettings"/>.</summary>
public sealed record TriggerSettings
{
    [JsonPropertyName("deadzone")]
    public float Deadzone { get; init; }

    [JsonPropertyName("fullAt")]
    public float FullAt { get; init; } = 1.0f;

    [JsonPropertyName("sensitivity")]
    public float Sensitivity { get; init; } = 1.0f;

    [JsonPropertyName("invert")]
    public bool Invert { get; init; }
}

/// <summary>Rumble scaling for the assigned physical pad.</summary>
public sealed record RumbleSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>Overall multiplier, 0..2. Above 1 amplifies a weak pad; 0 silences it without touching the game.</summary>
    [JsonPropertyName("gain")]
    public float Gain { get; init; } = 1.0f;

    [JsonPropertyName("lowFrequencyGain")]
    public float LowFrequencyGain { get; init; } = 1.0f;

    [JsonPropertyName("highFrequencyGain")]
    public float HighFrequencyGain { get; init; } = 1.0f;

    /// <summary>Swaps the two motors — some pads wire them opposite to what games assume.</summary>
    [JsonPropertyName("swapMotors")]
    public bool SwapMotors { get; init; }

    /// <summary>
    /// What drives the impulse-trigger motors of an Xbox One / Series pad.
    /// Off by default so a pad that never had trigger rumble does not
    /// suddenly gain it; ignored on pads without those motors.
    /// </summary>
    [JsonPropertyName("triggerRumbleSource")]
    public TriggerRumbleSource TriggerRumbleSource { get; init; } = TriggerRumbleSource.Off;

    /// <summary>
    /// Impulse-trigger strength, 0..2. Separate from <see cref="Gain"/>
    /// because trigger motors sit under the fingers: the level that feels
    /// right in the grips is usually too much in the triggers.
    /// </summary>
    [JsonPropertyName("triggerRumbleGain")]
    public float TriggerRumbleGain { get; init; } = 1.0f;
}

/// <summary>Where impulse-trigger rumble comes from.</summary>
public enum TriggerRumbleSource
{
    /// <summary>The trigger motors stay still.</summary>
    Off,

    /// <summary>
    /// The game's body rumble is mirrored into the triggers: left trigger
    /// follows the heavy motor, right trigger the light one. Games reach a
    /// virtual pad through XInput, which has no trigger channel at all, so
    /// this is how a game's rumble reaches a real Xbox pad's triggers.
    /// </summary>
    Motors,
}

/// <summary>What the player-indicator LED row shows (DualSense, DualShock 3, Switch).</summary>
public enum PlayerLedMode
{
    /// <summary>All player LEDs dark.</summary>
    Off,

    /// <summary>The slot number, in the console's own pattern.</summary>
    PlayerNumber,

    /// <summary>One LED per fifth of charge, like a phone's battery bar.</summary>
    Battery,

    /// <summary>Whatever the game asked the virtual pad to show; the slot number until it asks.</summary>
    Game,

    /// <summary>The fixed pattern in <see cref="LightingSettings.PlayerLedMask"/>.</summary>
    Custom,
}

/// <summary>What the DualSense microphone-mute LED does.</summary>
public enum MicLedMode
{
    Off,
    On,

    /// <summary>The firmware's own slow pulse — a "something needs attention" signal.</summary>
    Pulse,

    /// <summary>Follows the game's mute LED requests; off until it sends one.</summary>
    Game,
}

/// <summary>How a DualSense-class lightbar behaves when no game is driving it.</summary>
public enum LightbarMode
{
    /// <summary>Light off entirely.</summary>
    Off,

    /// <summary>One fixed colour, from <see cref="LightingSettings.Color"/>.</summary>
    Solid,

    /// <summary>The console-style per-player colour, keyed on slot index.</summary>
    PlayerNumber,

    /// <summary>Smooth fade in and out around the configured colour.</summary>
    Breathing,

    /// <summary>Green through amber to red as the pad discharges.</summary>
    BatteryLevel,

    // ── Added with the effects backend ───────────────────────────────
    // Existing values keep their ordinal positions: the enum is
    // serialised into profiles by NAME, but reordering would still
    // invalidate any integer that leaked into an older file.

    /// <summary>Hard on/off blink. Deliberately harsh — meant to be noticed.</summary>
    Strobe,

    /// <summary>Continuous hue sweep through the spectrum.</summary>
    Rainbow,

    /// <summary>
    /// Brightness follows rumble, so the light reacts to what the game is
    /// doing rather than to a timer.
    /// </summary>
    RumbleReactive,

    /// <summary>
    /// Red while a trigger is held, configured colour otherwise — a
    /// firing indicator.
    /// </summary>
    TriggerReactive,

    // ── Added with per-controller lighting ───────────────────────────
    // Appended, never inserted: device-settings.json stores this enum as
    // a NUMBER, so every value above must keep its ordinal.

    /// <summary>
    /// The game decides. Whatever lightbar colour the game sends to the
    /// virtual DualSense / DualShock 4 is shown, for as long as it last
    /// asked, with no idle fallback. Until the game sends anything the
    /// slot colour is shown.
    /// </summary>
    GameControlled,

    /// <summary>
    /// Another program decides, through the external lighting API
    /// (Options → Network &amp; phones) — Home Assistant, a Wallpaper
    /// Engine helper, a script. Game colours are ignored; while nothing
    /// external is active the configured colour is shown.
    /// </summary>
    External,
}

public sealed record LightingSettings
{
    [JsonPropertyName("mode")]
    public LightbarMode Mode { get; init; } = LightbarMode.PlayerNumber;

    /// <summary>Hex RGB used by <see cref="LightbarMode.Solid"/> and as the base for <see cref="LightbarMode.Breathing"/>.</summary>
    [JsonPropertyName("color")]
    public string Color { get; init; } = "#0066FF";

    /// <summary>
    /// The second colour of the two-colour modes: what
    /// <see cref="LightbarMode.Strobe"/> alternates with, what
    /// <see cref="LightbarMode.Breathing"/> fades to, and the firing colour
    /// of <see cref="LightbarMode.TriggerReactive"/>. Empty keeps each
    /// mode's original behaviour (dark, a dim floor, red), so profiles
    /// saved before this existed look exactly as they did.
    /// </summary>
    [JsonPropertyName("secondaryColor")]
    public string SecondaryColor { get; init; } = string.Empty;

    /// <summary>0..1.</summary>
    [JsonPropertyName("brightness")]
    public float Brightness { get; init; } = 1.0f;

    /// <summary>
    /// Player-indicator LED row brightness, 0..1. The DualSense has three
    /// firmware steps, so this is rounded to bright / medium / dim; 0
    /// switches the row off.
    /// </summary>
    [JsonPropertyName("indicatorBrightness")]
    public float IndicatorBrightness { get; init; } = 1.0f;

    /// <summary>
    /// Speed multiplier for the animated modes, 0.25..4. One number for
    /// all of them: a user who finds breathing too slow finds the rainbow
    /// too slow as well.
    /// </summary>
    [JsonPropertyName("animationSpeed")]
    public float AnimationSpeed { get; init; } = 1.0f;

    /// <summary>
    /// Lets a game's own lightbar colour take over from the configured
    /// mode while the game is actively sending one — the "flash red at low
    /// health" a game already does on a real DualSense or DualShock 4.
    /// On by default: a game that drives its lightbar is the case the
    /// player expects to see.
    /// </summary>
    [JsonPropertyName("followGame")]
    public bool FollowGame { get; init; } = true;

    /// <summary>
    /// Seconds after the game's LAST lightbar update before the configured
    /// mode comes back. Most games set a colour once on start-up, which
    /// would otherwise freeze the configured mode out for the whole
    /// session; games that animate their lightbar keep re-sending and stay
    /// in control. 0 holds the game's colour indefinitely.
    /// </summary>
    [JsonPropertyName("gameIdleFallbackSeconds")]
    public float GameIdleFallbackSeconds { get; init; } = 10f;

    /// <summary>
    /// Pulse the light red when the pad's battery runs low, over every
    /// other mode. On by default — a pad dying mid-session is worse than a
    /// lightbar briefly ignoring its setting — but it was previously
    /// impossible to turn off, which is wrong for a pad that is always
    /// near a cable.
    /// </summary>
    [JsonPropertyName("lowBatteryAlert")]
    public bool LowBatteryAlert { get; init; } = true;

    [JsonPropertyName("playerLedMode")]
    public PlayerLedMode PlayerLedMode { get; init; } = PlayerLedMode.PlayerNumber;

    /// <summary>
    /// Five-bit pattern for <see cref="PlayerLedMode.Custom"/>, bit 0 the
    /// leftmost LED — the DualSense's own layout. Pads with four LEDs show
    /// the nearest player number.
    /// </summary>
    [JsonPropertyName("playerLedMask")]
    public int PlayerLedMask { get; init; } = 0b00100;

    [JsonPropertyName("micLedMode")]
    public MicLedMode MicLedMode { get; init; } = MicLedMode.Off;
}

/// <summary>DualSense adaptive trigger effect. Values mirror the hardware's own effect set.</summary>
public enum AdaptiveTriggerMode
{
    Off,
    Feedback,
    Weapon,
    Vibration,
    SlopeFeedback,
    MultiplePositionFeedback,
    MultiplePositionVibration
}

/// <summary>
/// Ties an adaptive trigger to the rumble the game is currently asking
/// for, so the trigger reacts to play instead of holding one configured
/// feel forever.
///
/// <para>
/// Both linked modes read the OVERALL rumble level — the louder of the two
/// motors — rather than pairing left trigger to the low motor and right to
/// the high. Splitting them reads well on paper and fails in practice: a
/// game that drives only one motor would leave one trigger permanently
/// dead, and from the outside that is indistinguishable from the feature
/// not working.
/// </para>
/// </summary>
public enum TriggerFeedbackLink
{
    /// <summary>No link — the configured effect is sent exactly as tuned.</summary>
    None,

    /// <summary>
    /// Resistance tracks rumble: free travel when the game is quiet,
    /// rising to the configured <see cref="AdaptiveTriggerSettings.Strength"/>
    /// at full rumble. The configured mode still decides the SHAPE of the
    /// resistance; the link only moves how hard it pushes back.
    /// </summary>
    Resistance,

    /// <summary>
    /// The trigger's own actuator buzzes along with the game's rumble —
    /// what an Xbox title's impulse triggers would have done, routed onto
    /// a DualSense. While the game is quiet the configured effect applies
    /// unchanged, so a trigger tuned to resist still resists between
    /// events.
    /// </summary>
    Vibration
}

public sealed record AdaptiveTriggerSettings
{
    [JsonPropertyName("mode")]
    public AdaptiveTriggerMode Mode { get; init; } = AdaptiveTriggerMode.Off;

    /// <summary>Where along the pull the effect starts, 0..1.</summary>
    [JsonPropertyName("startPosition")]
    public float StartPosition { get; init; } = 0.2f;

    /// <summary>Where it ends, 0..1. Must exceed <see cref="StartPosition"/> to have any effect.</summary>
    [JsonPropertyName("endPosition")]
    public float EndPosition { get; init; } = 0.8f;

    /// <summary>Resistance strength, 0..1.</summary>
    [JsonPropertyName("strength")]
    public float Strength { get; init; } = 0.8f;

    /// <summary>Vibration frequency in Hz for the vibration-based modes.</summary>
    [JsonPropertyName("frequencyHz")]
    public int FrequencyHz { get; init; } = 10;

    /// <summary>
    /// Whether — and how — live game rumble drives this trigger. Defaults
    /// to <see cref="TriggerFeedbackLink.None"/> so an existing profile
    /// keeps the static feel it was tuned for.
    /// </summary>
    [JsonPropertyName("feedbackLink")]
    public TriggerFeedbackLink FeedbackLink { get; init; } = TriggerFeedbackLink.None;

    /// <summary>
    /// How much of the rumble signal reaches the trigger, 0..1. Scales the
    /// linked drive, not the configured <see cref="Strength"/>: at 0.5 a
    /// game at full rumble moves the trigger half as far as it otherwise
    /// would. Separate from <see cref="RumbleSettings.Gain"/> because that
    /// one also changes what the motors do.
    /// </summary>
    [JsonPropertyName("feedbackAmount")]
    public float FeedbackAmount { get; init; } = 1.0f;
}

/// <summary>
/// Everything tunable about ONE physical device as used by ONE slot.
///
/// <para>
/// Deliberately scoped per slot AND per device, not per device alone:
/// the same pad assigned to two slots can be tuned two different ways
/// (a twitchy config on one, a precise one on the other) without the
/// two fighting each other. That's why <see cref="DeviceSettingsKey"/>
/// carries both ids.
/// </para>
///
/// <para>
/// Split by what actually consumes it. Sticks and triggers are INPUT
/// conditioning — <see cref="Pipeline.DeviceSettingsProcessor"/> applies
/// them to the physical snapshot before any mapping rule runs. Rumble,
/// lighting, and adaptive triggers are OUTPUT effects delivered through
/// the dedicated effects thread; SDL writes stay on the worker that owns
/// the physical device handles so Bluetooth transfers never block the
/// mapping tick.
/// </para>
/// </summary>
public sealed record DeviceSettings
{
    [JsonPropertyName("leftStick")]
    public StickSettings LeftStick { get; init; } = new();

    [JsonPropertyName("rightStick")]
    public StickSettings RightStick { get; init; } = new();

    [JsonPropertyName("leftTrigger")]
    public TriggerSettings LeftTrigger { get; init; } = new();

    [JsonPropertyName("rightTrigger")]
    public TriggerSettings RightTrigger { get; init; } = new();

    [JsonPropertyName("rumble")]
    public RumbleSettings Rumble { get; init; } = new();

    [JsonPropertyName("lighting")]
    public LightingSettings Lighting { get; init; } = new();

    [JsonPropertyName("leftAdaptiveTrigger")]
    public AdaptiveTriggerSettings LeftAdaptiveTrigger { get; init; } = new();

    [JsonPropertyName("rightAdaptiveTrigger")]
    public AdaptiveTriggerSettings RightAdaptiveTrigger { get; init; } = new();

    /// <summary>Default-everything instance — used whenever a slot/device pair has never been tuned.</summary>
    public static DeviceSettings Default { get; } = new();
}

/// <summary>Identifies one tuning entry: this device, as used by this slot.</summary>
public readonly record struct DeviceSettingsKey(string SlotId, string DeviceId)
{
    public override string ToString() => $"{SlotId}::{DeviceId}";
}
