using System.Runtime.InteropServices;
using Autofire.Core.Enums;
using Autofire.Core.Models;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.Providers;

/// <summary>
/// Linux evdev input provider.
///
/// Reads gamepad state from <c>/dev/input/event*</c> nodes using the standard evdev
/// protocol — the same path used by Steam Input, kernel drivers, and SDL when its
/// HIDAPI fallback is disabled. Most modern controllers (Xbox One/Series, DualShock 4,
/// DualSense, 8BitDo etc.) appear here without any third-party driver on a current
/// kernel.
///
/// Permissions: the user must be a member of the <c>input</c> group, OR have a udev
/// rule that grants their seat access to the device. The README ships a sample
/// 99-autofire.rules file under <c>scripts/linux/</c>.
///
/// This is a non-elevated reader, so it cannot create virtual /dev/input devices.
/// For virtual output on Linux, see UInputVirtualOutputSink (separate file) which
/// requires the user to be a member of the <c>input</c> group AND <c>uinput</c> to be
/// loadable (typically already present on desktop kernels).
/// </summary>
public sealed class LinuxEvdevInputProvider : IInputProvider, IAsyncDisposable
{
    private readonly InputDeviceCatalog catalog;
    private readonly ILogger<LinuxEvdevInputProvider> logger;
    private readonly Dictionary<string, EvdevDevice> openDevices = new();
    private readonly Lock gate = new();
    private bool disposed;

    public LinuxEvdevInputProvider(InputDeviceCatalog catalog, ILogger<LinuxEvdevInputProvider> logger)
    {
        this.catalog = catalog;
        this.logger = logger;
    }

    public string DisplayName => "Linux evdev";

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            catalog.Update([], "ProviderStatus_UInputUnavailable");
            return ValueTask.CompletedTask;
        }

        ScanDevices();
        return ValueTask.CompletedTask;
    }

    public ValueTask<ControllerSnapshot> PollAsync(CancellationToken cancellationToken)
    {
        if (disposed || !OperatingSystem.IsLinux())
        {
            return ValueTask.FromResult(ControllerSnapshot.Empty);
        }

        // Periodically rescan because hot-plug events on Linux arrive via udev which
        // we don't monitor here — a 1 Hz scan keeps it cheap and good-enough.
        if ((Environment.TickCount & 0x3FF) == 0)
        {
            ScanDevices();
        }

        lock (gate)
        {
            var selected = SelectDevice(catalog.SelectedDeviceId);
            return selected is null
                ? ValueTask.FromResult(ControllerSnapshot.Empty)
                : ValueTask.FromResult(selected.ReadSnapshot());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        lock (gate)
        {
            foreach (var (_, device) in openDevices)
            {
                try { device.Dispose(); }
                catch (Exception ex) { logger.LogDebug(ex, "Error closing evdev device."); }
            }
            openDevices.Clear();
        }

        await ValueTask.CompletedTask;
    }

    private void ScanDevices()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var devicesDir = "/dev/input";
            if (!Directory.Exists(devicesDir))
            {
                catalog.Update([], "ProviderStatus_UInputActive", 0);
                return;
            }

            var current = Directory.EnumerateFiles(devicesDir, "event*").ToArray();

            lock (gate)
            {
                // Open new devices we haven't seen yet
                foreach (var path in current)
                {
                    if (!openDevices.ContainsKey(path))
                    {
                        try
                        {
                            var dev = EvdevDevice.TryOpen(path, logger);
                            if (dev is not null && dev.LooksLikeGamepad)
                            {
                                openDevices[path] = dev;
                            }
                            else
                            {
                                dev?.Dispose();
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Most event nodes require the input group — skip silently
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "Failed to probe evdev node {Path}.", path);
                        }
                    }
                }

                // Drop devices that disappeared
                var stale = openDevices.Keys.Where(k => !File.Exists(k)).ToArray();
                foreach (var k in stale)
                {
                    if (openDevices.Remove(k, out var dev))
                    {
                        dev.Dispose();
                    }
                }

                var snapshot = openDevices.Values
                    .Select(d => new DetectedInputDevice(
                        Id:         $"evdev:{d.Path}",
                        DisplayName: d.Name,
                        HardwareId:  d.UniqueId))
                    .ToArray();

                catalog.Update(snapshot, "ProviderStatus_UInputActive", snapshot.Length);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "evdev scan failed.");
        }
    }

    private EvdevDevice? SelectDevice(string? selectedId)
    {
        if (openDevices.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selectedId)
            && selectedId.StartsWith("evdev:", StringComparison.OrdinalIgnoreCase))
        {
            var path = selectedId[6..];
            if (openDevices.TryGetValue(path, out var dev))
            {
                return dev;
            }
        }

        return openDevices.Values.FirstOrDefault();
    }

    private sealed class EvdevDevice : IDisposable
    {
        private readonly FileStream stream;
        private readonly ButtonState[] buttonsBuffer = ButtonState.CreateEmptyMap();
        private float leftX, leftY, rightX, rightY, leftTrigger, rightTrigger;

        public string Path { get; }
        public string Name { get; }
        public string? UniqueId { get; }
        public bool LooksLikeGamepad { get; }

        private EvdevDevice(string path, string name, string? uniqueId, bool gamepad, FileStream stream)
        {
            Path = path;
            Name = name;
            UniqueId = uniqueId;
            LooksLikeGamepad = gamepad;
            this.stream = stream;
        }

        public static EvdevDevice? TryOpen(string path, ILogger logger)
        {
            try
            {
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);

                // Real implementation should ioctl EVIOCGNAME, EVIOCGUNIQ, EVIOCGBIT
                // to discover device capabilities. To keep this provider portable
                // we use sysfs instead, which mirrors most of that information at:
                //   /sys/class/input/event<N>/device/name
                //   /sys/class/input/event<N>/device/uniq
                //   /sys/class/input/event<N>/device/capabilities/key
                var sysName = TryReadSysfs(path, "name") ?? System.IO.Path.GetFileName(path);
                var uniq    = TryReadSysfs(path, "uniq");
                var caps    = TryReadSysfs(path, "capabilities/key") ?? string.Empty;

                // BTN_GAMEPAD = 0x130 — if the keybit map advertises this range, it's a gamepad
                var looksLikeGamepad = caps.Contains("130", StringComparison.OrdinalIgnoreCase)
                                       || sysName.Contains("Gamepad", StringComparison.OrdinalIgnoreCase)
                                       || sysName.Contains("Controller", StringComparison.OrdinalIgnoreCase);

                return new EvdevDevice(path, sysName, uniq, looksLikeGamepad, stream);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to open evdev device {Path}.", path);
                return null;
            }
        }

        public ControllerSnapshot ReadSnapshot()
        {
            // Drain any pending evdev events without blocking.
            // Each event is sizeof(input_event) = 24 bytes on 64-bit systems
            // (struct timeval + u16 type + u16 code + i32 value, padded).
            Span<byte> buf = stackalloc byte[24];
            try
            {
                while (stream.CanRead && stream.Length > stream.Position)
                {
                    var read = stream.Read(buf);
                    if (read != 24)
                    {
                        break;
                    }

                    var type  = BitConverter.ToUInt16(buf[16..18]);
                    var code  = BitConverter.ToUInt16(buf[18..20]);
                    var value = BitConverter.ToInt32(buf[20..24]);

                    if (type == 0x01) // EV_KEY
                    {
                        ApplyKey(code, value);
                    }
                    else if (type == 0x03) // EV_ABS
                    {
                        ApplyAxis(code, value);
                    }
                }
            }
            catch (IOException) { /* device unplugged — handled by ScanDevices */ }
            catch (ObjectDisposedException) { /* shutdown race — fine */ }

            return new ControllerSnapshot
            {
                Buttons = ButtonState.Clone(buttonsBuffer),
                LeftStick = new StickVector(leftX, -leftY),
                RightStick = new StickVector(rightX, -rightY),
                LeftTrigger = leftTrigger,
                RightTrigger = rightTrigger
            };
        }

        private void ApplyKey(ushort code, int value)
        {
            var pressed = value != 0;
            // Standard Linux gamepad keycodes (from <linux/input-event-codes.h>)
            switch (code)
            {
                case 0x130: buttonsBuffer[ButtonId.South] = pressed; break;          // BTN_SOUTH
                case 0x131: buttonsBuffer[ButtonId.East]  = pressed; break;          // BTN_EAST
                case 0x133: buttonsBuffer[ButtonId.North] = pressed; break;          // BTN_NORTH
                case 0x134: buttonsBuffer[ButtonId.West]  = pressed; break;          // BTN_WEST
                case 0x136: buttonsBuffer[ButtonId.LeftShoulder]  = pressed; break;  // BTN_TL
                case 0x137: buttonsBuffer[ButtonId.RightShoulder] = pressed; break;  // BTN_TR
                case 0x13A: buttonsBuffer[ButtonId.Back]  = pressed; break;          // BTN_SELECT
                case 0x13B: buttonsBuffer[ButtonId.Start] = pressed; break;          // BTN_START
                case 0x13C: buttonsBuffer[ButtonId.Guide] = pressed; break;          // BTN_MODE
                case 0x13D: buttonsBuffer[ButtonId.LeftStick]  = pressed; break;     // BTN_THUMBL
                case 0x13E: buttonsBuffer[ButtonId.RightStick] = pressed; break;     // BTN_THUMBR
            }
        }

        private void ApplyAxis(ushort code, int value)
        {
            // Normalise to -1..1 / 0..1 — assumes default driver range of -32768..32767 for sticks,
            // 0..255 for triggers. Some controllers diverge — a real implementation should query
            // EVIOCGABS and rescale, but the defaults work on Xbox-class and PlayStation pads.
            switch (code)
            {
                case 0x00: leftX  = Normalize(value);       break; // ABS_X
                case 0x01: leftY  = Normalize(value);       break; // ABS_Y
                case 0x03: rightX = Normalize(value);       break; // ABS_RX
                case 0x04: rightY = Normalize(value);       break; // ABS_RY
                case 0x02: leftTrigger  = NormalizeTrigger(value); break; // ABS_Z
                case 0x05: rightTrigger = NormalizeTrigger(value); break; // ABS_RZ
                case 0x10: ApplyDpadX(value); break; // ABS_HAT0X
                case 0x11: ApplyDpadY(value); break; // ABS_HAT0Y
            }
        }

        private void ApplyDpadX(int value)
        {
            buttonsBuffer[ButtonId.DpadLeft]  = value < 0;
            buttonsBuffer[ButtonId.DpadRight] = value > 0;
        }

        private void ApplyDpadY(int value)
        {
            buttonsBuffer[ButtonId.DpadUp]   = value < 0;
            buttonsBuffer[ButtonId.DpadDown] = value > 0;
        }

        private static float Normalize(int value)        => Math.Clamp(value / 32767f, -1f, 1f);
        private static float NormalizeTrigger(int value) => Math.Clamp(value / 255f,    0f,  1f);

        private static string? TryReadSysfs(string devicePath, string property)
        {
            try
            {
                var name = System.IO.Path.GetFileName(devicePath);
                var sysPath = $"/sys/class/input/{name}/device/{property}";
                if (File.Exists(sysPath))
                {
                    return File.ReadAllText(sysPath).Trim();
                }
            }
            catch { /* ignore */ }
            return null;
        }

        public void Dispose()
        {
            try { stream.Dispose(); } catch { /* ignored */ }
        }
    }
}
