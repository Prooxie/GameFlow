using GameFlow.Core.Models;
using GameFlow.Infrastructure.Localization;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Native;

/// <summary>Owns a standard native gamepad. Device failures remain visible and never select Preview silently.</summary>
public sealed class NativeGamepadOutputSink(
    INativeGamepadDeviceFactory factory,
    ILogger<NativeGamepadOutputSink> logger,
    ILocalizationService? localization = null) : IOutputSink
{
    private readonly byte[] previousReport = new byte[NativeGamepadReport.Length];
    private bool hasPreviousReport;
    private INativeGamepadDevice? device;
    private bool disposed;
    private string failure = string.Empty;
    private DateTimeOffset retryAfter;

    /// <inheritdoc />
    public string DisplayName => device is not null
        ? Translate("NativeOutputReady")
        : failure.Length == 0 ? Translate("NativeOutputPending") : "native-unavailable: " + Translate("NativeOutputUnavailable");

    /// <summary>Resolves status text when the host supplies localization.</summary>
    private string Translate(string key) => localization?[key] ?? key;

    /// <summary>Native output uses product provenance at discovery. It never hides devices through a vendor/product claim.</summary>
    public (ushort Vid, ushort Pid)? OwnedHardwareSignature => null;

    /// <inheritdoc />
    public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (device is null && DateTimeOffset.UtcNow < retryAfter)
        {
            return ValueTask.CompletedTask;
        }
        try
        {
            if (device is null)
            {
                device = factory.Create();
                failure = string.Empty;
                logger.LogInformation("Native virtual gamepad created.");
            }
            Span<byte> report = stackalloc byte[NativeGamepadReport.Length];
            NativeGamepadReport.Encode(snapshot, report);
            if (!hasPreviousReport || !report.SequenceEqual(previousReport))
            {
                device.Write(snapshot);
                report.CopyTo(previousReport);
                hasPreviousReport = true;
            }
            return ValueTask.CompletedTask;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            failure = exception.Message;
            retryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
            ReleaseDevice();
            logger.LogWarning(exception, "Native gamepad output failed. Check the platform output requirements.");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Releases the device even when a final neutral report fails.</summary>
    private void ReleaseDevice()
    {
        var previous = device;
        device = null;
        hasPreviousReport = false;
        if (previous is null) { return; }
        try { previous.Write(ControllerSnapshot.Empty()); }
        catch (IOException exception) { logger.LogDebug(exception, "Native gamepad rejected its final neutral report."); }
        finally { previous.Dispose(); }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            ReleaseDevice();
        }
        return ValueTask.CompletedTask;
    }
}
