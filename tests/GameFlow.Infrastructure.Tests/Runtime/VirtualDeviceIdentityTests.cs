using GameFlow.Infrastructure.Runtime;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>
/// Covers telling GameFlow's own emitted pads apart from real hardware.
/// </summary>
/// <remarks>
/// The consequence of getting this wrong is not cosmetic. A virtual pad
/// that reads as physical can be assigned as another slot's input, which
/// is a feedback loop, and it gets battery-polled — which is how a
/// wired-profile virtual pad ended up reporting itself at 10% charge.
/// </remarks>
public sealed class VirtualDeviceIdentityTests
{
    /// <summary>
    /// A pad left behind by a run that crashed is not covered by any sink's
    /// published VID/PID, so the enumerator is all that is left to go on.
    /// </summary>
    [Theory]
    [InlineData(@"ROOT\VID_045E&PID_028E&IG_00\0000")]
    [InlineData(@"\\?\ROOT#VID_045E&PID_028E&IG_00#0002#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData(@"SWD\HIDMAESTRO\1305C1BE7CD10002_0000")]
    public void ASoftwareCreatedPadIsRecognisedWithoutAClaim(string path) =>
        Assert.True(VirtualDeviceIdentity.IsVirtual(path, serial: null));

    /// <summary>
    /// The spelling HIDMaestro's pads actually enumerate under, captured
    /// from a live run: parented by the HID stack rather than by a bus,
    /// with nothing in the path naming HIDMaestro or ROOT. Both of the
    /// other signals miss it, so it was classified as physical hardware
    /// on every poll.
    /// </summary>
    [Theory]
    [InlineData(@"\\?\HID#HIDCLASS#1&4784345&10b&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData(@"HID\HIDCLASS\1&4784345&10B&0000")]
    public void AHidDeviceWithNoBusUnderneathItIsRecognised(string path) =>
        Assert.True(VirtualDeviceIdentity.IsVirtual(path, serial: null));

    /// <summary>
    /// Real hardware arrives over a bus and is named for it. Flagging one
    /// of these would make a physical pad unselectable, with no override.
    /// </summary>
    [Theory]
    [InlineData(@"\\?\HID#VID_054C&PID_0CE6&MI_03#8&1e2d4f1&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData(@"HID\VID_045E&PID_028E&IG_00\1&1D40F630&8&0000")]
    [InlineData(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0ce6#9&fbd7d67&0&0000")]
    [InlineData(@"USB\VID_054C&PID_0CE6\ABCDEF")]
    public void RealHardwareIsNotFlagged(string path) =>
        Assert.False(VirtualDeviceIdentity.IsVirtual(path, serial: null));

    [Fact]
    public void AnUnknownPathIsNotFlagged()
    {
        Assert.False(VirtualDeviceIdentity.IsVirtual(null, null));
        Assert.False(VirtualDeviceIdentity.IsVirtual("   ", null));
    }
}
