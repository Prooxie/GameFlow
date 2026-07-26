using System.Runtime.InteropServices;

namespace GameFlow.Infrastructure.Runtime.Input.Mac;

/// <summary>
/// Raw P/Invoke surface for macOS keyboard/mouse capture (CGEventTap)
/// and synthesis (CGEventPost) via Core Graphics / Core Foundation.
///
/// <para>
/// <b>Read this before trusting anything below.</b> Every value here
/// comes from trained knowledge of Apple's Core Graphics Event Services
/// — a stable, ~20-year-old API — with ZERO ability to verify any of it
/// in this environment: there is no macOS SDK, no Apple headers, no
/// Apple toolchain anywhere in this sandbox (checked directly — none
/// exist). This is categorically different from EvdevInterop.cs and
/// UinputInterop.cs, where every struct layout and ioctl number was
/// compiled and printed by a real C compiler against this machine's
/// real kernel headers. Nothing here has had that treatment. Confidence
/// varies by piece, called out inline:
/// </para>
/// <list type="bullet">
///   <item><b>Higher confidence:</b> the function signatures
///   (CGEventTapCreate, CGEventPost, CGEventCreateMouseEvent, CFRelease
///   etc.) and CGEventType values — these are some of the most
///   frequently documented, unchanged-since-Mac-OS-X-10.4 constants in
///   the whole Apple API surface.</item>
///   <item><b>Meaningfully less confident:</b> the exact CGEventField
///   enum ordering (kCGKeyboardEventKeycode, kCGMouseEventButtonNumber)
///   — a single transcription error here would silently misread every
///   keycode or button. Flagged at each use site.</item>
/// </list>
///
/// <para>
/// This needs a real Mac to validate far more urgently than the Linux
/// code needed real Linux hardware — there, the ABI itself was
/// independently confirmed before ever touching a device. Here, nothing
/// has been independently confirmed at all.
/// </para>
/// </summary>
internal static partial class MacEventInterop
{
    // ─── CGEventTapLocation / Placement / Options — higher confidence:
    // small, simple enums, widely and consistently documented. ───
    internal const int kCGHIDEventTap = 0;
    internal const int kCGHeadInsertEventTap = 0;
    internal const int kCGEventTapOptionListenOnly = 1; // passive: cannot modify or block real input, only observe

    // ─── CGEventType — higher confidence: heavily cited, stable values. ───
    internal const uint kCGEventKeyDown = 10;
    internal const uint kCGEventKeyUp = 11;
    internal const uint kCGEventLeftMouseDown = 1;
    internal const uint kCGEventLeftMouseUp = 2;
    internal const uint kCGEventRightMouseDown = 3;
    internal const uint kCGEventRightMouseUp = 4;
    internal const uint kCGEventMouseMoved = 5;
    internal const uint kCGEventLeftMouseDragged = 6;
    internal const uint kCGEventRightMouseDragged = 7;
    internal const uint kCGEventOtherMouseDown = 25;
    internal const uint kCGEventOtherMouseUp = 26;
    internal const uint kCGEventOtherMouseDragged = 27;

    /// <summary>Bitmask helper for CGEventTapCreate's eventsOfInterest — bit N = (1 &lt;&lt; CGEventType N), a documented, simple convention.</summary>
    internal static ulong EventMask(params uint[] types)
    {
        ulong mask = 0;
        foreach (var t in types)
        {
            mask |= 1UL << (int)t;
        }
        return mask;
    }

    // ─── CGEventField — LOWER CONFIDENCE. This exact ordering (a single
    // flat enum shared across every event type, not namespaced per
    // type) is recalled, not verified against any header. A wrong value
    // here silently reads the wrong field, not a crash — the single
    // biggest risk in this whole file. ───
    internal const uint kCGMouseEventButtonNumber = 3;
    internal const uint kCGKeyboardEventKeycode = 9;

    // ─── CGPoint — matches Apple's public CGGeometry.h layout (two
    // doubles) — this specific struct is about as stable/public as any
    // Apple type gets. ───
    [StructLayout(LayoutKind.Sequential)]
    internal struct CGPoint
    {
        public double X;
        public double Y;

        public CGPoint(double x, double y) { X = x; Y = y; }
    }

    // ─── Core Graphics ───

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static partial IntPtr CGEventTapCreate(
        int tap, int place, int options, ulong eventsOfInterest,
        IntPtr callback, IntPtr userInfo);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static partial void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static partial long CGEventGetIntegerValueField(IntPtr eventRef, uint field);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static partial uint CGEventGetType(IntPtr eventRef);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static partial CGPoint CGEventGetLocation(IntPtr eventRef);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static partial IntPtr CGEventCreate(IntPtr source);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static partial IntPtr CGEventCreateMouseEvent(
        IntPtr source, uint mouseType, CGPoint mouseCursorPosition, long mouseButton);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static partial void CGEventSetIntegerValueField(IntPtr eventRef, uint field, long value);

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static partial void CGEventPost(int tap, IntPtr eventRef);

    // ─── Core Foundation ───

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static partial void CFRelease(IntPtr cfObject);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static partial IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, nint order);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static partial IntPtr CFRunLoopGetCurrent();

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static partial void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static partial void CFRunLoopRun();

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static partial void CFRunLoopStop(IntPtr runLoop);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string cStr, uint encoding);

    internal const uint KCFStringEncodingUTF8 = 0x08000100;
}
