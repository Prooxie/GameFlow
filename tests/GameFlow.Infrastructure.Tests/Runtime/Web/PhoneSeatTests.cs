using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.Web;
using GameFlow.Infrastructure.Runtime.Web.Overlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

/// <summary>Phones as registered devices: seats, names, links, removal.</summary>
public sealed class PhoneSeatTests : IDisposable
{
    private readonly string phonesFile = Path.Combine(Path.GetTempPath(), $"gameflow-phones-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(phonesFile);

    private PhoneRegistry NewRegistry(WebControllerHub hub) =>
        new(hub, NullLogger<PhoneRegistry>.Instance, phonesFile);

    [Fact]
    public void ClaimSeat_TakesThatSeatEvenFromAnotherBrowser()
    {
        var hub = new WebControllerHub();
        var first = hub.ClaimSeat(3, "browser-one-id");

        var second = hub.ClaimSeat(3, "browser-two-id");

        Assert.Equal(3, second.PadIndex);
        Assert.False(hub.IsLeaseCurrent(first));
        Assert.True(hub.IsLeaseCurrent(second));
    }

    [Fact]
    public void ClaimPad_SkipsSeatsReservedForRegisteredPhones()
    {
        var hub = new WebControllerHub();

        var lease = hub.ClaimPad("anonymous-id", reservedSeats: new HashSet<int> { 0, 1 });

        Assert.Equal(2, lease.PadIndex);
    }

    [Fact]
    public void Revoke_EndsTheConnectionAndForgetsTheClient()
    {
        var hub = new WebControllerHub();
        var lease = hub.ClaimSeat(0, "phone-client-id");

        hub.Revoke(0);

        Assert.False(hub.IsLeaseCurrent(lease));
        Assert.False(hub.IsPadConnected(0));
        Assert.False(hub.UpdatePad(lease, new ControllerSnapshot()));
    }

    [Fact]
    public void UpdatePad_RecordsTheReportedLayout()
    {
        var hub = new WebControllerHub();
        var lease = hub.ClaimSeat(1);

        hub.UpdatePad(lease, new ControllerSnapshot(), WebPadLayout.Nintendo);

        Assert.Equal(WebPadLayout.Nintendo, hub.GetLayout(1));
        Assert.Equal(ControllerVisualStyle.NintendoSwitch, hub.GetLayout(1).ToVisualStyle());
    }

    [Fact]
    public void Registry_AddsOnTheLowestFreeSeatAndPersists()
    {
        var hub = new WebControllerHub();
        var registry = NewRegistry(hub);

        var a = registry.Add();
        var b = registry.Add("Couch phone");

        Assert.Equal(0, a!.Seat);
        Assert.Equal("Phone 1", a.Name);
        Assert.Equal(1, b!.Seat);

        var reloaded = NewRegistry(hub);
        Assert.Equal(["Phone 1", "Couch phone"], reloaded.Phones.Select(p => p.Name));
    }

    [Fact]
    public void Registry_HoldsAtMostSixteenPhones()
    {
        var registry = NewRegistry(new WebControllerHub());
        for (var i = 0; i < WebControllerHub.MaxPads; i++)
        {
            Assert.NotNull(registry.Add());
        }

        Assert.Null(registry.Add());
        Assert.False(registry.CanAdd);
    }

    [Fact]
    public void Registry_RemoveRevokesTheSeat()
    {
        var hub = new WebControllerHub();
        var registry = NewRegistry(hub);
        var phone = registry.Add()!;
        var lease = hub.ClaimSeat(phone.Seat);

        registry.Remove(phone.Seat);

        Assert.False(registry.IsRegistered(phone.Seat));
        Assert.False(hub.IsLeaseCurrent(lease));
    }

    [Fact]
    public void Scanner_ListsRegisteredPhonesEvenWhileOffline()
    {
        var hub = new WebControllerHub();
        var registry = NewRegistry(hub);
        registry.Add("Kitchen phone");

        var devices = WebControllerDeviceScanner.Scan(hub, registry);

        var phone = Assert.Single(devices);
        Assert.Equal("Kitchen phone", phone.DisplayName);
        Assert.False(phone.IsConnected);
        Assert.True(WebControllerDeviceScanner.IsPhone(phone.Id));
    }

    [Fact]
    public void Protocol_ParsesLayoutAndTheCaptureBit()
    {
        var snapshot = WebControllerProtocol.TryParseInput("{\"b\":65536,\"layout\":\"switch\"}", 0, out var layout);

        Assert.NotNull(snapshot);
        Assert.Equal(WebPadLayout.Nintendo, layout);
        Assert.True(snapshot.Buttons[ButtonId.Misc1]);
    }

    [Fact]
    public void Protocol_UnknownLayoutIsIgnored()
    {
        _ = WebControllerProtocol.TryParseInput("{\"b\":0,\"layout\":\"<script>\"}", 0, out var layout);

        Assert.Null(layout);
    }

    [Theory]
    [InlineData(null, "abc", false, null, "http://localhost:8080/overlay?slot=abc")]
    [InlineData(null, "abc", true, "#00B140", "http://localhost:8080/overlay?slot=abc&side=physical&bg=00B140")]
    [InlineData("pad-1", null, true, "", "http://localhost:8080/overlay?device=pad-1")]
    [InlineData(null, "abc", false, "Transparent", "http://localhost:8080/overlay?slot=abc")]
    public void OverlayUrl_NamesTheSurfaceAndCarriesOnlyARealColour(
        string? deviceId, string? slotId, bool physical, string? background, string expected)
    {
        var url = OverlayUrl.Build("http://localhost:8080/", slotId, deviceId, physical, background);

        Assert.Equal(expected, url);
    }

    [Fact]
    public void PanelAppearance_StoresPerSurfaceAndForgetsADeletedPanel()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gameflow-panels-{Guid.NewGuid():N}.json");
        try
        {
            var store = new PanelAppearanceStore(NullLogger<PanelAppearanceStore>.Instance, path);
            store.SetBackground(PanelAppearanceStore.VirtualKey("slot-a"), "#00B140");
            store.SetBackground(PanelAppearanceStore.PhysicalKey("slot-a"), "");

            var reloaded = new PanelAppearanceStore(NullLogger<PanelAppearanceStore>.Instance, path);
            Assert.Equal("#00B140", reloaded.GetBackground(PanelAppearanceStore.VirtualKey("slot-a")));
            Assert.Equal("", reloaded.GetBackground(PanelAppearanceStore.PhysicalKey("slot-a")));
            Assert.Null(reloaded.GetBackground(PanelAppearanceStore.VirtualKey("slot-b")));

            reloaded.RemovePanel("slot-a");
            Assert.Null(reloaded.GetBackground(PanelAppearanceStore.VirtualKey("slot-a")));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
