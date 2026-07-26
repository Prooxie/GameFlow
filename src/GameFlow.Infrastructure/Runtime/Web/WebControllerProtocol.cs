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
