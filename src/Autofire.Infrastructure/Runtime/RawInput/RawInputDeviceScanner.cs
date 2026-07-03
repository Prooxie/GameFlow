using System.Globalization;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace Autofire.Infrastructure.Runtime.RawInput;

/// <summary>
/// Enumerates keyboards and mice via the Windows Raw Input API
/// (<c>GetRawInputDeviceList</c> / <c>GetRawInputDeviceInfo</c>). These
/// are pure query calls — no window handle or message pump is required
/// just to list devices (that's only needed to *receive* WM_INPUT).
///
/// <para>HID-class devices (type 2) are intentionally skipped: gamepads
/// and joysticks come from SDL3, and listing them here too would
/// duplicate them. Only keyboards (type 1) and mice (type 0) are
/// returned.</para>
///
/// <para>Friendly names: without a registry lookup (avoided here to keep
/// the dependency surface clean) the Raw Input path only yields VID/PID,
/// so names are generic ("Keyboard"/"Mouse") enriched with VID:PID. A
/// registry <c>DeviceDesc</c>/<c>FriendlyName</c> lookup is a future
/// enhancement.</para>
///
/// <para><b>Windows-only.</b> Returns an empty list on other platforms.</para>
/// </summary>
internal static partial class RawInputDeviceScanner
{
    private const uint RimTypeMouse = 0;
    private const uint RimTypeKeyboard = 1;
    private const uint RidiDeviceName = 0x20000007;
    private const uint Failed = unchecked((uint)-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [Out] RawInputDeviceList[]? pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    public static IReadOnlyList<InputDeviceInfo> Scan()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        uint count = 0;
        var entrySize = (uint)Marshal.SizeOf<RawInputDeviceList>();

        // First call: discover how many devices exist.
        if (GetRawInputDeviceList(null, ref count, entrySize) == Failed || count == 0)
        {
            return [];
        }

        var list = new RawInputDeviceList[count];
        var copied = GetRawInputDeviceList(list, ref count, entrySize);
        if (copied == Failed)
        {
            return [];
        }

        var result = new List<InputDeviceInfo>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limit = (int)Math.Min(copied, (uint)list.Length);

        for (var i = 0; i < limit; i++)
        {
            var entry = list[i];

            DeviceCategory category;
            string kind;
            if (entry.dwType == RimTypeKeyboard)
            {
                category = DeviceCategory.Keyboard;
                kind = "keyboard";
            }
            else if (entry.dwType == RimTypeMouse)
            {
                category = DeviceCategory.Mouse;
                kind = "mouse";
            }
            else
            {
                continue; // HID — handled by SDL3.
            }

            var path = GetDeviceName(entry.hDevice);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            // Skip remote-desktop / virtual phantom devices.
            if (path.Contains("RDP_", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Root#RDP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (vid, pid) = ParseVidPid(path);

            // Endpoints whose interface path carries no VID/PID are, in
            // practice, phantoms: the motherboard's PS/2 controller with
            // nothing attached, vendor-software virtual devices, and the
            // like. They never produce input and the UI can't present them
            // meaningfully (no product, no hardware id) — hide them.
            // Revisit with an explicit "show system devices" toggle if a
            // real identifier-less device (bare PS/2 keyboard) ever needs
            // to surface.
            if (vid == 0 && pid == 0)
            {
                continue;
            }
            var label = category == DeviceCategory.Keyboard ? "Keyboard" : "Mouse";
            var name = vid != 0 || pid != 0
                ? $"{label} ({vid:X4}:{pid:X4})"
                : label;

            // Windows exposes one physical device as several Raw Input
            // endpoints (per HID top-level collection: media keys, system
            // control, NKRO...). BuildDeviceId collapses sibling collections
            // of the same interface into one id, so dedupe here — distinct
            // hardware keeps distinct instance paths and stays separate.
            var id = BuildDeviceId(kind, path);
            if (!seenIds.Add(id))
            {
                continue;
            }

            result.Add(new InputDeviceInfo(
                id,
                name,
                true,
                false,
                vid,
                pid,
                false,
                category));
        }

        return result;
    }

    private static string? GetDeviceName(IntPtr hDevice)
    {
        uint charCount = 0;
        if (GetRawInputDeviceInfo(hDevice, RidiDeviceName, IntPtr.Zero, ref charCount) == Failed || charCount == 0)
        {
            return null;
        }

        // pcbSize is in characters for RIDI_DEVICENAME; allocate UTF-16 bytes.
        var buffer = Marshal.AllocHGlobal((int)checked(charCount * 2 + 2));
        try
        {
            if (GetRawInputDeviceInfo(hDevice, RidiDeviceName, buffer, ref charCount) == Failed)
            {
                return null;
            }
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Parses VID/PID out of a Raw Input device interface path such as
    /// <c>\\?\HID#VID_046D&amp;PID_C52B#...</c>. Returns (0,0) for PS/2 or
    /// ACPI devices that carry no USB ids.
    /// </summary>
    private static (ushort Vid, ushort Pid) ParseVidPid(string path)
    {
        return (ReadHexToken(path, "VID_"), ReadHexToken(path, "PID_"));
    }

    private static ushort ReadHexToken(string path, string marker)
    {
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || index + marker.Length + 4 > path.Length)
        {
            return 0;
        }

        var token = path.Substring(index + marker.Length, 4);
        return ushort.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : (ushort)0;
    }

    /// <summary>Deterministic FNV-1a hash so a device's id is stable across polls.</summary>
    /// <summary>
    /// Canonical catalog id for a Raw Input keyboard/mouse. Single source of
    /// truth shared with <see cref="Input.WindowsRawInputReader"/>: the
    /// enumerator publishes each device under this id and the reader stores its
    /// captured state under the same id, so state lookups by id resolve. If the
    /// two ever disagree, keyboard/mouse input silently reads as neutral.
    /// <paramref name="kind"/> is "keyboard" or "mouse".
    /// </summary>
    public static string BuildDeviceId(string kind, string path) =>
        $"rawinput-{kind}-{StableHash(NormalizeDevicePath(path))}";

    // Collapses the HID collection ordinal out of a device interface path so
    // sibling top-level collections of the SAME physical interface share one
    // id: "...&MI_00&Col02#8&1ea11a2b&0&0001#{guid}" and
    // "...&MI_00&Col01#8&1ea11a2b&0&0000#{guid}" both normalize to the Col01
    // form. The instance hash ("8&1ea11a2b&0") differs between physically
    // distinct devices, so real multi-keyboard/multi-mouse setups keep
    // separate identities. Case is folded because device paths are
    // case-insensitive.
    private static string NormalizeDevicePath(string path)
    {
        var normalized = ColToken().Replace(path, "");
        normalized = TrailingOrdinal().Replace(normalized, "&0000#");
        return normalized.ToUpperInvariant();
    }

    [GeneratedRegex(@"&[Cc][Oo][Ll]\d+")]
    private static partial Regex ColToken();

    [GeneratedRegex(@"&\d{4}#")]
    private static partial Regex TrailingOrdinal();

    private static string StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
