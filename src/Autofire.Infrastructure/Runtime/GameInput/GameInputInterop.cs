using System.Runtime.InteropServices;

namespace Autofire.Infrastructure.Runtime.GameInput;

internal static class GameInputInterop
{
    internal const int AddRefSlot = 1;
    internal const int ReleaseSlot = 2;
    internal const int IGameInputGetCurrentReadingSlot = 8;
    internal const int IGameInputRegisterDeviceCallbackSlot = 12;
    internal const int IGameInputUnregisterCallbackSlot = 18;
    internal const int IGameInputReadingGetGamepadStateSlot = 12;
    internal const int IGameInputDeviceGetDeviceInfoSlot = 5;

    [Flags]
    internal enum GameInputKind : uint
    {
        Unknown = 0x00000000,
        ControllerAxis = 0x00000002,
        ControllerButton = 0x00000004,
        ControllerSwitch = 0x00000008,
        Controller = 0x0000000E,
        Keyboard = 0x00000010,
        Mouse = 0x00000020,
        Sensors = 0x00000040,
        ArcadeStick = 0x00010000,
        FlightStick = 0x00020000,
        Gamepad = 0x00040000,
        RacingWheel = 0x00080000
    }

    [Flags]
    internal enum GameInputDeviceStatus : uint
    {
        NoStatus = 0x00000000,
        Connected = 0x00000001,
        HapticInfoReady = 0x00200000,
        AnyStatus = 0xFFFFFFFF
    }

    internal enum GameInputEnumerationKind : int
    {
        NoEnumeration = 0,
        AsyncEnumeration = 1,
        BlockingEnumeration = 2
    }

    [Flags]
    internal enum GameInputGamepadButtons : uint
    {
        None = 0x00000000,
        Menu = 0x00000001,
        View = 0x00000002,
        A = 0x00000004,
        B = 0x00000008,
        X = 0x00000010,
        Y = 0x00000020,
        DPadUp = 0x00000040,
        DPadDown = 0x00000080,
        DPadLeft = 0x00000100,
        DPadRight = 0x00000200,
        LeftShoulder = 0x00000400,
        RightShoulder = 0x00000800,
        LeftThumbstick = 0x00001000,
        RightThumbstick = 0x00002000,
        C = 0x00004000,
        Z = 0x00008000,
        LeftTriggerButton = 0x00010000,
        RightTriggerButton = 0x00020000,
        LeftThumbstickUp = 0x00040000,
        LeftThumbstickDown = 0x00080000,
        LeftThumbstickLeft = 0x00100000,
        LeftThumbstickRight = 0x00200000,
        RightThumbstickUp = 0x00400000,
        RightThumbstickDown = 0x00800000,
        RightThumbstickLeft = 0x01000000,
        RightThumbstickRight = 0x02000000,
        PaddleLeft1 = 0x04000000,
        PaddleLeft2 = 0x08000000,
        PaddleRight1 = 0x10000000,
        PaddleRight2 = 0x20000000
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GameInputGamepadState
    {
        public GameInputGamepadButtons Buttons;
        public float LeftTrigger;
        public float RightTrigger;
        public float LeftThumbstickX;
        public float LeftThumbstickY;
        public float RightThumbstickX;
        public float RightThumbstickY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GameInputUsage
    {
        public ushort Page;
        public ushort Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GameInputVersion
    {
        public ushort Major;
        public ushort Minor;
        public ushort Build;
        public ushort Revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AppLocalDeviceId
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GameInputDeviceInfo
    {
        public ushort VendorId;
        public ushort ProductId;
        public ushort RevisionNumber;
        public GameInputUsage Usage;
        public GameInputVersion HardwareVersion;
        public GameInputVersion FirmwareVersion;
        public AppLocalDeviceId DeviceId;
        public AppLocalDeviceId DeviceRootId;
        public uint DeviceFamily;
        public GameInputKind SupportedInput;
        public uint SupportedRumbleMotors;
        public uint SupportedSystemButtons;
        public Guid ContainerId;
        public IntPtr DisplayName;
        public IntPtr PnpPath;
        public IntPtr KeyboardInfo;
        public IntPtr MouseInfo;
        public IntPtr SensorsInfo;
        public IntPtr ControllerInfo;
        public IntPtr ArcadeStickInfo;
        public IntPtr FlightStickInfo;
        public IntPtr GamepadInfo;
        public IntPtr RacingWheelInfo;
        public uint ForceFeedbackMotorCount;
        public IntPtr ForceFeedbackMotorInfo;
        public uint InputReportCount;
        public IntPtr InputReportInfo;
        public uint OutputReportCount;
        public IntPtr OutputReportInfo;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate int GameInputCreateDelegate(out IntPtr gameInput);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetCurrentReadingDelegate(
        IntPtr @this,
        GameInputKind inputKind,
        IntPtr device,
        out IntPtr reading);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int RegisterDeviceCallbackDelegate(
        IntPtr @this,
        IntPtr device,
        GameInputKind inputKind,
        GameInputDeviceStatus statusFilter,
        GameInputEnumerationKind enumerationKind,
        IntPtr context,
        DeviceCallbackDelegate callback,
        out ulong callbackToken);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal delegate bool UnregisterCallbackDelegate(IntPtr @this, ulong callbackToken);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int GetDeviceInfoDelegate(IntPtr @this, out IntPtr deviceInfo);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal delegate bool GetGamepadStateDelegate(IntPtr @this, out GameInputGamepadState state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate uint AddRefDelegate(IntPtr @this);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate uint ReleaseDelegate(IntPtr @this);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void DeviceCallbackDelegate(
        ulong callbackToken,
        IntPtr context,
        IntPtr device,
        ulong timestamp,
        GameInputDeviceStatus currentStatus,
        GameInputDeviceStatus previousStatus);

    internal static bool TryLoadGameInput(out IntPtr libraryHandle, out GameInputCreateDelegate? create)
    {
        create = null;
        libraryHandle = IntPtr.Zero;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        const DllImportSearchPath searchPath = DllImportSearchPath.AssemblyDirectory |
                                               DllImportSearchPath.SafeDirectories |
                                               DllImportSearchPath.System32;

        if (!NativeLibrary.TryLoad("gameinput", typeof(GameInputInterop).Assembly, searchPath, out libraryHandle))
        {
            return false;
        }

        if (!NativeLibrary.TryGetExport(libraryHandle, "GameInputCreate", out var export))
        {
            NativeLibrary.Free(libraryHandle);
            libraryHandle = IntPtr.Zero;
            return false;
        }

        create = Marshal.GetDelegateForFunctionPointer<GameInputCreateDelegate>(export);
        return true;
    }

    internal static bool Succeeded(int hresult)
    {
        return hresult >= 0;
    }

    internal static TDelegate GetVirtualMethod<TDelegate>(IntPtr instance, int slot)
        where TDelegate : Delegate
    {
        if (instance == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        var vtable = Marshal.ReadIntPtr(instance);
        var functionPtr = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(functionPtr);
    }

    internal static void AddRef(IntPtr instance)
    {
        if (instance == IntPtr.Zero)
        {
            return;
        }

        var addRef = GetVirtualMethod<AddRefDelegate>(instance, AddRefSlot);
        _ = addRef(instance);
    }

    internal static void Release(IntPtr instance)
    {
        if (instance == IntPtr.Zero)
        {
            return;
        }

        var release = GetVirtualMethod<ReleaseDelegate>(instance, ReleaseSlot);
        _ = release(instance);
    }

    internal static string DeviceIdToString(AppLocalDeviceId deviceId)
    {
        var bytes = deviceId.Value;
        return bytes is null || bytes.Length == 0 ? string.Empty : Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string PtrToUtf8String(IntPtr ptr)
    {
        return ptr == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }
}
