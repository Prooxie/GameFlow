using System.Text.Json;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>
/// Wire format between the browser gamepad and the runtime.
///
/// <para>
/// <b>The bit order below is a fixed contract</b>, duplicated in the
/// page's JavaScript (see the BIT table there). It is deliberately NOT
/// derived from <see cref="ButtonId"/>'s declaration order: that enum
/// can be reordered during ordinary refactoring, and if it were the
/// wire contract, doing so would silently make every connected phone
/// report the wrong buttons — a bug with no compile error and no
/// obvious cause. Changing anything here means changing the JavaScript
/// in the same commit.
/// </para>
/// </summary>
public static class WebControllerProtocol
{
    /// <summary>Bit index → button. Index IS the wire bit position.</summary>
    private static readonly ButtonId[] BitOrder =
    [
        ButtonId.South,          // 0
        ButtonId.East,           // 1
        ButtonId.West,           // 2
        ButtonId.North,          // 3
        ButtonId.LeftShoulder,   // 4
        ButtonId.RightShoulder,  // 5
        ButtonId.Back,           // 6
        ButtonId.Start,          // 7
        ButtonId.Guide,          // 8
        ButtonId.LeftStick,      // 9
        ButtonId.RightStick,     // 10
        ButtonId.DpadUp,         // 11
        ButtonId.DpadDown,       // 12
        ButtonId.DpadLeft,       // 13
        ButtonId.DpadRight,      // 14
        ButtonId.Touchpad        // 15
    ];

    /// <summary>
    /// Parses one input frame. Returns null on malformed JSON rather
    /// than throwing — the input is a socket message from a phone on
    /// the network, so a garbled or hostile frame must not be able to
    /// take down the receive loop.
    /// </summary>
    public static ControllerSnapshot? TryParseInput(string json, int padIndex)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var buttonMask = ReadInt(root, "b");
            var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
            for (var bit = 0; bit < BitOrder.Length; bit++)
            {
                if ((buttonMask & (1 << bit)) != 0)
                {
                    buttons[BitOrder[bit]] = true;
                }
            }

            return new ControllerSnapshot
            {
                DeviceName = $"Web Controller #{padIndex + 1}",
                Buttons = buttons,
                LeftStick = new StickVector(ReadAxis(root, "lx"), ReadAxis(root, "ly")),
                RightStick = new StickVector(ReadAxis(root, "rx"), ReadAxis(root, "ry")),
                LeftTrigger = ReadUnit(root, "lt"),
                RightTrigger = ReadUnit(root, "rt"),

                // Phone motion. The browser reports rotation in degrees/s
                // and the page converts to radians/s before sending, so
                // these arrive already in SDL's units — meaning a phone
                // drives GyroMapRule (reference frames, smoothing, Aim
                // Engage) exactly like a DualSense, with no phone-specific
                // path anywhere downstream.
                HasGyro = ReadInt(root, "gyro") != 0,
                GyroPitch = ReadSigned(root, "gp"),
                GyroYaw = ReadSigned(root, "gy"),
                GyroRoll = ReadSigned(root, "gr"),
                AccelX = ReadSigned(root, "ax"),
                AccelY = ReadSigned(root, "ay"),
                AccelZ = ReadSigned(root, "az"),

                Timestamp = DateTimeOffset.UtcNow
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Serializes the pad-assignment handshake the browser shows as "PAD #n". -1 means every slot is taken.</summary>
    public static string BuildPadAssignment(int padIndex) =>
        JsonSerializer.Serialize(new { pad = padIndex });

    /// <summary>Serializes a rumble command for the browser's Vibration API.</summary>
    public static string BuildRumble(WebRumbleCommand command) =>
        JsonSerializer.Serialize(new
        {
            rumble = new
            {
                low = command.LowFrequency,
                high = command.HighFrequency,
                ms = command.DurationMs
            }
        });

    private static int ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : 0;

    /// <summary>Signed stick axis, clamped — a phone could send anything, including NaN.</summary>
    private static float ReadAxis(JsonElement root, string name) => ClampFinite(ReadFloat(root, name), -1f, 1f);

    /// <summary>Unsigned trigger, clamped.</summary>
    private static float ReadUnit(JsonElement root, string name) => ClampFinite(ReadFloat(root, name), 0f, 1f);

    private static float ReadFloat(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? (float)parsed : 0f;

    /// <summary>
    /// Signed, UNBOUNDED value for motion (rad/s, m/s²). Unlike sticks
    /// and triggers these have no natural clamp range — a fast flick
    /// legitimately exceeds any fixed bound, and clamping would silently
    /// cap it. Still rejects NaN/infinity, which would otherwise poison
    /// every downstream calculation.
    /// </summary>
    private static float ReadSigned(JsonElement root, string name)
    {
        var value = ReadFloat(root, name);
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }
        // Sanity ceiling only — far beyond any real hand movement, so it
        // never truncates genuine input, but stops a hostile client
        // sending 1e30 and overflowing the maths downstream.
        return Math.Clamp(value, -1000f, 1000f);
    }

    private static float ClampFinite(float value, float min, float max)
    {
        // NaN fails every comparison, so it would slip past a plain
        // Math.Clamp and poison the stick maths downstream.
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }
        return Math.Clamp(value, min, max);
    }
}
