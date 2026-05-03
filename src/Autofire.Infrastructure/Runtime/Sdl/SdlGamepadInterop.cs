using System.Runtime.InteropServices;

namespace Autofire.Infrastructure.Runtime.Sdl;

internal static class SdlGamepadInterop
{
    private const string LibraryName = "SDL3";

    public const uint SDL_INIT_JOYSTICK = 0x00000200u;
    public const uint SDL_INIT_HAPTIC = 0x00001000u;
    public const uint SDL_INIT_GAMEPAD = 0x00002000u;

    public const string SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS";
    public const string SDL_HINT_JOYSTICK_HIDAPI = "SDL_JOYSTICK_HIDAPI";
    public const string SDL_HINT_JOYSTICK_DIRECTINPUT = "SDL_JOYSTICK_DIRECTINPUT";
    public const string SDL_HINT_XINPUT_ENABLED = "SDL_XINPUT_ENABLED";
    public const string SDL_HINT_AUTO_UPDATE_JOYSTICKS = "SDL_AUTO_UPDATE_JOYSTICKS";

    [DllImport(LibraryName, EntryPoint = "SDL_Init", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Init(uint flags);

    [DllImport(LibraryName, EntryPoint = "SDL_QuitSubSystem", CallingConvention = CallingConvention.Cdecl)]
    public static extern void QuitSubSystem(uint flags);

    [DllImport(LibraryName, EntryPoint = "SDL_Quit", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Quit();

    [DllImport(LibraryName, EntryPoint = "SDL_SetHint", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(LibraryName, EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetError();

    [DllImport(LibraryName, EntryPoint = "SDL_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Free(IntPtr memory);

    [DllImport(LibraryName, EntryPoint = "SDL_UpdateJoysticks", CallingConvention = CallingConvention.Cdecl)]
    public static extern void UpdateJoysticks();

    [DllImport(LibraryName, EntryPoint = "SDL_UpdateGamepads", CallingConvention = CallingConvention.Cdecl)]
    public static extern void UpdateGamepads();

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepads", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepads(out int count);

    [DllImport(LibraryName, EntryPoint = "SDL_OpenGamepad", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr OpenGamepad(int instanceId);

    [DllImport(LibraryName, EntryPoint = "SDL_CloseGamepad", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CloseGamepad(IntPtr gamepad);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadID", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetGamepadId(IntPtr gamepad);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadConnectionState", CallingConvention = CallingConvention.Cdecl)]
    public static extern SdlJoystickConnectionState GetGamepadConnectionState(IntPtr gamepad);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadNameForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepadNameForId(int instanceId);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadPathForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepadPathForId(int instanceId);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadSerial", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepadSerial(IntPtr gamepad);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadVendorForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetGamepadVendorForId(int instanceId);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadProductForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetGamepadProductForId(int instanceId);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadTypeForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern SdlGamepadType GetGamepadTypeForId(int instanceId);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadButtonLabelForType", CallingConvention = CallingConvention.Cdecl)]
    public static extern SdlGamepadButtonLabel GetGamepadButtonLabelForType(SdlGamepadType type, SdlGamepadButton button);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadAxis", CallingConvention = CallingConvention.Cdecl)]
    public static extern short GetGamepadAxis(IntPtr gamepad, SdlGamepadAxis axis);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadButton", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GetGamepadButton(IntPtr gamepad, SdlGamepadButton button);

    [DllImport(LibraryName, EntryPoint = "SDL_GetNumGamepadTouchpads", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetNumGamepadTouchpads(IntPtr gamepad);

    [DllImport(LibraryName, EntryPoint = "SDL_GetNumGamepadTouchpadFingers", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetNumGamepadTouchpadFingers(IntPtr gamepad, int touchpad);

    [DllImport(LibraryName, EntryPoint = "SDL_GetGamepadTouchpadFinger", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GetGamepadTouchpadFinger(
        IntPtr gamepad,
        int touchpad,
        int finger,
        [MarshalAs(UnmanagedType.I1)] out bool down,
        out float x,
        out float y,
        out float pressure);

    public static string? PtrToStringUtf8(IntPtr pointer)
        => pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);

    public static string GetLastError()
        => PtrToStringUtf8(GetError()) ?? "SDL error unavailable.";

    public static float NormalizeStick(short value)
    {
        const float minimum = -32768f;
        const float maximum = 32767f;
        return Math.Clamp(value < 0 ? value / -minimum : value / maximum, -1f, 1f);
    }

    public static float NormalizeTrigger(short value)
        => Math.Clamp(value / 32767f, 0f, 1f);
}

internal enum SdlGamepadAxis
{
    Invalid = -1,
    LeftX = 0,
    LeftY = 1,
    RightX = 2,
    RightY = 3,
    LeftTrigger = 4,
    RightTrigger = 5,
    Count = 6
}

internal enum SdlGamepadButton
{
    Invalid = -1,
    South = 0,
    East = 1,
    West = 2,
    North = 3,
    Back = 4,
    Guide = 5,
    Start = 6,
    LeftStick = 7,
    RightStick = 8,
    LeftShoulder = 9,
    RightShoulder = 10,
    DpadUp = 11,
    DpadDown = 12,
    DpadLeft = 13,
    DpadRight = 14,
    Misc1 = 15,
    RightPaddle1 = 16,
    LeftPaddle1 = 17,
    RightPaddle2 = 18,
    LeftPaddle2 = 19,
    Touchpad = 20,
    Misc2 = 21,
    Misc3 = 22,
    Misc4 = 23,
    Misc5 = 24,
    Misc6 = 25,
    Count = 26
}

internal enum SdlGamepadType
{
    Unknown = 0,
    Standard = 1,
    Xbox360 = 2,
    XboxOne = 3,
    Ps3 = 4,
    Ps4 = 5,
    Ps5 = 6,
    NintendoSwitchPro = 7,
    NintendoSwitchJoyConLeft = 8,
    NintendoSwitchJoyConRight = 9,
    NintendoSwitchJoyConPair = 10,
    GameCube = 11,
    Count = 12
}

internal enum SdlGamepadButtonLabel
{
    Unknown = 0,
    A = 1,
    B = 2,
    X = 3,
    Y = 4,
    Cross = 5,
    Circle = 6,
    Square = 7,
    Triangle = 8
}

internal enum SdlJoystickConnectionState
{
    Invalid = -1,
    Unknown = 0,
    Wired = 1,
    Wireless = 2
}
