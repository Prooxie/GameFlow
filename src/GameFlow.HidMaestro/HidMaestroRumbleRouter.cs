using System.Runtime.Versioning;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using HIDMaestro;
using Microsoft.Extensions.Logging;

namespace GameFlow.HidMaestro;

/// <summary>
/// Turns the SDK's typed output events into GameFlow's normalized rumble values.
/// OutputReceived covers XInput. OutputDecoded covers the profile-authored Sony
/// and Switch motor fields.
/// </summary>
/// <remarks>
/// The handlers run on the SDK's event thread, so they never throw. The first
/// failure is logged once, and later packets are still attempted. Losing
/// rumble must not harm the virtual input device.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class HidMaestroRumbleRouter(ILogger logger)
{
    private const int MaxDeclinedReportsLogged = 6;

    private readonly HashSet<HMOutputSource> firstOutputLogged = [];
    private readonly HashSet<int> unrecognisedXInputShapes = [];
    private bool decodedRumbleLogged;
    private bool xinputRumbleLogged;
    private int declinedReportsLogged;
    private int outputWarningLogged;

    /// <summary>Raised with the low and high motor values, each from 0 to 1.</summary>
    public event Action<double, double>? RumbleReceived;

    /// <summary>Handles one raw packet. Only XInput packets carry rumble.</summary>
    public void OnOutputReceived(HMController sender, HMOutputPacket packet)
    {
        try
        {
            var data = packet.Data;

            // One line per source, with the payload. A packet whose length the
            // decoder does not recognise is otherwise indistinguishable from no
            // packet arriving at all, and the two have different causes.
            if (firstOutputLogged.Add(packet.Source))
            {
                logger.LogInformation(
                    "HIDMaestro: first output packet from the host — source={Source} ({SourceName}), " +
                    "reportId={ReportId}, {Length} byte(s): {Payload}",
                    (int)packet.Source,
                    packet.Source.ToString(),
                    packet.ReportId,
                    data.Length,
                    Convert.ToHexString(data.Span));
            }

            if (packet.Source != HMOutputSource.XInput)
            {
                return;
            }

            if (HidMaestroRumbleDecoder.TryDecodeXInput(data.Span, out var low, out var high))
            {
                if (!xinputRumbleLogged)
                {
                    xinputRumbleLogged = true;
                    logger.LogInformation(
                        "HIDMaestro: first XInput rumble decoded — low={Low:F2} high={High:F2}.",
                        low, high);
                }

                RumbleReceived?.Invoke(low, high);
            }
            else if (unrecognisedXInputShapes.Add(data.Length))
            {
                // Not necessarily a dropped rumble. XUSB carries more than
                // vibration on this channel: an LED/index assignment arrives the
                // moment Windows binds the pad. Logged once per distinct length,
                // so a shape that only appears while a game vibrates stays visible.
                logger.LogInformation(
                    "HIDMaestro: ignored an XInput output packet in a shape the vibration " +
                    "decoder does not know — {Length} byte(s): {Payload}. This is expected for the " +
                    "LED/index packets Windows sends when a pad is bound; it only indicates a problem " +
                    "if it coincides with rumble being requested and not felt.",
                    data.Length, Convert.ToHexString(data.Span));
            }
        }
        catch (Exception exception)
        {
            LogOutputWarningOnce(exception);
        }
    }

    /// <summary>Handles one decoded vendor report. A report with a bad CRC never drives a motor.</summary>
    public void OnOutputDecoded(object? sender, HMOutputDecodedEventArgs e)
    {
        try
        {
            // Never actuate a physical motor from a corrupt Bluetooth frame.
            if (!e.CrcValid)
            {
                return;
            }

            var fields = e.Fields;
            if (fields is null)
            {
                return;
            }

            if (HidMaestroRumbleDecoder.TryDecodeSemanticFields(fields, out var low, out var high))
            {
                if (!decodedRumbleLogged)
                {
                    decodedRumbleLogged = true;
                    logger.LogInformation(
                        "HIDMaestro: first decoded rumble from the host — low={Low:F2} high={High:F2}.",
                        low, high);
                }

                RumbleReceived?.Invoke(low, high);
            }
            else if (declinedReportsLogged < MaxDeclinedReportsLogged)
            {
                // The decoder declined. Log the values, not just the names: the
                // decision turns on the Sony validity flags, so flags and motor
                // bytes together tell a correct rejection (LED-only padding) from
                // a wrong one. Several reports, because the first report a host
                // sends is usually LED or configuration, not rumble.
                declinedReportsLogged++;
                logger.LogInformation(
                    "HIDMaestro: decoded output report {Index}/{Max} carried no rumble the validity " +
                    "flags authorise. Fields: {Fields}",
                    declinedReportsLogged,
                    MaxDeclinedReportsLogged,
                    string.Join(", ", fields.Select(f => $"{f.Key}={Describe(f.Value)}")));
            }
        }
        catch (Exception exception)
        {
            LogOutputWarningOnce(exception);
        }
    }

    /// <summary>Renders a decoded field value compactly for the log.</summary>
    private static string Describe(object? value) => value switch
    {
        null => "null",
        byte b => "0x" + b.ToString("X2"),
        ReadOnlyMemory<byte> m => "0x" + Convert.ToHexString(m.Span),
        byte[] a => "0x" + Convert.ToHexString(a),
        _ => value.ToString() ?? "?",
    };

    private void LogOutputWarningOnce(Exception exception)
    {
        if (Interlocked.Exchange(ref outputWarningLogged, 1) == 0)
        {
            logger.LogWarning(exception,
                "HIDMaestro: an output-feedback packet could not be decoded; later packets will still be attempted.");
        }
    }
}
