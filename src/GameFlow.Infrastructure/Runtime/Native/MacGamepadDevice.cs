using System.Runtime.InteropServices;
using System.Text;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Input.Mac;

namespace GameFlow.Infrastructure.Runtime.Native;

/// <summary>Publishes a standard HID gamepad through IOKit. Apple must authorize the virtual HID entitlement.</summary>
internal sealed partial class MacGamepadDevice : INativeGamepadDevice
{
    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private IntPtr device;

    /// <summary>Creates a virtual device. Unsigned distributions report the missing Apple capability.</summary>
    public MacGamepadDevice()
    {
        var descriptor = Convert.ToBase64String(NativeGamepadReport.Descriptor);
        var properties = Encoding.UTF8.GetBytes($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
            <key>ReportDescriptor</key><data>{descriptor}</data>
            <key>VendorID</key><integer>{NativeGamepadReport.VendorId}</integer>
            <key>ProductID</key><integer>{NativeGamepadReport.ProductId}</integer>
            <key>Product</key><string>{NativeGamepadReport.ProductName}</string>
            <key>Manufacturer</key><string>GameFlow</string>
            <key>SerialNumber</key><string>gameflow-output-{Guid.NewGuid():N}</string>
            <key>Transport</key><string>Virtual</string>
            <key>PrimaryUsagePage</key><integer>1</integer>
            <key>PrimaryUsage</key><integer>5</integer>
            </dict></plist>
            """);
        var data = CreateData(IntPtr.Zero, properties, properties.Length);
        if (data == IntPtr.Zero) { throw new IOException("Create the virtual gamepad property data."); }
        try
        {
            var dictionary = CreatePropertyList(IntPtr.Zero, data, 0, out _, out var error);
            if (error != IntPtr.Zero) { MacHidInterop.CFRelease(error); }
            if (dictionary == IntPtr.Zero) { throw new IOException("Read the virtual gamepad properties."); }
            try { device = CreateDevice(IntPtr.Zero, dictionary); }
            finally { MacHidInterop.CFRelease(dictionary); }
        }
        finally { MacHidInterop.CFRelease(data); }
        if (device == IntPtr.Zero)
        {
            throw new UnauthorizedAccessException(
                "macOS virtual output requires a Developer ID signature and an Apple-approved com.apple.developer.hid.virtual.device entitlement.");
        }
    }

    /// <inheritdoc />
    public void Write(ControllerSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(device == IntPtr.Zero, this);
        Span<byte> report = stackalloc byte[NativeGamepadReport.Length];
        NativeGamepadReport.Encode(snapshot, report);
        var result = HandleReport(device, report, report.Length);
        if (result != 0) { throw new IOException($"IOHIDUserDeviceHandleReport failed with IOReturn 0x{result:X8}."); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var previous = Interlocked.Exchange(ref device, IntPtr.Zero);
        if (previous != IntPtr.Zero) { MacHidInterop.CFRelease(previous); }
    }

    /// <summary>Creates a virtual device from a retained property dictionary.</summary>
    [LibraryImport(IOKit, EntryPoint = "IOHIDUserDeviceCreate")]
    private static partial IntPtr CreateDevice(IntPtr allocator, IntPtr properties);

    /// <summary>Submits one HID input report.</summary>
    [LibraryImport(IOKit, EntryPoint = "IOHIDUserDeviceHandleReport")]
    private static partial int HandleReport(IntPtr device, ReadOnlySpan<byte> report, nint length);

    /// <summary>Copies setup bytes into an owned Core Foundation object.</summary>
    [LibraryImport(CoreFoundation, EntryPoint = "CFDataCreate")]
    private static partial IntPtr CreateData(IntPtr allocator, ReadOnlySpan<byte> bytes, nint length);

    /// <summary>Creates the device property dictionary from a property list.</summary>
    [LibraryImport(CoreFoundation, EntryPoint = "CFPropertyListCreateWithData")]
    private static partial IntPtr CreatePropertyList(IntPtr allocator, IntPtr data, nuint options, out nint format, out IntPtr error);
}
