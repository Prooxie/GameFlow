using System.Runtime.InteropServices;

namespace GameFlow.Infrastructure.Runtime.Input.Linux;

/// <summary>
/// Raw P/Invoke surface for creating and driving a virtual input device
/// via <c>/dev/uinput</c> — the standard Linux mechanism for synthesizing
/// input, and the write-direction counterpart to <see cref="EvdevInterop"/>'s
/// read-only evdev surface.
///
/// <para>
/// Every struct layout and ioctl number here was obtained by compiling a
/// small C program against this machine's own
/// <c>/usr/include/linux/uinput.h</c> and printing <c>sizeof</c>/
/// <c>offsetof</c>/the macro-expanded ioctl numbers directly — not
/// recalled from memory or hand-derived from the <c>_IOC</c> formula
/// (the way <see cref="EvdevInterop"/>'s were, before ALSO being
/// cross-checked this same way). This is ground truth from the real
/// compiler and the real header on this machine.
/// </para>
///
/// <para>
/// <b>Unverified against a live device.</b> This sandboxed container has
/// no working uinput driver — <c>/dev/uinput</c> can be mknod'd here but
/// returns ENODEV on open (confirmed: no /proc/misc entry, no kernel
/// module tooling present at all). The numbers below are as
/// well-grounded as possible without live hardware, but the actual
/// open/ioctl/write sequence creating a real virtual device has not
/// been exercised. Needs a real Linux desktop test.
/// </para>
/// </summary>
internal static partial class UinputInterop
{
    // ─── struct input_id (embedded in uinput_setup) — verified: 8 bytes. ───
    [StructLayout(LayoutKind.Sequential)]
    internal struct InputId
    {
        public ushort BusType;
        public ushort Vendor;
        public ushort Product;
        public ushort Version;
    }

    // ─── struct uinput_setup — verified: 92 bytes total, id@0, name@8, ff_effects_max@88. ───
    [StructLayout(LayoutKind.Sequential)]
    internal struct UinputSetup
    {
        public InputId Id;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxNameSize)]
        public byte[] Name;

        public uint FfEffectsMax;
    }

    internal const int MaxNameSize = 80; // UINPUT_MAX_NAME_SIZE, verified
    internal const ushort BusVirtual = 0x06; // BUS_VIRTUAL, verified

    // ─── ioctl numbers — each printed directly from the real macro by a
    // compiled C program on this machine (see this file's class comment). ───
    internal const nuint UI_DEV_CREATE = 0x5501;
    internal const nuint UI_DEV_DESTROY = 0x5502;
    internal const nuint UI_DEV_SETUP = 0x405c5503;
    internal const nuint UI_SET_EVBIT = 0x40045564;
    internal const nuint UI_SET_KEYBIT = 0x40045565;
    internal const nuint UI_SET_RELBIT = 0x40045566;

    internal const int SYN_REPORT = 0; // input-event-codes.h

    // ─── ioctl overloads — one fixed C# signature per "shape" of the
    // variadic native call uinput actually needs (plain int value; a
    // struct pointer; no third argument at all). EvdevInterop already
    // declares the byte[]-buffer shape (unused here) plus open/close,
    // which this reuses rather than re-declaring.

    [LibraryImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    internal static partial int ioctl_intarg(int fd, nuint request, int value);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    internal static partial int ioctl_noarg(int fd, nuint request);

    // UinputSetup embeds a [MarshalAs(ByValArray)] fixed byte buffer —
    // DllImport's mature runtime marshaler handles this pattern more
    // predictably than LibraryImport's newer source-generated
    // marshaling for structs with embedded fixed arrays, matching how
    // WindowsRawInputReader.cs already reaches for classic DllImport
    // (not LibraryImport) whenever ITS Win32 struct interop gets more
    // involved than a flat blittable layout.
    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    internal static extern int ioctl_uinputsetup(int fd, nuint request, ref UinputSetup setup);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial nint write(int fd, ref EvdevInterop.InputEvent buf, nuint count);

    private const int O_WRONLY = 1;
    private const int O_NONBLOCK = 0x800;

    /// <summary>Opens /dev/uinput for writing. Reuses EvdevInterop's own `open` P/Invoke rather than redeclaring the same native symbol a second time.</summary>
    internal static int OpenWriteOnlyNonBlocking(string path) => EvdevInterop.open(path, O_WRONLY | O_NONBLOCK);

    /// <summary>Left-justified UTF-8 name in a zero-padded MaxNameSize buffer, matching a C fixed char[80] with an implicit null terminator. Truncates safely if the name is too long to fit with room for the terminator.</summary>
    internal static byte[] BuildFixedName(string name)
    {
        var buffer = new byte[MaxNameSize];
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        Array.Copy(bytes, buffer, Math.Min(bytes.Length, buffer.Length - 1));
        return buffer;
    }
}
