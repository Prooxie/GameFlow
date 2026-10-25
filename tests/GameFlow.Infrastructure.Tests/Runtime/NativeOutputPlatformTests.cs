using System.Buffers.Binary;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Input.Linux;
using GameFlow.Infrastructure.Runtime.Native;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>Exercises operating-system output on prepared CI hosts.</summary>
public sealed class NativeOutputPlatformTests
{
    /// <summary>Linux CI enables the probe after loading uinput and installing test-only device permissions.</summary>
    public static bool RunLinuxProbe => OperatingSystem.IsLinux()
        && Environment.GetEnvironmentVariable("GAMEFLOW_TEST_UINPUT") == "1";

    /// <summary>macOS CI checks the capability boundary without a restricted entitlement.</summary>
    public static bool RunMacProbe => OperatingSystem.IsMacOS();

    /// <summary>Reads the published device state through evdev and checks disconnect cleanup.</summary>
    [Fact(Skip = "Requires a prepared Linux uinput host.", SkipUnless = nameof(RunLinuxProbe))]
    public async Task LinuxDeviceWritesRealEventsAndDisconnectsAsync()
    {
        var before = Directory.GetDirectories("/sys/class/input", "event*").ToHashSet(StringComparer.Ordinal);
        var device = new NativeGamepadDeviceFactory().Create();
        string? sysPath = null;
        try
        {
            for (var attempt = 0; attempt < 50 && sysPath is null; attempt++)
            {
                sysPath = Directory.GetDirectories("/sys/class/input", "event*")
                    .FirstOrDefault(path => !before.Contains(path)
                        && File.ReadAllText(Path.Combine(path, "device/name")).Trim() == NativeGamepadReport.ProductName);
                if (sysPath is null) { await Task.Delay(100, TestContext.Current.CancellationToken); }
            }
            Assert.NotNull(sysPath);
            var path = "/dev/input/" + Path.GetFileName(sysPath);
            var descriptor = -1;
            for (var attempt = 0; attempt < 50 && descriptor < 0; attempt++)
            {
                descriptor = EvdevInterop.OpenNonBlocking(path);
                if (descriptor < 0) { await Task.Delay(100, TestContext.Current.CancellationToken); }
            }
            Assert.True(descriptor >= 0, "The test host must grant access to its created event device.");
            try
            {
                var buttons = ButtonMask.Empty;
                buttons[ButtonId.South] = true;
                device.Write(ControllerSnapshot.Empty() with { LeftStick = new StickVector(1, 0), Buttons = buttons });
                var absolute = new byte[24];
                Assert.True(EvdevInterop.ioctl_bits(descriptor, 0x80184540, absolute) >= 0);
                Assert.Equal(32767, BinaryPrimitives.ReadInt32LittleEndian(absolute));
                var keys = new byte[96];
                Assert.True(EvdevInterop.ioctl_bits(descriptor, 0x80604518, keys) >= 0);
                Assert.NotEqual(0, keys[304 / 8] & (1 << (304 % 8)));

                device.Write(ControllerSnapshot.Empty());

                Assert.True(EvdevInterop.ioctl_bits(descriptor, 0x80184540, absolute) >= 0);
                Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(absolute));
                Assert.True(EvdevInterop.ioctl_bits(descriptor, 0x80604518, keys) >= 0);
                Assert.Equal(0, keys[304 / 8] & (1 << (304 % 8)));
            }
            finally { _ = EvdevInterop.close(descriptor); }
        }
        finally { device.Dispose(); }
        for (var attempt = 0; attempt < 50 && Directory.Exists(sysPath); attempt++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        Assert.False(Directory.Exists(sysPath));
    }

    /// <summary>An unsigned macOS build must report the missing capability instead of creating a silent Preview sink.</summary>
    [Fact(Skip = "Requires macOS IOKit.", SkipUnless = nameof(RunMacProbe))]
    public void MacUnsignedDeviceReportsMissingEntitlement()
    {
        var exception = Assert.Throws<UnauthorizedAccessException>(() => new NativeGamepadDeviceFactory().Create());

        Assert.Contains("com.apple.developer.hid.virtual.device", exception.Message, StringComparison.Ordinal);
    }
}
