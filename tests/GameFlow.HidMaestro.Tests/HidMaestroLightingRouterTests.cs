using GameFlow.Infrastructure.Runtime.Effects;
using HIDMaestro;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.HidMaestro.Tests;

/// <summary>
/// The router used to drop light-only reports as "declined". It now raises
/// them, independently of rumble, so a game's own lightbar can reach the
/// physical pad.
/// </summary>
public sealed class HidMaestroLightingRouterTests
{
    private static Dictionary<string, object> DualSense(byte flag0, byte flag1) => new()
    {
        ["validFlag0"] = flag0,
        ["validFlag1"] = flag1,
        ["validFlag2"] = (byte)0,
        ["leftMotor"] = (byte)128,
        ["rightMotor"] = (byte)0,
        ["lightbar"] = new byte[] { 0xFF, 0x00, 0x00 },
        ["playerIndicator"] = (byte)0x04,
        ["muteLed"] = (byte)0,
        ["ledBrightness"] = (byte)0,
    };

    private static (HidMaestroRumbleRouter Router, List<GameLighting> Lights, List<(double, double)> Rumble) Create()
    {
        var router = new HidMaestroRumbleRouter(NullLogger.Instance);
        var lights = new List<GameLighting>();
        var rumble = new List<(double, double)>();
        router.LightingReceived += lights.Add;
        router.RumbleReceived += (low, high) => rumble.Add((low, high));
        return (router, lights, rumble);
    }

    [WindowsFact]
    public void ALightOnlyReportRaisesLightingAndNoRumble()
    {
        var (router, lights, rumble) = Create();

        router.OnOutputDecoded(null, new HMOutputDecodedEventArgs { Fields = DualSense(0, 0x04), CrcValid = true });

        var lighting = Assert.Single(lights);
        Assert.NotNull(lighting.Color);
        Assert.Empty(rumble);
    }

    [WindowsFact]
    public void AReportCarryingBothRaisesBoth()
    {
        var (router, lights, rumble) = Create();

        router.OnOutputDecoded(null, new HMOutputDecodedEventArgs { Fields = DualSense(0x03, 0x04), CrcValid = true });

        Assert.Single(lights);
        Assert.Single(rumble);
    }

    [WindowsFact]
    public void ABadCrcRaisesNoLighting()
    {
        var (router, lights, _) = Create();

        router.OnOutputDecoded(null, new HMOutputDecodedEventArgs { Fields = DualSense(0, 0x04), CrcValid = false });

        Assert.Empty(lights);
    }

    [WindowsFact]
    public void AThrowingLightingSubscriberDoesNotEscape()
    {
        var router = new HidMaestroRumbleRouter(NullLogger.Instance);
        router.LightingReceived += _ => throw new InvalidOperationException("boom");

        router.OnOutputDecoded(null, new HMOutputDecodedEventArgs { Fields = DualSense(0, 0x04), CrcValid = true });
    }
}
