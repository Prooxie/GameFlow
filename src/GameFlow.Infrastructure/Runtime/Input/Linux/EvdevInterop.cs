using System.Runtime.InteropServices;

namespace GameFlow.Infrastructure.Runtime.Input.Linux;

/// <summary>
/// Raw P/Invoke surface for Linux evdev (<c>/dev/input/eventN</c>).
///
/// <para>
/// <see cref="InputEvent"/>'s layout and every ioctl number below were
/// checked against this build machine's actual kernel headers
/// (<c>/usr/include/linux/input.h</c>, <c>input-event-codes.h</c>,
/// <c>asm-generic/ioctl.h</c>) rather than written from memory — the
/// <c>_IOC</c> bit-packing formula and shift constants
/// (<see cref="IocNrShift"/> etc.) are the verified values, and
/// <see cref="EviocgbitEvKey"/>/<see cref="EviocgbitEvRel"/> are computed
/// from them the same way the C macro does, not hand-guessed.
/// </para>
///
/// <para>
/// <b>64-bit only.</b> On 64-bit Linux (x86_64, arm64 — the only
/// realistic desktop targets), the kernel's wire-format
/// <c>struct input_event</c> is a 16-byte <c>timeval</c> (two 8-byte
/// longs) plus u16 type + u16 code + s32 value = 24 bytes, no padding.
/// This is the long-stable evdev ABI, not the header's Y2038
/// source-compatibility branching (which affects how C programs are
/// COMPILED, not what the kernel actually writes to the device file on
/// a 64-bit system). A 32-bit build would need a different, smaller
/// layout — not supported here; unsupported rather than silently wrong.
/// </para>
///
/// <para>
/// Nothing here has been run against a real input device — this
/// sandboxed container has no <c>/dev/input/</c> at all (verified: the
/// directory doesn't exist). The struct layout and ioctl numbers are
/// checked against real headers, but the actual open/read/ioctl
/// round-trip against live hardware is unverified. Needs a real Linux
/// desktop smoke test.
/// </para>
/// </summary>
internal static partial class EvdevInterop
{
    // ─── struct input_event, 64-bit layout ───
    [StructLayout(LayoutKind.Sequential)]
    internal struct InputEvent
    {
        public long TvSec;
        public long TvUsec;
        public ushort Type;
        public ushort Code;
        public int Value;
    }

    internal const int InputEventSize = 24; // sizeof(InputEvent) on 64-bit — asserted in LinuxRawInputReader's constructor

    // ─── EV_* event types (input-event-codes.h) ───
    internal const ushort EV_SYN = 0x00;
    internal const ushort EV_KEY = 0x01;
    internal const ushort EV_REL = 0x02;

    // ─── REL_* relative-axis codes ───
    internal const ushort REL_X = 0x00;
    internal const ushort REL_Y = 0x01;
    internal const ushort REL_WHEEL = 0x08;

    // ─── BTN_* mouse button codes ───
    internal const ushort BTN_LEFT = 0x110;
    internal const ushort BTN_RIGHT = 0x111;
    internal const ushort BTN_MIDDLE = 0x112;
    internal const ushort BTN_SIDE = 0x113;
    internal const ushort BTN_EXTRA = 0x114;

    // ─── A representative sample of KEY_* codes, used only to CLASSIFY
    // a device as a keyboard (does it support a good spread of letter
    // keys?) during enumeration — the full press/release translation
    // table lives in EvdevKeyCodeMap.cs. ───
    internal const ushort KEY_A = 30;
    internal const ushort KEY_S = 31;
    internal const ushort KEY_SPACE = 57;

    // ─── ioctl request-number derivation — verified against
    // asm-generic/ioctl.h: _IOC_NRSHIFT=0, TYPESHIFT=NRSHIFT+NRBITS(8)=8,
    // SIZESHIFT=TYPESHIFT+TYPEBITS(8)=16, DIRSHIFT=SIZESHIFT+SIZEBITS(14)=30.
    // Computed as uint throughout (the packed request number is
    // inherently a 32-bit unsigned quantity — dir(2)+type(8)+nr(8)+
    // size(14) = 32 bits exactly), then zero-extended to nuint at the
    // P/Invoke boundary to correctly fill ioctl's real native parameter
    // type: `unsigned long int` (8 bytes on x86_64/arm64 — CONFIRMED by
    // reading this machine's own /usr/include/x86_64-linux-gnu/sys/ioctl.h,
    // not assumed). A 4-byte `int` there would pass only the low half of
    // the register, leaving the upper 32 bits as whatever garbage was
    // already in it.
    private const int IocNrShift = 0;
    private const int IocTypeShift = IocNrShift + 8;   // +_IOC_NRBITS
    private const int IocSizeShift = IocTypeShift + 8; // +_IOC_TYPEBITS
    private const int IocDirShift = IocSizeShift + 14; // +_IOC_SIZEBITS
    private const uint IocRead = 2; // _IOC_READ

    private static nuint Ioc(uint dir, uint type, uint nr, uint size) =>
        (nuint)((dir << IocDirShift) | (type << IocTypeShift) | (nr << IocNrShift) | (size << IocSizeShift));

    /// <summary>EVIOCGNAME(len): read the device's human-readable name into a buffer of `len` bytes.</summary>
    internal static nuint Eviocgname(int len) => Ioc(IocRead, 'E', 0x06, (uint)len);

    /// <summary>EVIOCGBIT(EV_KEY, len): read the bitmask of every supported key/button code into a buffer of `len` bytes.</summary>
    internal static nuint EviocgbitEvKey(int len) => Ioc(IocRead, 'E', 0x20u + EV_KEY, (uint)len);

    /// <summary>EVIOCGBIT(EV_REL, len): read the bitmask of every supported relative-axis code.</summary>
    internal static nuint EviocgbitEvRel(int len) => Ioc(IocRead, 'E', 0x20u + EV_REL, (uint)len);

    // ─── libc ───
    private const int O_RDONLY = 0;

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int open(string pathname, int flags);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int close(int fd);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial nint read(int fd, ref InputEvent buf, nuint count);

    // request is `nuint`, matching the VERIFIED native prototype
    // `extern int ioctl(int __fd, unsigned long int __request, ...)`
    // (checked directly against this machine's sys/ioctl.h) — not `int`.
    [LibraryImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    internal static partial int ioctl_bits(int fd, nuint request, [Out] byte[] argp);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    internal static partial int ioctl_name(int fd, nuint request, [Out] byte[] argp);

    internal static int OpenReadOnly(string path) => open(path, O_RDONLY);
}
