using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Input.Linux;

/// <summary>One /dev/input/eventN device, classified.</summary>
internal readonly record struct EvdevDeviceInfo(string Path, string Name, bool IsKeyboard, bool IsMouse);

/// <summary>
/// Scans <c>/dev/input/event*</c> and classifies each as keyboard/mouse
/// via capability bitmasks — shared by <see cref="LinuxInputDeviceScanner"/>
/// (publishes to the catalog for the device picker) and
/// <see cref="LinuxRawInputReader"/> (opens reader threads). Both MUST
/// use <see cref="BuildDeviceId"/> for the same device to agree, or
/// selecting a device in the UI silently reads as neutral input — see
/// RawInputDeviceScanner.BuildDeviceId's identical warning on the
/// Windows side of this same problem.
/// </summary>
internal static class EvdevDeviceDiscovery
{
    /// <summary><paramref name="kind"/> is "keyboard" or "mouse".</summary>
    public static string BuildDeviceId(string kind, string path) => $"evdev-{kind}-{path}";

    /// <summary>
    /// Enumerates /dev/input/event*, opening each briefly to query its
    /// capability bitmask (EVIOCGBIT) and name (EVIOCGNAME), then
    /// closing it — the actual long-lived read handle a keyboard/mouse
    /// gets is opened separately by <see cref="LinuxRawInputReader"/>.
    /// A device can classify as both (rare composite devices exist) or
    /// neither (ignored). Devices this process can't open (permissions —
    /// typically needs membership in the "input" group) are silently
    /// skipped rather than throwing, since one unreadable device
    /// shouldn't blank the whole list.
    /// </summary>
    public static IReadOnlyList<EvdevDeviceInfo> Scan(ILogger? logger = null)
    {
        var results = new List<EvdevDeviceInfo>();

        const string devInputDir = "/dev/input";
        if (!Directory.Exists(devInputDir))
        {
            return results;
        }

        string[] paths;
        try
        {
            paths = Directory.GetFiles(devInputDir, "event*");
        }
        catch (Exception exception)
        {
            logger?.LogDebug(exception, "Failed to list {Dir}.", devInputDir);
            return results;
        }

        foreach (var path in paths.OrderBy(p => p, StringComparer.Ordinal))
        {
            var fd = EvdevInterop.OpenReadOnly(path);
            if (fd < 0)
            {
                continue; // typically EACCES — no permission (needs "input" group) — skip, don't fail the whole scan
            }

            try
            {
                var isKeyboard = HasCapability(fd, EvdevInterop.EviocgbitEvKey(KeyBitmaskBytes), KeyboardProbeBits);
                var isMouse = HasCapability(fd, EvdevInterop.EviocgbitEvKey(KeyBitmaskBytes), MouseButtonProbeBits)
                    && HasCapability(fd, EvdevInterop.EviocgbitEvRel(RelBitmaskBytes), MouseAxisProbeBits);

                if (!isKeyboard && !isMouse)
                {
                    continue;
                }

                var name = ReadName(fd, path);
                results.Add(new EvdevDeviceInfo(path, name, isKeyboard, isMouse));
            }
            finally
            {
                _ = EvdevInterop.close(fd);
            }
        }

        return results;
    }

    // KEY bitmask needs to cover the highest KEY_* code we probe
    // (KEY_A=30) plus the mouse BTN_* range (BTN_LEFT=0x110=272) —
    // 288 bits / 8 = 36 bytes comfortably covers both with room to spare.
    private const int KeyBitmaskBytes = 36;
    private const int RelBitmaskBytes = 4; // REL_WHEEL=8 is the highest code this needs; 4 bytes = 32 bits covers it

    // Probe sets: (byte index, bit index) pairs checked against the
    // bitmask ioctl returned. A device only needs to hit EITHER probe
    // set fully to classify — not an exhaustive capability check, just
    // enough signal to distinguish "this is a real keyboard/mouse" from
    // "this is some other HID with a couple of buttons."
    private static readonly (int Byte, int Bit)[] KeyboardProbeBits =
        [BitOf(EvdevInterop.KEY_A), BitOf(EvdevInterop.KEY_S), BitOf(EvdevInterop.KEY_SPACE)];

    private static readonly (int Byte, int Bit)[] MouseButtonProbeBits = [BitOf(EvdevInterop.BTN_LEFT)];
    private static readonly (int Byte, int Bit)[] MouseAxisProbeBits = [BitOf(EvdevInterop.REL_X), BitOf(EvdevInterop.REL_Y)];

    private static (int, int) BitOf(ushort code) => (code / 8, code % 8);

    private static bool HasCapability(int fd, nuint ioctlRequest, (int Byte, int Bit)[] probes)
    {
        var buffer = new byte[KeyBitmaskBytes]; // sized for the larger of the two probe kinds; ioctl only writes what the kernel reports
        if (EvdevInterop.ioctl_bits(fd, ioctlRequest, buffer) < 0)
        {
            return false;
        }

        foreach (var (byteIndex, bitIndex) in probes)
        {
            if (byteIndex >= buffer.Length || (buffer[byteIndex] & (1 << bitIndex)) == 0)
            {
                return false; // ALL probe bits must be set — a partial match isn't confident enough
            }
        }
        return true;
    }

    private static string ReadName(int fd, string fallbackPath)
    {
        var buffer = new byte[256];
        if (EvdevInterop.ioctl_name(fd, EvdevInterop.Eviocgname(buffer.Length), buffer) < 0)
        {
            return fallbackPath;
        }

        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
        {
            length = buffer.Length;
        }
        var name = System.Text.Encoding.UTF8.GetString(buffer, 0, length).Trim();
        return string.IsNullOrEmpty(name) ? fallbackPath : name;
    }
}
