using GameFlow.Infrastructure.Runtime.Input.Linux;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Input.Linux;

public sealed class UinputInteropTests
{
    [Fact]
    public void UinputSetup_MarshalsToTheVerified92ByteLayout()
    {
        // 8 (InputId) + 80 (Name, fixed) + 4 (FfEffectsMax) = 92 — confirmed
        // via a compiled C program against the real uinput.h (see this
        // class's own header comment), not assumed from the field list.
        var size = System.Runtime.InteropServices.Marshal.SizeOf<UinputInterop.UinputSetup>();
        Assert.Equal(92, size);
    }

    [Fact]
    public void InputId_MarshalsToTheVerified8ByteLayout()
    {
        var size = System.Runtime.InteropServices.Marshal.SizeOf<UinputInterop.InputId>();
        Assert.Equal(8, size);
    }

    [Fact]
    public void BuildFixedName_ProducesExactlyMaxNameSizeBytes()
    {
        var buffer = UinputInterop.BuildFixedName("GameFlow Virtual Mouse");
        Assert.Equal(UinputInterop.MaxNameSize, buffer.Length);
    }

    [Fact]
    public void BuildFixedName_NullPadsAfterTheName()
    {
        var buffer = UinputInterop.BuildFixedName("Mouse");
        var expectedText = System.Text.Encoding.UTF8.GetBytes("Mouse");

        for (var i = 0; i < expectedText.Length; i++)
        {
            Assert.Equal(expectedText[i], buffer[i]);
        }
        for (var i = expectedText.Length; i < buffer.Length; i++)
        {
            Assert.Equal(0, buffer[i]);
        }
    }

    [Fact]
    public void BuildFixedName_TruncatesSafely_LeavingRoomForTheImplicitTerminator()
    {
        var tooLong = new string('x', UinputInterop.MaxNameSize + 20);
        var buffer = UinputInterop.BuildFixedName(tooLong);

        Assert.Equal(UinputInterop.MaxNameSize, buffer.Length);
        // Last byte must stay zero — an 80-byte C string needs at least
        // one null terminator; filling all 80 bytes with 'x' would leave
        // the native side reading past the buffer looking for one.
        Assert.Equal(0, buffer[^1]);
    }
}
