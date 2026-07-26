using GameFlow.Infrastructure.Runtime.Input;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Input.Linux;

/// <summary>
/// Linux counterpart to WindowsRawInputReader — one background thread
/// per classified evdev device, doing a blocking read() loop and
/// updating shared state under <see cref="gate"/>. Implements
/// <see cref="IKeyboardStateSource"/> and <see cref="IMouseStateSource"/>
/// only; there is no Linux equivalent of <see cref="IRawInputAttacher"/>
/// (evdev reads are already system-wide once permitted — there's no
/// "attach to a window" concept to implement, so <see cref="NullRawInputAttacher"/>
/// is reused for that slot in DI rather than writing a redundant no-op).
///
/// <para>
/// <b>Scans once, at construction.</b> A keyboard/mouse plugged in
/// after this reader starts won't be picked up without an app restart —
/// WindowsRawInputReader's own device-discovery cadence wasn't verified
/// against this round's time budget, so matching it exactly wasn't
/// attempted; this is a documented, known scope limit rather than a
/// silent gap.
/// </para>
///
/// <para>
/// <b>Requires the "input" group.</b> /dev/input/eventN is typically
/// root:input mode 660 — a user not in the input group gets EACCES on
/// open for every device and this reader silently finds nothing to
/// read (logged once, not thrown). That's an operational/permissions
/// concern for the person running GameFlow, not something this code
/// can fix.
/// </para>
/// </summary>
public sealed class LinuxRawInputReader : IKeyboardStateSource, IMouseStateSource, IDisposable
{
    private readonly ILogger<LinuxRawInputReader> logger;
    private readonly Lock gate = new();
    private readonly Dictionary<string, HashSet<int>> pressedKeysByDevice = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MouseAccumulator> mouseByDevice = new(StringComparer.Ordinal);
    private readonly List<(int Fd, Thread Thread)> readers = [];
    private volatile bool disposed;

    private static readonly IReadOnlySet<int> EmptyKeys = new HashSet<int>();

    private sealed class MouseAccumulator
    {
        // Drained to zero on every read — see MouseFrame's own doc comment.
        public int Dx;
        public int Dy;
        public int WheelDelta;

        // Level state — NOT drained; reflects current physical state.
        public bool Left;
        public bool Right;
        public bool Middle;
        public bool Button4;
        public bool Button5;
    }

    public LinuxRawInputReader(ILogger<LinuxRawInputReader> logger)
    {
        this.logger = logger;

        // Cheap, loud insurance: if struct marshaling ever produced a
        // different size than the verified 24-byte evdev wire format
        // (shouldn't happen for this exact field layout on 64-bit, but
        // this is unverifiable against real hardware in this
        // environment), every subsequent read() would silently
        // misinterpret the byte stream instead of failing clearly here.
        var marshaledSize = System.Runtime.InteropServices.Marshal.SizeOf<EvdevInterop.InputEvent>();
        if (marshaledSize != EvdevInterop.InputEventSize)
        {
            throw new InvalidOperationException(
                $"evdev: InputEvent marshaled to {marshaledSize} bytes, expected {EvdevInterop.InputEventSize}. " +
                "Refusing to start — reading with the wrong struct size would silently corrupt every event.");
        }

        var devices = EvdevDeviceDiscovery.Scan(logger);
        foreach (var device in devices)
        {
            if (device.IsKeyboard)
            {
                var id = EvdevDeviceDiscovery.BuildDeviceId("keyboard", device.Path);
                pressedKeysByDevice[id] = [];
                StartReaderThread(device.Path, id, isKeyboard: true, isMouse: false);
            }
            if (device.IsMouse)
            {
                var id = EvdevDeviceDiscovery.BuildDeviceId("mouse", device.Path);
                mouseByDevice[id] = new MouseAccumulator();
                StartReaderThread(device.Path, id, isKeyboard: false, isMouse: true);
            }
            // A device classifying as BOTH (rare composite hardware) gets
            // two independent fds/threads on the same device file — evdev
            // fans its full event stream out to every open fd
            // independently, and each thread here filters for only the
            // event kind it cares about. Slightly wasteful for the
            // uncommon composite case; simpler than one thread updating
            // two different pieces of shared state.
        }

        logger.LogInformation("evdev: {Count} keyboard/mouse reader thread(s) started from {Total} device(s) scanned.",
            readers.Count, devices.Count);
    }

    private void StartReaderThread(string path, string deviceId, bool isKeyboard, bool isMouse)
    {
        var fd = EvdevInterop.OpenReadOnly(path);
        if (fd < 0)
        {
            logger.LogDebug("evdev: could not open {Path} for continuous reading (permissions?).", path);
            return;
        }

        var thread = new Thread(() => ReadLoop(fd, deviceId, isKeyboard, isMouse))
        {
            IsBackground = true,
            Name = $"evdev-reader-{System.IO.Path.GetFileName(path)}-{(isKeyboard ? "kbd" : "mouse")}"
        };
        readers.Add((fd, thread));
        thread.Start();
    }

    private void ReadLoop(int fd, string deviceId, bool isKeyboard, bool isMouse)
    {
        var ev = new EvdevInterop.InputEvent();
        while (!disposed)
        {
            var bytesRead = (long)EvdevInterop.read(fd, ref ev, (nuint)EvdevInterop.InputEventSize);
            if (bytesRead <= 0)
            {
                // fd closed by Dispose(), or the device was unplugged / errored — either way, stop quietly.
                return;
            }
            if (bytesRead != EvdevInterop.InputEventSize)
            {
                continue; // short/partial read — skip rather than risk misinterpreting a malformed event
            }

            if (ev.Type == EvdevInterop.EV_KEY)
            {
                HandleKeyEvent(deviceId, isKeyboard, isMouse, ev.Code, ev.Value);
            }
            else if (ev.Type == EvdevInterop.EV_REL && isMouse)
            {
                HandleRelEvent(deviceId, ev.Code, ev.Value);
            }
        }
    }

    private void HandleKeyEvent(string deviceId, bool isKeyboard, bool isMouse, ushort code, int value)
    {
        var isDown = value != 0; // evdev: 0 = release, 1 = press, 2 = autorepeat (treated as still-down)

        if (isMouse)
        {
            lock (gate)
            {
                if (mouseByDevice.TryGetValue(deviceId, out var acc))
                {
                    switch (code)
                    {
                        case EvdevInterop.BTN_LEFT: acc.Left = isDown; return;
                        case EvdevInterop.BTN_RIGHT: acc.Right = isDown; return;
                        case EvdevInterop.BTN_MIDDLE: acc.Middle = isDown; return;
                        case EvdevInterop.BTN_SIDE: acc.Button4 = isDown; return;
                        case EvdevInterop.BTN_EXTRA: acc.Button5 = isDown; return;
                    }
                }
            }
        }

        if (isKeyboard && EvdevKeyCodeMap.EvdevToVirtualKey.TryGetValue(code, out var virtualKey))
        {
            lock (gate)
            {
                if (pressedKeysByDevice.TryGetValue(deviceId, out var keys))
                {
                    if (isDown) { keys.Add(virtualKey); } else { keys.Remove(virtualKey); }
                }
            }
        }
    }

    private void HandleRelEvent(string deviceId, ushort code, int value)
    {
        lock (gate)
        {
            if (!mouseByDevice.TryGetValue(deviceId, out var acc))
            {
                return;
            }
            switch (code)
            {
                case EvdevInterop.REL_X: acc.Dx += value; break;
                case EvdevInterop.REL_Y: acc.Dy += value; break;
                // *120 to match MouseFrame's documented "Windows wheel units (multiples of 120)" convention.
                case EvdevInterop.REL_WHEEL: acc.WheelDelta += value * 120; break;
            }
        }
    }

    public IReadOnlySet<int> GetPressedKeys(string deviceId)
    {
        lock (gate)
        {
            return pressedKeysByDevice.TryGetValue(deviceId, out var keys) && keys.Count > 0
                ? new HashSet<int>(keys)
                : EmptyKeys;
        }
    }

    public IReadOnlySet<int> GetPressedKeysAggregate()
    {
        lock (gate)
        {
            if (pressedKeysByDevice.Count == 0)
            {
                return EmptyKeys;
            }
            var union = new HashSet<int>();
            foreach (var keys in pressedKeysByDevice.Values)
            {
                union.UnionWith(keys);
            }
            return union.Count == 0 ? EmptyKeys : union;
        }
    }

    public MouseFrame ReadMouseFrame(string deviceId)
    {
        lock (gate)
        {
            return mouseByDevice.TryGetValue(deviceId, out var acc) ? Drain(acc) : default;
        }
    }

    public MouseFrame ReadMouseFrameAggregate()
    {
        lock (gate)
        {
            if (mouseByDevice.Count == 0)
            {
                return default;
            }

            int dx = 0, dy = 0, wheel = 0;
            bool left = false, right = false, middle = false, button4 = false, button5 = false;
            foreach (var acc in mouseByDevice.Values)
            {
                dx += acc.Dx; dy += acc.Dy; wheel += acc.WheelDelta;
                left |= acc.Left; right |= acc.Right; middle |= acc.Middle; button4 |= acc.Button4; button5 |= acc.Button5;
                acc.Dx = 0; acc.Dy = 0; acc.WheelDelta = 0;
            }
            return new MouseFrame(dx, dy, left, right, middle, button4, button5, wheel);
        }
    }

    /// <summary>Reads the accumulator into a frame and resets ONLY the drain fields (Dx/Dy/WheelDelta) — button levels persist. Caller already holds <see cref="gate"/>.</summary>
    private static MouseFrame Drain(MouseAccumulator acc)
    {
        var frame = new MouseFrame(acc.Dx, acc.Dy, acc.Left, acc.Right, acc.Middle, acc.Button4, acc.Button5, acc.WheelDelta);
        acc.Dx = 0;
        acc.Dy = 0;
        acc.WheelDelta = 0;
        return frame;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        // Closing the fd is the standard way to unblock a thread stuck in
        // a blocking read() on Linux — the syscall returns (error/EOF)
        // and the loop's `while (!disposed)` check then exits it cleanly.
        foreach (var (fd, _) in readers)
        {
            _ = EvdevInterop.close(fd);
        }
        foreach (var (_, thread) in readers)
        {
            _ = thread.Join(TimeSpan.FromSeconds(1));
        }
    }
}
