using System.Runtime.InteropServices;
using GameFlow.Infrastructure.Runtime.Input;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Input.Mac;

/// <summary>
/// macOS counterpart to WindowsRawInputReader / LinuxRawInputReader —
/// a global CGEventTap (listen-only: cannot modify or block real
/// input) running on a dedicated thread that hosts its own CFRunLoop,
/// updating shared state from a native callback.
///
/// <para>
/// <b>No per-device distinction.</b> Unlike evdev (one file per
/// physical device) or Windows Raw Input (one handle per device),
/// CGEventTap reports ONE aggregate stream for every keyboard/mouse
/// system-wide — there is no "which physical keyboard" information at
/// this API level (IOHIDManager could offer that, at a similar cost in
/// complexity to everything already here; not attempted this round).
/// <see cref="GetPressedKeys"/>/<see cref="ReadMouseFrame"/> (the
/// per-device reads) therefore always return empty, and the real data
/// lives in <see cref="GetPressedKeysAggregate"/>/<see cref="ReadMouseFrameAggregate"/>
/// — exactly the fallback path <see cref="IKeyboardStateSource"/>'s own
/// doc comment already describes for "can't attribute to a specific
/// device," reusing the existing contract rather than inventing a new one.
/// </para>
///
/// <para>
/// <b>Requires Input Monitoring permission</b> (System Settings >
/// Privacy &amp; Security > Input Monitoring). Without it,
/// CGEventTapCreate returns null (logged once) and every read is
/// silently empty — an OS-level permission gate the app itself can't
/// grant, matching how Linux needs "input" group membership.
/// </para>
///
/// <para>
/// <b>No verification possible in this environment</b> — see
/// MacEventInterop.cs's header comment. Everything here is a best
/// effort from trained knowledge of a stable API, not confirmed
/// against any header, compiler, or device. Needs a real Mac far more
/// urgently than the Linux code needed real Linux hardware.
/// </para>
/// </summary>
public sealed class MacRawInputReader : IKeyboardStateSource, IMouseStateSource, IDisposable
{
    private readonly ILogger<MacRawInputReader> logger;
    private readonly Lock gate = new();
    private readonly HashSet<int> pressedVirtualKeys = [];
    private static readonly IReadOnlySet<int> EmptyKeys = new HashSet<int>();

    // Mouse position is read as an ABSOLUTE point (CGEventGetLocation)
    // and diffed against the previous read to get a delta — chosen
    // specifically to avoid depending on kCGMouseEventDeltaX/Y, whose
    // exact CGEventField ordering is one of the least-certain constants
    // in this file (see MacEventInterop.cs). CGEventGetLocation is a
    // dedicated, separately-documented function, not reliant on that
    // enum at all.
    private bool hasLastMousePosition;
    private double lastMouseX;
    private double lastMouseY;
    private int accumulatedDx;
    private int accumulatedDy;
    private bool left;
    private bool right;
    private bool middle;
    private bool button4;
    private bool button5;

    private GCHandle selfHandle;
    private IntPtr tapHandle;
    private IntPtr runLoopSource;
    private IntPtr hostRunLoop;
    private Thread? tapThread;
    private volatile bool disposed;
    private readonly ManualResetEventSlim runLoopReady = new(false);

    public MacRawInputReader(ILogger<MacRawInputReader> logger)
    {
        this.logger = logger;
        selfHandle = GCHandle.Alloc(this);

        tapThread = new Thread(RunEventTapThread) { IsBackground = true, Name = "cgeventtap-reader" };
        tapThread.Start();

        // Bounded wait so callers get a settled state (active, or
        // failed-and-logged) rather than a brief ambiguous window right
        // after construction.
        _ = runLoopReady.Wait(TimeSpan.FromSeconds(2));
    }

    private void RunEventTapThread()
    {
        try
        {
            var mask = MacEventInterop.EventMask(
                MacEventInterop.kCGEventKeyDown, MacEventInterop.kCGEventKeyUp,
                MacEventInterop.kCGEventLeftMouseDown, MacEventInterop.kCGEventLeftMouseUp,
                MacEventInterop.kCGEventRightMouseDown, MacEventInterop.kCGEventRightMouseUp,
                MacEventInterop.kCGEventMouseMoved,
                MacEventInterop.kCGEventLeftMouseDragged, MacEventInterop.kCGEventRightMouseDragged,
                MacEventInterop.kCGEventOtherMouseDown, MacEventInterop.kCGEventOtherMouseUp,
                MacEventInterop.kCGEventOtherMouseDragged);

            unsafe
            {
                // Must explicitly say unmanaged[Cdecl] here to match
                // TapCallback's [UnmanagedCallersOnly(CallConvs =
                // [typeof(CallConvCdecl)])] — a bare `unmanaged<>` (no
                // explicit convention) doesn't imply Cdecl specifically,
                // and the compiler won't assume the two agree (CS8786).
                delegate* unmanaged[Cdecl]<IntPtr, uint, IntPtr, IntPtr, IntPtr> callback = &TapCallback;
                tapHandle = MacEventInterop.CGEventTapCreate(
                    MacEventInterop.kCGHIDEventTap, MacEventInterop.kCGHeadInsertEventTap,
                    MacEventInterop.kCGEventTapOptionListenOnly, mask,
                    (IntPtr)callback, GCHandle.ToIntPtr(selfHandle));
            }

            if (tapHandle == IntPtr.Zero)
            {
                logger.LogWarning(
                    "CGEventTapCreate returned null — most likely missing Input Monitoring " +
                    "permission (System Settings > Privacy & Security > Input Monitoring). " +
                    "Keyboard/mouse-as-a-source will read as neutral until granted.");
                runLoopReady.Set();
                return;
            }

            runLoopSource = MacEventInterop.CFMachPortCreateRunLoopSource(IntPtr.Zero, tapHandle, 0);
            hostRunLoop = MacEventInterop.CFRunLoopGetCurrent();
            var modeString = MacEventInterop.CFStringCreateWithCString(
                IntPtr.Zero, "kCFRunLoopDefaultMode", MacEventInterop.KCFStringEncodingUTF8);

            MacEventInterop.CFRunLoopAddSource(hostRunLoop, runLoopSource, modeString);
            MacEventInterop.CGEventTapEnable(tapHandle, true);

            logger.LogInformation("CGEventTap: keyboard/mouse capture active.");
            runLoopReady.Set();

            MacEventInterop.CFRunLoopRun(); // blocks here until Dispose() calls CFRunLoopStop
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "CGEventTap thread failed to start.");
            runLoopReady.Set();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static IntPtr TapCallback(IntPtr proxy, uint type, IntPtr eventRef, IntPtr userInfo)
    {
        MacRawInputReader? reader = null;
        try
        {
            reader = GCHandle.FromIntPtr(userInfo).Target as MacRawInputReader;
            reader?.HandleEvent(type, eventRef);
        }
        catch (Exception exception)
        {
            // A native callback must never let a managed exception
            // escape across the P/Invoke boundary — that's undefined
            // behavior on the C side, not just a missed event.
            reader?.logger.LogDebug(exception, "CGEventTap callback threw.");
        }
        return eventRef; // listen-only mode ignores the return value either way; returning it unmodified is the conventional, safe choice.
    }

    private void HandleEvent(uint type, IntPtr eventRef)
    {
        if (type == MacEventInterop.kCGEventKeyDown || type == MacEventInterop.kCGEventKeyUp)
        {
            var isDown = type == MacEventInterop.kCGEventKeyDown;
            // kCGKeyboardEventKeycode — one of the two CGEventField
            // values this file is least certain about (see
            // MacEventInterop.cs's header comment).
            var macKeycode = (int)MacEventInterop.CGEventGetIntegerValueField(eventRef, MacEventInterop.kCGKeyboardEventKeycode);
            if (MacKeyCodeMap.MacToVirtualKey.TryGetValue(macKeycode, out var vk))
            {
                lock (gate)
                {
                    if (isDown) { pressedVirtualKeys.Add(vk); } else { pressedVirtualKeys.Remove(vk); }
                }
            }
            return;
        }

        var location = MacEventInterop.CGEventGetLocation(eventRef);
        lock (gate)
        {
            if (hasLastMousePosition)
            {
                accumulatedDx += (int)Math.Round(location.X - lastMouseX);
                accumulatedDy += (int)Math.Round(location.Y - lastMouseY);
            }
            lastMouseX = location.X;
            lastMouseY = location.Y;
            hasLastMousePosition = true;

            switch (type)
            {
                case MacEventInterop.kCGEventLeftMouseDown: left = true; break;
                case MacEventInterop.kCGEventLeftMouseUp: left = false; break;
                case MacEventInterop.kCGEventRightMouseDown: right = true; break;
                case MacEventInterop.kCGEventRightMouseUp: right = false; break;
                case MacEventInterop.kCGEventOtherMouseDown:
                    SetOtherButton(MacEventInterop.CGEventGetIntegerValueField(eventRef, MacEventInterop.kCGMouseEventButtonNumber), true);
                    break;
                case MacEventInterop.kCGEventOtherMouseUp:
                    SetOtherButton(MacEventInterop.CGEventGetIntegerValueField(eventRef, MacEventInterop.kCGMouseEventButtonNumber), false);
                    break;
            }
        }
    }

    /// <summary>kCGMouseEventButtonNumber — the other CGEventField value this file is least certain about. 0/1 (left/right) never arrive here since those fire the dedicated Left/RightMouse events instead; 2=middle by convention, 3+=further side buttons. Caller already holds gate.</summary>
    private void SetOtherButton(long buttonNumber, bool isDown)
    {
        switch (buttonNumber)
        {
            case 2: middle = isDown; break;
            case 3: button4 = isDown; break;
            case 4: button5 = isDown; break;
        }
    }

    public IReadOnlySet<int> GetPressedKeys(string deviceId) => EmptyKeys;

    public IReadOnlySet<int> GetPressedKeysAggregate()
    {
        lock (gate)
        {
            return pressedVirtualKeys.Count == 0 ? EmptyKeys : new HashSet<int>(pressedVirtualKeys);
        }
    }

    public MouseFrame ReadMouseFrame(string deviceId) => default;

    public MouseFrame ReadMouseFrameAggregate()
    {
        lock (gate)
        {
            var frame = new MouseFrame(accumulatedDx, accumulatedDy, left, right, middle, button4, button5);
            accumulatedDx = 0;
            accumulatedDy = 0;
            return frame;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        if (tapHandle != IntPtr.Zero)
        {
            MacEventInterop.CGEventTapEnable(tapHandle, false);
        }
        if (hostRunLoop != IntPtr.Zero)
        {
            MacEventInterop.CFRunLoopStop(hostRunLoop);
        }
        _ = (tapThread?.Join(TimeSpan.FromSeconds(1)));

        if (runLoopSource != IntPtr.Zero) { MacEventInterop.CFRelease(runLoopSource); }
        if (tapHandle != IntPtr.Zero) { MacEventInterop.CFRelease(tapHandle); }
        if (selfHandle.IsAllocated) { selfHandle.Free(); }
        runLoopReady.Dispose();
    }
}
