using System.Buffers.Binary;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Native;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>Checks the native gamepad wire format.</summary>
public sealed class NativeGamepadReportTests
{
    [Fact]
    public void EncodeEmptySnapshotReleasesAllControls()
    {
        Span<byte> report = stackalloc byte[NativeGamepadReport.Length];

        NativeGamepadReport.Encode(ControllerSnapshot.Empty(), report);

        Assert.Equal(8, report[16]);
        Assert.Equal(new byte[16], report[..16].ToArray());
    }

    [Fact]
    public void EncodeSnapshotClampsAxesAndMapsButtons()
    {
        var buttons = ButtonMask.Empty;
        buttons[ButtonId.South] = true;
        buttons[ButtonId.DpadUp] = true;
        buttons[ButtonId.DpadRight] = true;
        var snapshot = ControllerSnapshot.Empty() with
        {
            Buttons = buttons,
            LeftStick = new StickVector(-2, float.NaN),
            RightStick = new StickVector(float.PositiveInfinity, 2),
            LeftTrigger = 2,
            RightTrigger = -1,
        };
        Span<byte> report = stackalloc byte[NativeGamepadReport.Length];

        NativeGamepadReport.Encode(snapshot, report);

        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(report));
        Assert.Equal(-32767, BinaryPrimitives.ReadInt16LittleEndian(report[4..]));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(report[6..]));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(report[8..]));
        Assert.Equal(-32767, BinaryPrimitives.ReadInt16LittleEndian(report[10..]));
        Assert.Equal(32767, BinaryPrimitives.ReadUInt16LittleEndian(report[12..]));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(report[14..]));
        Assert.Equal(1, report[16]);
    }

    [Fact]
    public void EncodeOppositeDirectionsReturnsNeutralHat()
    {
        var buttons = ButtonMask.Empty;
        buttons[ButtonId.DpadUp] = true;
        buttons[ButtonId.DpadDown] = true;
        Span<byte> report = stackalloc byte[NativeGamepadReport.Length];

        NativeGamepadReport.Encode(ControllerSnapshot.Empty() with { Buttons = buttons }, report);

        Assert.Equal(8, report[16]);
    }
}
