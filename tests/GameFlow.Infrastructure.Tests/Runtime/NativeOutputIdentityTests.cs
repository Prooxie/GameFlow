using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.Native;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>Checks native output provenance without hiding physical pads that share identifiers.</summary>
public sealed class NativeOutputIdentityTests
{
    [Theory]
    [InlineData("GameFlow Virtual Gamepad", 0xBEEF, 0xF001, true)]
    [InlineData("Physical gamepad", 0xBEEF, 0xF001, false)]
    [InlineData("GameFlow Virtual Gamepad", 0x054C, 0x0CE6, false)]
    public void IsNativeOutputRequiresProductMarkerAndIdentifiers(string name, ushort vendor, ushort product, bool expected)
    {
        var actual = VirtualDeviceIdentity.IsNativeOutput(name, vendor, product);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsVirtualAcceptsNativeSerialWithoutPath()
    {
        var actual = VirtualDeviceIdentity.IsVirtual(null, "gameflow-output-123456");

        Assert.True(actual);
    }
}
