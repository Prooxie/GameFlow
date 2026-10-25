using System.Buffers.Binary;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Native;

/// <summary>Encodes a standard HID gamepad with six axes, a hat, and nineteen mapped buttons.</summary>
public static class NativeGamepadReport
{
    /// <summary>Report size in bytes. The report has no report identifier.</summary>
    public const int Length = 17;

    /// <summary>Vendor identifier reserved by GameFlow for its custom gamepads.</summary>
    public const ushort VendorId = 0xBEEF;

    /// <summary>Product identifier of the standard native gamepad.</summary>
    public const ushort ProductId = 0xF001;

    /// <summary>Product marker used with the identifiers to exclude native outputs from input discovery.</summary>
    public const string ProductName = "GameFlow Virtual Gamepad";

    /// <summary>Button order shared by the HID descriptor and the Linux event writer.</summary>
    internal static ReadOnlySpan<ButtonId> Buttons =>
    [
        ButtonId.South, ButtonId.East, ButtonId.West, ButtonId.North,
        ButtonId.LeftShoulder, ButtonId.RightShoulder, ButtonId.Back, ButtonId.Start,
        ButtonId.Guide, ButtonId.LeftStick, ButtonId.RightStick,
        ButtonId.LeftTriggerButton, ButtonId.RightTriggerButton,
        ButtonId.Paddle1, ButtonId.Paddle2, ButtonId.Paddle3, ButtonId.Paddle4,
        ButtonId.Touchpad, ButtonId.Misc1,
    ];

    /// <summary>Standard HID descriptor. Stick Y axes increase downwards. Trigger axes range from zero to 32767.</summary>
    public static ReadOnlySpan<byte> Descriptor =>
    [
        0x05, 0x01, 0x09, 0x05, 0xA1, 0x01,
        0x05, 0x09, 0x19, 0x01, 0x29, 0x20, 0x15, 0x00, 0x25, 0x01,
        0x75, 0x01, 0x95, 0x20, 0x81, 0x02,
        0x05, 0x01, 0x09, 0x30, 0x09, 0x31, 0x09, 0x33, 0x09, 0x34,
        0x16, 0x01, 0x80, 0x26, 0xFF, 0x7F, 0x75, 0x10, 0x95, 0x04, 0x81, 0x02,
        0x09, 0x32, 0x09, 0x35, 0x15, 0x00, 0x26, 0xFF, 0x7F, 0x95, 0x02, 0x81, 0x02,
        0x09, 0x39, 0x15, 0x00, 0x25, 0x07, 0x35, 0x00, 0x46, 0x3B, 0x01,
        0x65, 0x14, 0x75, 0x04, 0x95, 0x01, 0x81, 0x42,
        0x65, 0x00, 0x75, 0x04, 0x95, 0x01, 0x81, 0x03, 0xC0,
    ];

    /// <summary>Writes one complete report. Non-finite values become zero. Opposite hat directions cancel.</summary>
    public static void Encode(ControllerSnapshot snapshot, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (destination.Length < Length) { throw new ArgumentException("The report buffer is too small.", nameof(destination)); }
        destination[..Length].Clear();
        uint buttons = 0;
        var order = Buttons;
        for (var i = 0; i < order.Length; i++)
        {
            if (snapshot.Buttons[order[i]]) { buttons |= 1u << i; }
        }
        BinaryPrimitives.WriteUInt32LittleEndian(destination, buttons);
        WriteAxis(destination[4..], snapshot.LeftStick.X);
        WriteAxis(destination[6..], -snapshot.LeftStick.Y);
        WriteAxis(destination[8..], snapshot.RightStick.X);
        WriteAxis(destination[10..], -snapshot.RightStick.Y);
        WriteAxis(destination[12..], Math.Max(0, snapshot.LeftTrigger));
        WriteAxis(destination[14..], Math.Max(0, snapshot.RightTrigger));
        var x = (snapshot.Buttons[ButtonId.DpadRight] ? 1 : 0) - (snapshot.Buttons[ButtonId.DpadLeft] ? 1 : 0);
        var y = (snapshot.Buttons[ButtonId.DpadDown] ? 1 : 0) - (snapshot.Buttons[ButtonId.DpadUp] ? 1 : 0);
        destination[16] = (x, y) switch
        {
            (0, -1) => 0,
            (1, -1) => 1,
            (1, 0) => 2,
            (1, 1) => 3,
            (0, 1) => 4,
            (-1, 1) => 5,
            (-1, 0) => 6,
            (-1, -1) => 7,
            _ => 8,
        };
    }

    /// <summary>Converts a finite normalized axis to signed sixteen-bit storage.</summary>
    private static void WriteAxis(Span<byte> destination, float value) =>
        BinaryPrimitives.WriteInt16LittleEndian(destination,
            (short)MathF.Round(Math.Clamp(float.IsFinite(value) ? value : 0, -1, 1) * short.MaxValue));
}
