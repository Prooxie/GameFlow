using HIDMaestro;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.HidMaestro.Tests;

public sealed class HidMaestroRumbleRouterTests
{
    private static readonly byte[] XInputFiveBytes = [0x00, 0x08, 0xFF, 0x80, 0x00];

    private static Dictionary<string, object> DualSenseRumbleFields() => new()
    {
        ["validFlag0"] = (byte)0x03,
        ["validFlag1"] = (byte)0,
        ["validFlag2"] = (byte)0,
        ["leftMotor"] = (byte)192,
        ["rightMotor"] = (byte)64,
    };

    private static (HidMaestroRumbleRouter Router, List<(double Low, double High)> Raised) Create()
    {
        var router = new HidMaestroRumbleRouter(NullLogger.Instance);
        var raised = new List<(double, double)>();
        router.RumbleReceived += (low, high) => raised.Add((low, high));
        return (router, raised);
    }

    [WindowsFact]
    public void XInputFiveBytePacket_RaisesBothMotors()
    {
        var (router, raised) = Create();

        router.OnOutputReceived(null!, new HMOutputPacket(HMOutputSource.XInput, 0, XInputFiveBytes, 1));

        var rumble = Assert.Single(raised);
        Assert.Equal(1.0, rumble.Low);
        Assert.Equal(128 / 255d, rumble.High);
    }

    [WindowsFact]
    public void HidOutputPacket_IsIgnored()
    {
        var (router, raised) = Create();

        router.OnOutputReceived(null!, new HMOutputPacket(HMOutputSource.HidOutput, 0, XInputFiveBytes, 1));

        Assert.Empty(raised);
    }

    [WindowsFact]
    public void UnrecognisedXInputLength_RaisesNothing()
    {
        var (router, raised) = Create();

        router.OnOutputReceived(null!, new HMOutputPacket(HMOutputSource.XInput, 0, new byte[] { 0x01, 0x03, 0x02 }, 1));

        Assert.Empty(raised);
    }

    [WindowsFact]
    public void DecodedReportWithBadCrc_IsIgnored()
    {
        var (router, raised) = Create();

        router.OnOutputDecoded(null, new HMOutputDecodedEventArgs { Fields = DualSenseRumbleFields(), CrcValid = false });

        Assert.Empty(raised);
    }

    [WindowsFact]
    public void DecodedSonyReport_Raises()
    {
        var (router, raised) = Create();

        router.OnOutputDecoded(null, new HMOutputDecodedEventArgs { Fields = DualSenseRumbleFields(), CrcValid = true });

        var rumble = Assert.Single(raised);
        Assert.InRange(rumble.Low, 0.752, 0.754);
        Assert.InRange(rumble.High, 0.250, 0.252);
    }

    [WindowsFact]
    public void DecodedReportWithoutFields_IsIgnored()
    {
        var (router, raised) = Create();

        router.OnOutputDecoded(null, new HMOutputDecodedEventArgs { Fields = null!, CrcValid = true });

        Assert.Empty(raised);
    }

    [WindowsFact]
    public void ThrowingSubscriber_DoesNotEscapeTheHandler()
    {
        var router = new HidMaestroRumbleRouter(NullLogger.Instance);
        router.RumbleReceived += (_, _) => throw new InvalidOperationException("boom");

        var packet = new HMOutputPacket(HMOutputSource.XInput, 0, XInputFiveBytes, 1);
        router.OnOutputReceived(null!, packet);
        router.OnOutputReceived(null!, packet);
    }
}
