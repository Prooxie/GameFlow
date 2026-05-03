using System.Runtime.InteropServices;

namespace Autofire.Infrastructure.Runtime.XInput;

internal static class XInputInterop
{
    internal const int ErrorSuccess = 0;
    internal const int ErrorDeviceNotConnected = 1167;

    [Flags]
    internal enum XInputGamepadButtons : ushort
    {
        DPadUp = 0x0001,
        DPadDown = 0x0002,
        DPadLeft = 0x0004,
        DPadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        Guide = 0x0400,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XInputGamepad
    {
        public XInputGamepadButtons Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate uint XInputGetStateDelegate(uint dwUserIndex, out XInputState pState);

    internal static bool TryLoad(out IntPtr libraryHandle, out XInputGetStateDelegate? getState)
    {
        libraryHandle = IntPtr.Zero;
        getState = null;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        const DllImportSearchPath searchPath = DllImportSearchPath.AssemblyDirectory |
                                               DllImportSearchPath.SafeDirectories |
                                               DllImportSearchPath.System32;

        foreach (var name in new[] { "xinput1_4", "xinput1_3", "xinput9_1_0" })
        {
            if (!NativeLibrary.TryLoad(name, typeof(XInputInterop).Assembly, searchPath, out libraryHandle))
            {
                continue;
            }

            if (NativeLibrary.TryGetExport(libraryHandle, "XInputGetState", out var export))
            {
                getState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(export);
                return true;
            }

            NativeLibrary.Free(libraryHandle);
            libraryHandle = IntPtr.Zero;
        }

        return false;
    }

    internal static float NormalizeTrigger(byte value)
    {
        return value / 255f;
    }

    internal static float NormalizeAxis(short value)
    {
        return value == short.MinValue ? -1f : Math.Clamp(value / 32767f, -1f, 1f);
    }
}
