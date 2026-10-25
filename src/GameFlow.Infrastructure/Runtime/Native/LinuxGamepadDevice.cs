using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Input.Linux;

namespace GameFlow.Infrastructure.Runtime.Native;

/// <summary>Creates a Linux gamepad through uinput. The caller must have write access to /dev/uinput.</summary>
internal sealed partial class LinuxGamepadDevice : INativeGamepadDevice
{
    private const ushort AbsoluteEvent = 3;
    private const nuint SetAbsoluteBit = 0x40045567;
    private const nuint SetupAbsoluteAxis = 0x401C5504;
    private const nuint SetPhysicalPath = 0x4008556C;
    private int descriptor = -1;

    /// <summary>Linux gamepad button codes. Extra buttons use BTN_TRIGGER_HAPPY codes.</summary>
    private static ReadOnlySpan<ushort> ButtonCodes =>
        [304, 305, 308, 307, 310, 311, 314, 315, 316, 317, 318, 312, 313, 704, 705, 706, 707, 708, 709];

    /// <summary>Axes in report order. Linux ABS_Z and ABS_RZ hold trigger values.</summary>
    private static ReadOnlySpan<ushort> AxisCodes => [0, 1, 3, 4, 2, 5, 16, 17];

    /// <summary>Creates the device. A failed setup always closes its file descriptor.</summary>
    public LinuxGamepadDevice()
    {
        descriptor = UinputInterop.OpenWriteOnlyNonBlocking("/dev/uinput");
        if (descriptor < 0) { throw Failure("Open /dev/uinput. Load the uinput module and grant the current user write access."); }
        try
        {
            Check(UinputInterop.ioctl_intarg(descriptor, UinputInterop.UI_SET_EVBIT, EvdevInterop.EV_KEY));
            Check(UinputInterop.ioctl_intarg(descriptor, UinputInterop.UI_SET_EVBIT, AbsoluteEvent));
            foreach (var code in ButtonCodes)
            {
                Check(UinputInterop.ioctl_intarg(descriptor, UinputInterop.UI_SET_KEYBIT, code));
            }
            var axes = AxisCodes;
            for (var i = 0; i < axes.Length; i++)
            {
                Check(UinputInterop.ioctl_intarg(descriptor, SetAbsoluteBit, axes[i]));
                var axis = new AbsoluteSetup
                {
                    Code = axes[i],
                    Minimum = i >= 6 ? -1 : i >= 4 ? 0 : -32767,
                    Maximum = i >= 6 ? 1 : 32767,
                };
                Check(SetupAxis(descriptor, SetupAbsoluteAxis, ref axis));
            }
            Check(SetPath(descriptor, SetPhysicalPath, "gameflow-output/" + Guid.NewGuid().ToString("N")));
            var setup = new UinputInterop.UinputSetup
            {
                Id = new UinputInterop.InputId
                {
                    BusType = UinputInterop.BusVirtual,
                    Vendor = NativeGamepadReport.VendorId,
                    Product = NativeGamepadReport.ProductId,
                    Version = 1,
                },
                Name = UinputInterop.BuildFixedName(NativeGamepadReport.ProductName),
            };
            Check(UinputInterop.ioctl_uinputsetup(descriptor, UinputInterop.UI_DEV_SETUP, ref setup));
            Check(UinputInterop.ioctl_noarg(descriptor, UinputInterop.UI_DEV_CREATE));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Write(ControllerSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(descriptor < 0, this);
        Span<byte> report = stackalloc byte[NativeGamepadReport.Length];
        NativeGamepadReport.Encode(snapshot, report);
        Span<EvdevInterop.InputEvent> events = stackalloc EvdevInterop.InputEvent[28];
        events.Clear();
        var codes = ButtonCodes;
        var buttons = BinaryPrimitives.ReadUInt32LittleEndian(report);
        for (var i = 0; i < codes.Length; i++)
        {
            events[i] = new EvdevInterop.InputEvent { Type = EvdevInterop.EV_KEY, Code = codes[i], Value = (int)((buttons >> i) & 1) };
        }
        var axes = AxisCodes;
        for (var i = 0; i < 6; i++)
        {
            events[codes.Length + i] = new EvdevInterop.InputEvent
            {
                Type = AbsoluteEvent,
                Code = axes[i],
                Value = BinaryPrimitives.ReadInt16LittleEndian(report[(4 + (i * 2))..]),
            };
        }
        var hat = report[16];
        events[25] = new EvdevInterop.InputEvent { Type = AbsoluteEvent, Code = 16, Value = hat is 1 or 2 or 3 ? 1 : hat is 5 or 6 or 7 ? -1 : 0 };
        events[26] = new EvdevInterop.InputEvent { Type = AbsoluteEvent, Code = 17, Value = hat is 3 or 4 or 5 ? 1 : hat is 7 or 0 or 1 ? -1 : 0 };
        events[27] = new EvdevInterop.InputEvent { Type = EvdevInterop.EV_SYN, Code = 0 };
        var bytes = MemoryMarshal.AsBytes(events);
        var written = WriteEvents(descriptor, bytes, (nuint)bytes.Length);
        if (written != bytes.Length) { throw Failure("Write the uinput gamepad report."); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var previous = Interlocked.Exchange(ref descriptor, -1);
        if (previous < 0) { return; }
        _ = UinputInterop.ioctl_noarg(previous, UinputInterop.UI_DEV_DESTROY);
        _ = EvdevInterop.close(previous);
    }

    /// <summary>Reports the native error immediately, before cleanup changes errno.</summary>
    private static IOException Failure(string operation) => new(operation, new Win32Exception(Marshal.GetLastPInvokeError()));

    /// <summary>Rejects a failed uinput setup operation.</summary>
    private static void Check(int result)
    {
        if (result < 0) { throw Failure("Configure the uinput gamepad."); }
    }

    /// <summary>Linux uinput_abs_setup. Padding aligns input_absinfo on a four-byte boundary.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AbsoluteSetup
    {
        public ushort Code;
        public int Value;
        public int Minimum;
        public int Maximum;
        public int Fuzz;
        public int Flat;
        public int Resolution;
    }

    /// <summary>Configures the range of one absolute axis.</summary>
    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int SetupAxis(int descriptor, nuint request, ref AbsoluteSetup setup);

    /// <summary>Sets the device's physical provenance marker.</summary>
    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetPath(int descriptor, nuint request, string path);

    /// <summary>Submits one complete event batch to the kernel.</summary>
    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static partial nint WriteEvents(int descriptor, ReadOnlySpan<byte> events, nuint count);
}
