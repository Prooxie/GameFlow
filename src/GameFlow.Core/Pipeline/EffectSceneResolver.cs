using GameFlow.Core.Models;

namespace GameFlow.Core.Pipeline;

/// <summary>
/// Everything the animated lighting modes need to know about the pad at
/// this instant. Passed in rather than read, so the resolver stays pure
/// and testable — no clock, no device, no globals.
/// </summary>
/// <param name="ElapsedSeconds">Seconds since the effect engine started. Drives every time-based mode.</param>
/// <param name="PlayerIndex">Zero-based slot index, for <see cref="LightbarMode.PlayerNumber"/>.</param>
/// <param name="BatteryPercent">0–100, or null when the pad does not report one.</param>
/// <param name="IsCharging">Whether the pad is charging, which the battery mode shows differently.</param>
/// <param name="RumbleLevel">Current rumble magnitude 0–1, for <see cref="LightbarMode.RumbleReactive"/>.</param>
/// <param name="TriggerLevel">Highest trigger pull 0–1, for <see cref="LightbarMode.TriggerReactive"/>.</param>
/// <param name="LowBattery">
/// The pad is in its low-battery state. Decided by the caller, which owns
/// the hysteresis that keeps a reading on the boundary from flapping.
/// </param>
/// <param name="Game">What the game last asked the virtual pad's lights to do, or null if it never has.</param>
/// <param name="External">An unexpired request from the external lighting API, or null.</param>
public readonly record struct EffectSceneContext(
    double ElapsedSeconds,
    int PlayerIndex = 0,
    int? BatteryPercent = null,
    bool IsCharging = false,
    double RumbleLevel = 0,
    double TriggerLevel = 0,
    bool LowBattery = false,
    GameLightState? Game = null,
    ExternalLightRequest? External = null);

/// <summary>
/// The lighting a game sent to the VIRTUAL pad — decoded from the
/// DualSense / DualShock 4 output reports it wrote — as of this instant.
/// Every member is optional because games set them independently: most
/// send a colour and never touch the player LEDs.
/// </summary>
/// <param name="Color">The lightbar colour, or null if the game never set one.</param>
/// <param name="ColorAgeSeconds">Seconds since the game last sent <paramref name="Color"/>.</param>
/// <param name="FlashOnSeconds">DualShock 4 hardware flash: lit time per cycle. 0 = no flash.</param>
/// <param name="FlashOffSeconds">DualShock 4 hardware flash: dark time per cycle.</param>
/// <param name="PlayerLeds">DualSense player-LED bits the game asked for.</param>
/// <param name="MicLed">DualSense mic-mute LED state the game asked for.</param>
/// <param name="PlayerLedBrightness">DualSense player-LED brightness step the game asked for.</param>
public readonly record struct GameLightState(
    LightColor? Color,
    double ColorAgeSeconds = 0,
    double FlashOnSeconds = 0,
    double FlashOffSeconds = 0,
    byte? PlayerLeds = null,
    MicLedState? MicLed = null,
    IndicatorBrightnessLevel? PlayerLedBrightness = null);

/// <summary>
/// One request from the external lighting API. Only the modes the API
/// accepts are meaningful here (off, solid, breathing, strobe, rainbow);
/// anything else renders as solid.
/// </summary>
public readonly record struct ExternalLightRequest(LightbarMode Mode, LightColor Color, float Brightness = 1f);

/// <summary>Which layer decided the lightbar colour this frame.</summary>
public enum LightingSource
{
    /// <summary>The mode chosen in the Tuning tab.</summary>
    Configured,

    /// <summary>The game's own lightbar request.</summary>
    Game,

    /// <summary>The external lighting API.</summary>
    External,

    /// <summary>The low-battery alert.</summary>
    LowBattery,
}

/// <summary>DualSense mic-mute LED states, numbered as the firmware numbers them.</summary>
public enum MicLedState : byte
{
    Off = 0,
    On = 1,
    Pulse = 2,
}

/// <summary>DualSense player-LED brightness steps, numbered as the firmware numbers them (0 is the brightest).</summary>
public enum IndicatorBrightnessLevel : byte
{
    Bright = 0,
    Medium = 1,
    Dim = 2,
}

/// <summary>Every light on the pad, decided for this instant.</summary>
/// <param name="Lightbar">
/// The lightbar colour. Never "leave it alone": off is an explicit black,
/// because a missing colour is exactly how "Off" used to leave the bar lit.
/// </param>
/// <param name="Source">Which layer won, for diagnostics.</param>
/// <param name="PlayerLeds">Player-LED bits in the DualSense layout (bit 0 = leftmost of five); 0 = all off.</param>
/// <param name="PlayerLedBrightness">Brightness step for <paramref name="PlayerLeds"/>.</param>
/// <param name="MicLed">Mic-mute LED.</param>
public readonly record struct ResolvedLighting(
    LightColor Lightbar,
    LightingSource Source,
    byte PlayerLeds,
    IndicatorBrightnessLevel PlayerLedBrightness,
    MicLedState MicLed);

/// <summary>
/// One adaptive-trigger effect with any live modulation already folded in
/// — the same five values <see cref="AdaptiveTriggerSettings"/> carries,
/// but describing this instant rather than the saved configuration.
///
/// <para>
/// Deliberately still in Core units (0..1 positions, Hz) rather than the
/// report's bytes. The byte layout is a firmware contract that belongs
/// with the encoder; what belongs here is the decision about WHICH effect
/// to send, which is the part worth testing without a controller.
/// </para>
/// </summary>
public readonly record struct ResolvedAdaptiveTrigger(
    AdaptiveTriggerMode Mode,
    float StartPosition,
    float EndPosition,
    float Strength,
    int FrequencyHz)
{
    public static ResolvedAdaptiveTrigger From(AdaptiveTriggerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ResolvedAdaptiveTrigger(
            settings.Mode,
            settings.StartPosition,
            settings.EndPosition,
            settings.Strength,
            settings.FrequencyHz);
    }
}

/// <summary>An 8-bit RGB triple. Core has no drawing dependency, so this is defined here.</summary>
public readonly record struct LightColor(byte R, byte G, byte B)
{
    public static LightColor Black => new(0, 0, 0);

    /// <summary>Scales all three channels. Used for brightness and for the animated modes.</summary>
    public LightColor Scaled(double factor)
    {
        var f = Math.Clamp(factor, 0d, 1d);
        return new LightColor(
            (byte)Math.Round(R * f),
            (byte)Math.Round(G * f),
            (byte)Math.Round(B * f));
    }

    /// <summary>
    /// Parses <c>#RRGGBB</c> / <c>RRGGBB</c>, tolerating an <c>#AARRGGBB</c>
    /// alpha prefix. Returns false rather than throwing: the value comes
    /// from a hand-editable profile.
    /// </summary>
    public static bool TryParse(string? hex, out LightColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var text = hex.Trim().TrimStart('#');
        if (text.Length == 8)
        {
            text = text[2..];
        }

        if (text.Length != 6)
        {
            return false;
        }

        static bool Hex(ReadOnlySpan<char> span, out byte value) =>
            byte.TryParse(span, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);

        if (Hex(text.AsSpan(0, 2), out var r) &&
            Hex(text.AsSpan(2, 2), out var g) &&
            Hex(text.AsSpan(4, 2), out var b))
        {
            color = new LightColor(r, g, b);
            return true;
        }

        return false;
    }
}

/// <summary>
/// Turns saved lighting and rumble settings into the concrete values a
/// pad should be showing right now.
///
/// <para>
/// Kept in Core, pure, and driven by an explicit
/// <see cref="EffectSceneContext"/> rather than reading a clock: the
/// animated modes are the part most likely to be wrong in a way nobody
/// notices — a breathing curve that never reaches full, a strobe with an
/// uneven duty cycle, a battery colour that reads green at 5% — and none
/// of that is testable if the time source is ambient.
/// </para>
/// </summary>
public static class EffectSceneResolver
{
    /// <summary>Console-style player colours: blue, red, green, magenta.</summary>
    private static readonly LightColor[] PlayerColors =
    [
        new(0x00, 0x66, 0xFF),
        new(0xFF, 0x22, 0x22),
        new(0x22, 0xCC, 0x44),
        new(0xCC, 0x22, 0xCC),
    ];

    private const double BreathingPeriodSeconds = 4.0;
    private const double StrobePeriodSeconds = 0.5;
    private const double RainbowPeriodSeconds = 6.0;

    /// <summary>The low-battery pulse period — faster than breathing, so it reads as an alert.</summary>
    private const double LowBatteryPeriodSeconds = 1.0;

    /// <summary>
    /// The console's player-number LED patterns, as SDL's PS5 driver lights
    /// them: centre; the inner pair; centre and the outer pair; the four
    /// outside; all five.
    /// </summary>
    private static readonly byte[] PlayerLedPatterns = [0x04, 0x0A, 0x15, 0x1B, 0x1F];

    /// <summary>The five player LEDs.</summary>
    public const byte AllPlayerLeds = 0x1F;

    /// <summary>
    /// Every light on the pad, with the layers applied in order:
    /// the low-battery alert (when enabled), then an external API request,
    /// then the game's own lightbar colour (when followed and recent), and
    /// finally the configured mode.
    ///
    /// <para>
    /// The user's <see cref="LightingSettings.Brightness"/> dims every
    /// layer except the alert. It is the one setting that means "how bright
    /// do I want this bar", and a game or a home-automation script ignoring
    /// it would make the slider look broken.
    /// </para>
    /// </summary>
    public static ResolvedLighting ResolveLighting(LightingSettings settings, in EffectSceneContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var (lightbar, source) = ResolveLightbar(settings, context);
        var (playerLeds, playerLedBrightness) = ResolvePlayerLeds(settings, context);
        return new ResolvedLighting(lightbar, source, playerLeds, playerLedBrightness, ResolveMicLed(settings, context));
    }

    private static (LightColor Color, LightingSource Source) ResolveLightbar(
        LightingSettings settings, in EffectSceneContext context)
    {
        if (settings.LowBatteryAlert && context.LowBattery)
        {
            return (LowBatteryPulse(context.ElapsedSeconds), LightingSource.LowBattery);
        }

        var brightness = Math.Clamp(settings.Brightness, 0f, 1f);

        if (context.External is { } external)
        {
            var color = Animate(
                external.Mode, external.Color, secondary: null, speed: 1d, context);
            return (color.Scaled(Math.Clamp(external.Brightness, 0f, 1f) * brightness), LightingSource.External);
        }

        if (settings.Mode != LightbarMode.External
            && context.Game is { Color: { } gameColor } game
            && FollowsGame(settings, game.ColorAgeSeconds))
        {
            return (GameFlash(gameColor, game, context.ElapsedSeconds).Scaled(brightness), LightingSource.Game);
        }

        return (ResolveLight(settings, context), LightingSource.Configured);
    }

    /// <summary>
    /// Whether the game's colour should be on the bar. In
    /// <see cref="LightbarMode.GameControlled"/> it always is; otherwise
    /// only while following is on and the game sent it recently enough.
    /// </summary>
    private static bool FollowsGame(LightingSettings settings, double ageSeconds)
    {
        if (settings.Mode == LightbarMode.GameControlled)
        {
            return true;
        }

        if (!settings.FollowGame)
        {
            return false;
        }

        var fallback = settings.GameIdleFallbackSeconds;
        return fallback <= 0 || ageSeconds <= fallback;
    }

    /// <summary>
    /// Plays a DualShock 4 hardware flash. The DS4 firmware blinks the bar
    /// itself when a game asks; GameFlow sees only the request, so the blink
    /// is reproduced here — on any colour light, not just a DS4's.
    /// </summary>
    private static LightColor GameFlash(LightColor color, in GameLightState game, double elapsedSeconds)
    {
        if (game.FlashOnSeconds <= 0 || game.FlashOffSeconds <= 0)
        {
            return color;
        }

        var cycle = game.FlashOnSeconds + game.FlashOffSeconds;
        return Phase(elapsedSeconds, cycle) * cycle < game.FlashOnSeconds ? color : LightColor.Black;
    }

    /// <summary>
    /// The colour the configured mode produces — the bottom layer of
    /// <see cref="ResolveLighting"/>, with no game, external or battery
    /// override applied.
    ///
    /// <para>
    /// <see cref="LightbarMode.Off"/> is black, not "nothing". A missing
    /// colour means "leave the LED as it is" to everything downstream, so
    /// Off used to leave the bar showing whatever it showed last.
    /// </para>
    /// </summary>
    public static LightColor ResolveLight(LightingSettings settings, in EffectSceneContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var baseColor = LightColor.TryParse(settings.Color, out var parsed)
            ? parsed
            : PlayerColors[0];
        LightColor? secondary = LightColor.TryParse(settings.SecondaryColor, out var second)
            ? second
            : null;
        var speed = Math.Clamp((double)settings.AnimationSpeed, 0.25d, 4d);

        var mode = settings.Mode switch
        {
            // Nothing from the game yet: show the slot's colour, so the pad
            // is identifiable while it waits.
            LightbarMode.GameControlled => LightbarMode.PlayerNumber,

            // Nothing external active: the configured colour.
            LightbarMode.External => LightbarMode.Solid,

            var other => other,
        };

        var color = Animate(mode, baseColor, secondary, speed, context);

        // Brightness applies last so it scales whatever the mode produced,
        // including the animated modes.
        var brightness = Math.Clamp(settings.Brightness, 0f, 1f);
        return color.Scaled(brightness);
    }

    /// <summary>The raw colour of one mode at this instant, before brightness.</summary>
    private static LightColor Animate(
        LightbarMode mode,
        LightColor baseColor,
        LightColor? secondary,
        double speed,
        in EffectSceneContext context)
    {
        var elapsed = context.ElapsedSeconds;

        return mode switch
        {
            LightbarMode.Off => LightColor.Black,

            LightbarMode.Solid => baseColor,

            LightbarMode.PlayerNumber => PlayerColor(context.PlayerIndex),

            // Without a second colour it never fades fully to black — a
            // lightbar that goes out looks like a disconnection. Floor at
            // 15%. With one, it fades between the two.
            LightbarMode.Breathing => secondary is { } low
                ? Blend(low, baseColor, Wave(elapsed, BreathingPeriodSeconds / speed))
                : baseColor.Scaled(0.15 + (0.85 * Wave(elapsed, BreathingPeriodSeconds / speed))),

            LightbarMode.BatteryLevel => BatteryColor(context),

            // Square wave, even duty cycle.
            LightbarMode.Strobe =>
                Phase(elapsed, StrobePeriodSeconds / speed) < 0.5 ? baseColor : secondary ?? LightColor.Black,

            LightbarMode.Rainbow => Rainbow(Phase(elapsed, RainbowPeriodSeconds / speed)),

            // Idle floor so the pad is still lit when nothing is shaking.
            LightbarMode.RumbleReactive =>
                baseColor.Scaled(0.2 + (0.8 * Math.Clamp(context.RumbleLevel, 0d, 1d))),

            LightbarMode.TriggerReactive =>
                context.TriggerLevel > 0.05
                    ? (secondary ?? new LightColor(0xFF, 0x22, 0x22)).Scaled(Math.Clamp(context.TriggerLevel, 0.25d, 1d))
                    : baseColor,

            _ => baseColor,
        };
    }

    /// <summary>
    /// Urgent red pulse. Faster than the breathing mode and never fully
    /// dark, so it reads as an alert rather than as an animation someone
    /// chose. Deliberately ignores the brightness slider: an alert dimmed
    /// to nothing is not an alert.
    /// </summary>
    public static LightColor LowBatteryPulse(double elapsedSeconds)
    {
        var level = 0.35 + (0.65 * Wave(elapsedSeconds, LowBatteryPeriodSeconds));
        return new LightColor(0xFF, 0x18, 0x18).Scaled(level);
    }

    /// <summary>The console-style colour for a zero-based slot index.</summary>
    private static LightColor PlayerColor(int playerIndex) =>
        // Wraps rather than going dark past the fourth pad: sixteen
        // slots are supported and an unlit pad reads as broken.
        PlayerColors[((playerIndex % PlayerColors.Length) + PlayerColors.Length) % PlayerColors.Length];

    /// <summary>Linear blend, <paramref name="t"/> 0 = <paramref name="from"/>, 1 = <paramref name="to"/>.</summary>
    private static LightColor Blend(LightColor from, LightColor to, double t)
    {
        var f = Math.Clamp(t, 0d, 1d);
        static byte Mix(byte a, byte b, double f) => (byte)Math.Round(a + ((b - a) * f));
        return new LightColor(Mix(from.R, to.R, f), Mix(from.G, to.G, f), Mix(from.B, to.B, f));
    }

    /// <summary>
    /// The player-number pattern for a zero-based slot index, wrapping past
    /// the fifth pattern like the lightbar colours do.
    /// </summary>
    public static byte PlayerLedPattern(int playerIndex) =>
        PlayerLedPatterns[((playerIndex % PlayerLedPatterns.Length) + PlayerLedPatterns.Length) % PlayerLedPatterns.Length];

    /// <summary>
    /// A battery bar on the player LEDs, filling from the left: one LED per
    /// started fifth, so a pad at 3% still shows one lit LED rather than
    /// reading as off. Null when the pad reports no charge.
    /// </summary>
    public static byte? BatteryLedPattern(int? batteryPercent)
    {
        if (batteryPercent is not { } percent)
        {
            return null;
        }

        var lit = Math.Clamp((int)Math.Ceiling(Math.Clamp(percent, 0, 100) / 20d), 1, 5);
        return (byte)((1 << lit) - 1);
    }

    /// <summary>
    /// The SDL player index that best shows <paramref name="pattern"/> on a
    /// pad whose LEDs SDL drives by NUMBER (DualShock 3, Switch): an exact
    /// console pattern maps to its player, anything else to the number of
    /// lit LEDs. -1 for no LEDs, which SDL treats as "indicator off".
    /// </summary>
    public static int ToPlayerIndex(byte pattern)
    {
        var bits = pattern & AllPlayerLeds;
        if (bits == 0)
        {
            return -1;
        }

        var exact = Array.IndexOf(PlayerLedPatterns, (byte)bits);
        return exact >= 0 ? exact : System.Numerics.BitOperations.PopCount((uint)bits) - 1;
    }

    private static (byte Leds, IndicatorBrightnessLevel Brightness) ResolvePlayerLeds(
        LightingSettings settings, in EffectSceneContext context)
    {
        var level = ToIndicatorLevel(settings.IndicatorBrightness);
        var slotPattern = PlayerLedPattern(context.PlayerIndex);

        var leds = settings.PlayerLedMode switch
        {
            PlayerLedMode.Off => (byte)0,
            PlayerLedMode.Battery => BatteryLedPattern(context.BatteryPercent) ?? slotPattern,
            PlayerLedMode.Custom => (byte)(settings.PlayerLedMask & AllPlayerLeds),
            PlayerLedMode.Game => context.Game?.PlayerLeds is { } fromGame
                ? (byte)(fromGame & AllPlayerLeds)
                : slotPattern,
            _ => slotPattern,
        };

        if (settings.PlayerLedMode == PlayerLedMode.Game && context.Game?.PlayerLedBrightness is { } gameLevel)
        {
            level = gameLevel;
        }

        // The slider at zero means "off", which no firmware step expresses.
        if (settings.IndicatorBrightness <= 0.01f)
        {
            leds = 0;
        }

        return (leds, level);
    }

    /// <summary>
    /// The 0..1 slider as one of the DualSense's three brightness steps.
    /// Equal thirds, so the readout percentage and the step agree.
    /// </summary>
    public static IndicatorBrightnessLevel ToIndicatorLevel(float brightness) =>
        brightness switch
        {
            >= 0.67f => IndicatorBrightnessLevel.Bright,
            >= 0.34f => IndicatorBrightnessLevel.Medium,
            _ => IndicatorBrightnessLevel.Dim,
        };

    private static MicLedState ResolveMicLed(LightingSettings settings, in EffectSceneContext context) =>
        settings.MicLedMode switch
        {
            MicLedMode.On => MicLedState.On,
            MicLedMode.Pulse => MicLedState.Pulse,
            MicLedMode.Game => context.Game?.MicLed ?? MicLedState.Off,
            _ => MicLedState.Off,
        };

    /// <summary>
    /// Applies rumble settings to the raw magnitudes a game asked for,
    /// returning (low, high) in 0–1.
    /// </summary>
    public static (double Low, double High) ResolveRumble(
        RumbleSettings settings, double lowFrequency, double highFrequency)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return (0d, 0d);
        }

        var low = Math.Clamp(lowFrequency, 0d, 1d);
        var high = Math.Clamp(highFrequency, 0d, 1d);

        if (settings.SwapMotors)
        {
            (low, high) = (high, low);
        }

        // Gain is allowed above 1 to rescue a weak pad, so the result is
        // clamped after multiplying rather than trusting the inputs.
        var gain = Math.Max(0d, settings.Gain);
        low = Math.Clamp(low * gain * Math.Max(0d, settings.LowFrequencyGain), 0d, 1d);
        high = Math.Clamp(high * gain * Math.Max(0d, settings.HighFrequencyGain), 0d, 1d);

        return (low, high);
    }

    /// <summary>
    /// Rumble below this is treated as silence. SDL and the games
    /// themselves both leave a motor sitting at a value that rounds to one
    /// or two of 255 rather than a clean zero, and without a floor that
    /// noise would hold a trigger permanently engaged.
    /// </summary>
    private const double FeedbackLinkFloor = 0.02;

    /// <summary>
    /// Steps the linked drive is quantized to. The trigger actuator cannot
    /// resolve finer than this, and every distinct value is another effect
    /// report — over Bluetooth, at the producer's 60 Hz ceiling, that is
    /// the difference between reacting to the game and flooding the link
    /// with changes nobody can feel.
    /// </summary>
    private const double FeedbackLinkSteps = 16;

    /// <summary>
    /// The adaptive-trigger effect a pad should be running right now.
    ///
    /// <para>
    /// With no link configured this is the saved settings unchanged, which
    /// is what every existing profile gets. A link reads
    /// <see cref="EffectSceneContext.RumbleLevel"/> — the level AFTER
    /// <see cref="ResolveRumble"/>, so silencing rumble for a pad silences
    /// what its triggers do too, rather than leaving the triggers reacting
    /// to a game the motors are ignoring.
    /// </para>
    /// </summary>
    public static ResolvedAdaptiveTrigger ResolveAdaptiveTrigger(
        AdaptiveTriggerSettings settings, in EffectSceneContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var resolved = ResolvedAdaptiveTrigger.From(settings);

        if (settings.FeedbackLink == TriggerFeedbackLink.None)
        {
            return resolved;
        }

        var drive = Quantize(
            Math.Clamp(context.RumbleLevel, 0d, 1d) *
            Math.Clamp(settings.FeedbackAmount, 0f, 1f));

        if (drive <= FeedbackLinkFloor)
        {
            // Quiet game. Resistance falls to free travel because that IS
            // the link's resting state; vibration hands back to whatever
            // static effect the user tuned, so the trigger is not dead
            // between events.
            return settings.FeedbackLink == TriggerFeedbackLink.Resistance
                ? resolved with { Strength = 0f }
                : resolved;
        }

        return settings.FeedbackLink switch
        {
            TriggerFeedbackLink.Resistance =>
                resolved with { Strength = (float)(Math.Clamp(settings.Strength, 0f, 1f) * drive) },

            // Overrides the configured mode outright rather than blending:
            // the firmware runs ONE effect per trigger, so a buzz and a
            // resistance curve cannot both be live. Frequency stays the
            // user's, since that is the pitch of the buzz they tuned.
            TriggerFeedbackLink.Vibration =>
                resolved with
                {
                    Mode = AdaptiveTriggerMode.Vibration,
                    Strength = (float)drive,
                },

            _ => resolved,
        };
    }

    /// <summary>Rounds to <see cref="FeedbackLinkSteps"/> discrete levels.</summary>
    private static double Quantize(double value) =>
        Math.Round(value * FeedbackLinkSteps, MidpointRounding.AwayFromZero) / FeedbackLinkSteps;

    /// <summary>Green above 60%, amber to 25%, red below. Charging shows a breathing green.</summary>
    private static LightColor BatteryColor(in EffectSceneContext context)
    {
        if (context.IsCharging)
        {
            return new LightColor(0x22, 0xCC, 0x44)
                .Scaled(0.25 + (0.75 * Wave(context.ElapsedSeconds, BreathingPeriodSeconds)));
        }

        // No reading is not the same as an empty battery: show the neutral
        // player colour rather than a red "about to die" that is not true.
        if (context.BatteryPercent is not { } percent)
        {
            return PlayerColors[0];
        }

        return percent switch
        {
            > 60 => new LightColor(0x22, 0xCC, 0x44),
            > 25 => new LightColor(0xFF, 0xAA, 0x22),
            _ => new LightColor(0xFF, 0x22, 0x22),
        };
    }

    /// <summary>Position within one cycle, 0 to 1.</summary>
    private static double Phase(double elapsedSeconds, double periodSeconds)
    {
        if (periodSeconds <= 0)
        {
            return 0;
        }

        var phase = (elapsedSeconds % periodSeconds) / periodSeconds;
        return phase < 0 ? phase + 1 : phase;
    }

    /// <summary>Smooth 0→1→0 over one period. Cosine, so the turns are gentle.</summary>
    private static double Wave(double elapsedSeconds, double periodSeconds) =>
        (1 - Math.Cos(Phase(elapsedSeconds, periodSeconds) * 2 * Math.PI)) / 2;

    /// <summary>Full-saturation hue sweep. Hand-rolled so Core needs no colour library.</summary>
    private static LightColor Rainbow(double phase)
    {
        var h = Math.Clamp(phase, 0d, 1d) * 6d;
        var sector = (int)Math.Floor(h) % 6;
        var f = h - Math.Floor(h);

        byte Up() => (byte)Math.Round(f * 255);
        byte Down() => (byte)Math.Round((1 - f) * 255);

        return sector switch
        {
            0 => new LightColor(255, Up(), 0),
            1 => new LightColor(Down(), 255, 0),
            2 => new LightColor(0, 255, Up()),
            3 => new LightColor(0, Down(), 255),
            4 => new LightColor(Up(), 0, 255),
            _ => new LightColor(255, 0, Down()),
        };
    }
}
