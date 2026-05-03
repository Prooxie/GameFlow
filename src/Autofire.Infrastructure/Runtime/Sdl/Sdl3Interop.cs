using System.Runtime.InteropServices;

namespace Autofire.Infrastructure.Runtime.Sdl;

internal static class Sdl3Interop
{
    public const uint InitJoystick = 0x00000200u;
    public const uint InitHaptic = 0x00001000u;
    public const uint InitGamepad = 0x00002000u;

    public const byte HatCentered = 0x00u;
    public const byte HatUp = 0x01u;
    public const byte HatRight = 0x02u;
    public const byte HatDown = 0x04u;
    public const byte HatLeft = 0x08u;

    public const string HintJoystickHidApi = "SDL_JOYSTICK_HIDAPI";
    public const string HintXInputEnabled = "SDL_XINPUT_ENABLED";

    public enum GamepadAxis
    {
        Invalid = -1,
        LeftX = 0,
        LeftY = 1,
        RightX = 2,
        RightY = 3,
        LeftTrigger = 4,
        RightTrigger = 5
    }

    public enum GamepadButton
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
        Touchpad = 20
    }

    public enum JoystickType
    {
        Unknown = 0,
        Gamepad = 1,
        Wheel = 2,
        ArcadeStick = 3,
        FlightStick = 4,
        DancePad = 5,
        Guitar = 6,
        DrumKit = 7,
        ArcadePad = 8,
        Throttle = 9,
        Count = 10
    }

    public enum GamepadType
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

    [DllImport("SDL3", EntryPoint = "SDL_Init", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Init(uint flags);

    [DllImport("SDL3", EntryPoint = "SDL_QuitSubSystem", CallingConvention = CallingConvention.Cdecl)]
    public static extern void QuitSubSystem(uint flags);

    [DllImport("SDL3", EntryPoint = "SDL_SetHint", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport("SDL3", EntryPoint = "SDL_UpdateGamepads", CallingConvention = CallingConvention.Cdecl)]
    public static extern void UpdateGamepads();

    [DllImport("SDL3", EntryPoint = "SDL_UpdateJoysticks", CallingConvention = CallingConvention.Cdecl)]
    public static extern void UpdateJoysticks();

    [DllImport("SDL3", EntryPoint = "SDL_SetGamepadEventsEnabled", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetGamepadEventsEnabled([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport("SDL3", EntryPoint = "SDL_SetJoystickEventsEnabled", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetJoystickEventsEnabled([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepads", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepads(out int count);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoysticks", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetJoysticks(out int count);

    [DllImport("SDL3", EntryPoint = "SDL_IsGamepad", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool IsGamepad(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_OpenGamepad", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr OpenGamepad(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_CloseGamepad", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CloseGamepad(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GamepadConnected", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GamepadConnected(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadName", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepadName(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadPathForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetGamepadPathForId(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadVendor", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetGamepadVendor(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadProduct", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetGamepadProduct(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadType", CallingConvention = CallingConvention.Cdecl)]
    public static extern GamepadType GetGamepadType(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadAxis", CallingConvention = CallingConvention.Cdecl)]
    public static extern short GetGamepadAxis(IntPtr gamepad, GamepadAxis axis);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadButton", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GetGamepadButton(IntPtr gamepad, GamepadButton button);

    [DllImport("SDL3", EntryPoint = "SDL_GetNumGamepadTouchpads", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetNumGamepadTouchpads(IntPtr gamepad);

    [DllImport("SDL3", EntryPoint = "SDL_GetNumGamepadTouchpadFingers", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetNumGamepadTouchpadFingers(IntPtr gamepad, int touchpad);

    [DllImport("SDL3", EntryPoint = "SDL_GetGamepadTouchpadFinger", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GetGamepadTouchpadFinger(IntPtr gamepad, int touchpad, int finger, out byte down, out float x, out float y, out float pressure);

    [DllImport("SDL3", EntryPoint = "SDL_OpenJoystick", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr OpenJoystick(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_CloseJoystick", CallingConvention = CallingConvention.Cdecl)]
    public static extern void CloseJoystick(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_JoystickConnected", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool JoystickConnected(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickName", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetJoystickName(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickNameForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetJoystickNameForId(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickPathForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetJoystickPathForId(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickVendor", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetJoystickVendor(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickVendorForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetJoystickVendorForId(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickProduct", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetJoystickProduct(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickProductForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern ushort GetJoystickProductForId(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickType", CallingConvention = CallingConvention.Cdecl)]
    public static extern JoystickType GetJoystickType(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickTypeForID", CallingConvention = CallingConvention.Cdecl)]
    public static extern JoystickType GetJoystickTypeForId(int instanceId);

    [DllImport("SDL3", EntryPoint = "SDL_GetNumJoystickAxes", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetNumJoystickAxes(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickAxis", CallingConvention = CallingConvention.Cdecl)]
    public static extern short GetJoystickAxis(IntPtr joystick, int axis);

    [DllImport("SDL3", EntryPoint = "SDL_GetNumJoystickButtons", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetNumJoystickButtons(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickButton", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GetJoystickButton(IntPtr joystick, int button);

    [DllImport("SDL3", EntryPoint = "SDL_GetNumJoystickHats", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetNumJoystickHats(IntPtr joystick);

    [DllImport("SDL3", EntryPoint = "SDL_GetJoystickHat", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte GetJoystickHat(IntPtr joystick, int hat);

    [DllImport("SDL3", EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetError();

    [DllImport("SDL3", EntryPoint = "SDL_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Free(IntPtr memory);

    public static string GetErrorString()
    {
        var pointer = GetError();
        return Utf8(pointer) ?? "SDL3 returned an unknown error.";
    }

    public static string? Utf8(IntPtr pointer)
        => pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);

    public static int[] ReadJoystickIds(IntPtr pointer, int count)
    {
        if (pointer == IntPtr.Zero || count <= 0)
        {
            return [];
        }

        var ids = new int[count];
        for (var index = 0; index < count; index++)
        {
            ids[index] = Marshal.ReadInt32(pointer, index * sizeof(int));
        }

        return ids;
    }
}
