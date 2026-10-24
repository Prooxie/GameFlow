using GameFlow.Infrastructure.Localization;
using GameFlow.Infrastructure.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>
/// Covers hiding GameFlow's own emitted pads from the input device list.
/// </summary>
/// <remarks>
/// The filter answers "did this device appear at or after the sink that
/// owns its vendor/product pair activated?". Getting that wrong publishes
/// a virtual pad as physical hardware, which can then be assigned to
/// another slot as a feedback loop and is battery-polled for a number
/// that means nothing.
/// </remarks>
public sealed class InputDeviceCatalogVirtualFilterTests
{
    private const ushort Xbox360Vid = 0x045E;
    private const ushort Xbox360Pid = 0x028E;

    private static InputDeviceCatalog NewCatalog() =>
        new(new StubLocalization(), new DeviceCategoryOverrideStore(NullLogger<DeviceCategoryOverrideStore>.Instance));

    private static InputDeviceInfo Pad(string id, ushort vid = Xbox360Vid, ushort pid = Xbox360Pid) =>
        new(id, "Xbox 360 Controller", IsConnected: true, IsSelected: false,
            VendorId: vid, ProductId: pid, IsGamepad: true, Category: DeviceCategory.Gamepad);

    /// <summary>A physical Bluetooth pad must remain visible beside a matching virtual output.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplaceDevicesBluetoothDualSenseRemainsVisibleDuringOutputClaim(bool connectedBeforeClaim)
    {
        var catalog = NewCatalog();
        var physical = Pad("physical", 0x054C, 0x0CE6) with
        {
            HardwarePath = @"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0ce6#9&22599cf&9&0000",
        };
        if (connectedBeforeClaim) { catalog.ReplaceDevices("sdl", [physical]); }
        using var claim = catalog.ClaimHardwareSignature(0x054C, 0x0CE6, DateTimeOffset.UtcNow);

        catalog.ReplaceDevices("sdl", [physical, physical with { Id = "virtual", IsVirtual = true }]);

        Assert.Equal(physical, Assert.Single(catalog.Devices));
    }

    /// <summary>Resolved Bluetooth identity must clear an earlier heuristic exclusion.</summary>
    [Fact]
    public void ReplaceDevicesResolvedBluetoothPathRestoresPreviouslyHiddenDevice()
    {
        var catalog = NewCatalog();
        using var claim = catalog.ClaimHardwareSignature(0x054C, 0x0CE6, DateTimeOffset.UtcNow);
        var physical = Pad("physical", 0x054C, 0x0CE6);
        catalog.ReplaceDevices("sdl", [physical]);
        Assert.Empty(catalog.Devices);

        catalog.ReplaceDevices("sdl", [physical with
        {
            HardwarePath = @"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\9&22599CF&9&0000",
        }]);

        Assert.Equal("physical", Assert.Single(catalog.Devices).Id);
    }

    [Fact]
    public void LingeringVirtualEndpointStaysHiddenAfterGraceUntilItDisappears()
    {
        var clock = new ManualClock();
        var catalog = new InputDeviceCatalog(new StubLocalization(),
            new DeviceCategoryOverrideStore(NullLogger<DeviceCategoryOverrideStore>.Instance), clock);
        var claim = catalog.ClaimHardwareSignature(Xbox360Vid, Xbox360Pid, clock.Now);
        catalog.ReplaceDevices("sdl", [Pad("lingering")]);
        claim.Dispose();
        clock.Now += TimeSpan.FromMinutes(1);
        catalog.ReplaceDevices("sdl", [Pad("lingering")]);
        Assert.Empty(catalog.Devices);
        catalog.ReplaceDevices("sdl", []);
        catalog.ReplaceDevices("sdl", [Pad("lingering")]);
        Assert.Single(catalog.Devices);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void APadThatAppearsAfterItsSinkActivatedIsHidden()
    {
        var catalog = NewCatalog();
        catalog.SetIgnoredHardwareSignatures(
            [(Xbox360Vid, Xbox360Pid, DateTimeOffset.UtcNow.AddSeconds(-1))]);

        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-virtual")]);

        Assert.Empty(catalog.Devices);
    }

    /// <summary>
    /// A real pad of the same model that was already plugged in stays
    /// selectable. This is the reason the filter is time-based at all.
    /// </summary>
    [Fact]
    public void APadThatWasAlreadyPresentBeforeActivationStaysVisible()
    {
        var catalog = NewCatalog();
        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-real")]);

        catalog.SetIgnoredHardwareSignatures(
            [(Xbox360Vid, Xbox360Pid, DateTimeOffset.UtcNow.AddSeconds(30))]);
        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-real")]);

        Assert.Single(catalog.Devices);
    }

    /// <summary>
    /// The regression this was written for.
    /// </summary>
    /// <remarks>
    /// A device id is a hash of vendor, product and name, so a virtual pad
    /// that is removed and re-created hashes to exactly the same id. The
    /// catalog remembered when it first saw that id and never forgot, so
    /// the re-created pad inherited a timestamp from before this run's
    /// sink existed — and the filter, asking whether it appeared after
    /// activation, always answered no. One crashed run left an orphan
    /// behind, the orphan seeded the stale timestamp, and every launch
    /// afterwards published the virtual pad as physical hardware even
    /// once the orphan itself had been swept away.
    /// </remarks>
    [Fact]
    public void APadRecreatedUnderTheSameIdIsJudgedOnItsNewArrival()
    {
        var catalog = NewCatalog();

        // A leftover from a previous run, present before anything activates.
        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-virtual")]);
        Assert.Single(catalog.Devices);

        // Swept away.
        catalog.ReplaceDevices("sdl", []);
        Assert.Empty(catalog.Devices);

        // This run's sink activates and creates its own pad, which hashes
        // to the same id as the one that just left.
        catalog.SetIgnoredHardwareSignatures(
            [(Xbox360Vid, Xbox360Pid, DateTimeOffset.UtcNow)]);
        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-virtual")]);

        Assert.Empty(catalog.Devices);
    }

    [Fact]
    public void ADifferentModelIsNeverHidden()
    {
        var catalog = NewCatalog();
        catalog.SetIgnoredHardwareSignatures(
            [(Xbox360Vid, Xbox360Pid, DateTimeOffset.UtcNow.AddSeconds(-1))]);

        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-dualsense", 0x054C, 0x0CE6)]);

        Assert.Single(catalog.Devices);
    }

    [Fact]
    public void ADeviceWhosePathMarksItVirtualIsNeverListed()
    {
        // The path verdict is known on the very first enumeration, before
        // any sink has said what it owns; nothing else should be needed.
        var catalog = NewCatalog();

        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-virtual") with { IsVirtual = true }]);

        Assert.Empty(catalog.Devices);
    }

    [Fact]
    public void AClaimHidesAPadThatEnumeratesWhileItIsBeingCreated()
    {
        var catalog = NewCatalog();
        using var claim = catalog.ClaimHardwareSignature(Xbox360Vid, Xbox360Pid, DateTimeOffset.UtcNow);

        // The SDL worker lists the new pad before the coordinator publishes
        // anything; the claim alone must keep it out.
        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-new")]);

        Assert.Empty(catalog.Devices);
    }

    [Fact]
    public void AClaimTakesEffectOnAPadAlreadyListed()
    {
        var catalog = NewCatalog();
        var updates = 0;
        catalog.Updated += (_, _) => updates++;
        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-new")]);
        updates = 0;

        // Claimed a moment BEFORE the pad was first seen, as the sink does.
        using var claim = catalog.ClaimHardwareSignature(Xbox360Vid, Xbox360Pid, DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.Empty(catalog.Devices);
        Assert.Equal(1, updates);
    }

    [Fact]
    public void AReleasedClaimKeepsHidingDuringTheGrace()
    {
        var catalog = NewCatalog();
        var claim = catalog.ClaimHardwareSignature(Xbox360Vid, Xbox360Pid, DateTimeOffset.UtcNow);
        claim.Dispose();

        // A torn-down pad can still enumerate for a moment.
        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-leaving")]);

        Assert.Empty(catalog.Devices);
    }

    [Fact]
    public void AClaimDoesNotHideARealPadOfTheSameModelThatWasAlreadyConnected()
    {
        var catalog = NewCatalog();
        catalog.ReplaceDevices("sdl", [Pad("sdl-gamepad-real")]);

        using var claim = catalog.ClaimHardwareSignature(Xbox360Vid, Xbox360Pid, DateTimeOffset.UtcNow.AddSeconds(10));

        Assert.Single(catalog.Devices);
    }

    private sealed class StubLocalization : ILocalizationService
    {
        public event EventHandler? CultureChanged { add { } remove { } }

        public IReadOnlyList<LanguageOption> SupportedLanguages => [];

        public string CurrentCulture => "en";

        public string this[string key] => key;

        public string Translate(string key) => key;

        public void SetCulture(string cultureCode) { }
    }
}
