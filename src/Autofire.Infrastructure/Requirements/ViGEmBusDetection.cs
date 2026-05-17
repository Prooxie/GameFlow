using Microsoft.Extensions.Logging;
using Nefarius.ViGEm.Client;

namespace Autofire.Infrastructure.Requirements;

/// <summary>
/// Detects whether the ViGEm Bus driver is installed on the current
/// machine. Windows-only.
///
/// <para>
/// Uses the same code path the real
/// <see cref="Autofire.Infrastructure.Runtime.ViGEm.ViGEmXbox360OutputSink"/>
/// uses: instantiate <see cref="ViGEmClient"/> and immediately dispose it.
/// If the bus driver is installed and reachable, the constructor
/// succeeds; if the driver is missing, the constructor throws (typically
/// <c>VigemBusNotFoundException</c>, but we don't bind to that type — any
/// throw is treated as "missing").
/// </para>
///
/// <para>
/// Probing this way means a positive result also confirms that the
/// runtime can talk to the bus, not just that some files exist. It is
/// strictly stronger than a registry-key check.
/// </para>
/// </summary>
internal static class ViGEmBusDetection
{
    /// <summary>
    /// Tri-state probe result. <see cref="Unknown"/> is reserved for
    /// non-Windows platforms (where the requirement is inapplicable) and
    /// for cases where the probe couldn't be made (e.g. exceptions other
    /// than the well-known "bus not found" type — usually means an actual
    /// runtime fault, not a clean missing-driver case).
    /// </summary>
    public enum Detection
    {
        /// <summary>The bus is installed and the client connected successfully.</summary>
        Installed,

        /// <summary>The bus is not installed (client constructor threw).</summary>
        Missing,

        /// <summary>Probe was not applicable or could not be performed conclusively.</summary>
        Unknown,
    }

    /// <summary>
    /// Performs the probe. Always returns <see cref="Detection.Unknown"/>
    /// on non-Windows platforms — callers should never even ask in that
    /// case. Logs failures at Debug to aid support.
    /// </summary>
    public static Detection Detect(ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Detection.Unknown;
        }

        try
        {
            // The ViGEmClient constructor opens a handle to the bus
            // device; if the driver isn't installed it throws. Disposing
            // immediately afterwards is the standard pattern (the real
            // sinks hold their client for the lifetime of the sink, but a
            // probe doesn't need to).
            using var client = new ViGEmClient();
            return Detection.Installed;
        }
        catch (Exception exception)
        {
            // We deliberately catch broadly: the exact exception type
            // varies by Nefarius.ViGEm.Client version. Any failure here
            // means the user can't use ViGEm-based outputs, regardless
            // of cause, which is the actionable bit. The exception
            // message is logged so support can distinguish "driver
            // missing" from "driver installed but service stopped".
            logger.LogDebug(
                exception,
                "ViGEm Bus probe threw; treating as missing. Type={ExceptionType}, Message={ExceptionMessage}",
                exception.GetType().FullName,
                exception.Message);
            return Detection.Missing;
        }
    }
}
