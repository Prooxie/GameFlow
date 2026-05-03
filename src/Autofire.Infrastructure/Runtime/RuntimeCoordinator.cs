using Autofire.Core.Models;
using Autofire.Core.Pipeline;
using Autofire.Infrastructure.Profiles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime;

public sealed class RuntimeCoordinator(
    IInputSourceFactory inputSourceFactory,
    IOutputSinkFactory outputSinkFactory,
    RuntimeSnapshotStore snapshotStore,
    ProfileSession profileSession,
    InputDeviceCatalog inputDeviceCatalog,
    ILogger<RuntimeCoordinator> logger) : BackgroundService
{
    private readonly IInputSourceFactory inputSourceFactory = inputSourceFactory;
    private readonly IOutputSinkFactory outputSinkFactory = outputSinkFactory;
    private readonly RuntimeSnapshotStore snapshotStore = snapshotStore;
    private readonly ProfileSession profileSession = profileSession;
    private readonly InputDeviceCatalog inputDeviceCatalog = inputDeviceCatalog;
    private readonly ILogger<RuntimeCoordinator> logger = logger;
    private readonly SemaphoreSlim providerGate = new(1, 1);

    private IInputSource? currentInputSource;
    private IOutputSink? currentOutputSink;
    private int disposeStarted;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var highResolutionTimerLease = new WindowsHighResolutionTimerLease(logger);

        try
        {
            await profileSession.EnsureInitializedAsync(stoppingToken);

            var activeProfile = profileSession.CurrentProfile;
            var pipeline = new ControllerMappingPipeline(activeProfile);
            var interval = GetPollingInterval(activeProfile.PollingRateHz);
            var nextTickAt = DateTimeOffset.UtcNow;

            await ActivateProvidersAsync(activeProfile, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!ReferenceEquals(activeProfile, profileSession.CurrentProfile))
                {
                    var previousProfile = activeProfile;
                    activeProfile = profileSession.CurrentProfile;
                    pipeline = new ControllerMappingPipeline(activeProfile);
                    interval = GetPollingInterval(activeProfile.PollingRateHz);
                    nextTickAt = DateTimeOffset.UtcNow;

                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("Reloaded pipeline for profile {ProfileId}.", activeProfile.Id);
                    }

                    if (!string.Equals(previousProfile.InputProvider, activeProfile.InputProvider, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(previousProfile.OutputProvider, activeProfile.OutputProvider, StringComparison.OrdinalIgnoreCase))
                    {
                        await ActivateProvidersAsync(activeProfile, stoppingToken);
                    }
                }

                interval = GetPollingInterval(activeProfile.PollingRateHz);

                var inputSource = currentInputSource ?? throw new InvalidOperationException("Input source is not initialized.");
                var outputSink = currentOutputSink ?? throw new InvalidOperationException("Output sink is not initialized.");
                var now = DateTimeOffset.UtcNow;
                var physical = await inputSource.ReadAsync(stoppingToken);
                var result = pipeline.Process(physical, now);

                await outputSink.WriteAsync(result.VirtualSnapshot, stoppingToken);
                snapshotStore.Update(inputSource.DisplayName, outputSink.DisplayName, result);

                nextTickAt += interval;
                var delay = nextTickAt - DateTimeOffset.UtcNow;
                if (delay <= TimeSpan.Zero)
                {
                    if (-delay > interval)
                    {
                        nextTickAt = DateTimeOffset.UtcNow;
                    }

                    continue;
                }

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Runtime coordinator cancellation requested.");
        }
        catch (ObjectDisposedException exception)
        {
            logger.LogDebug(exception, "Runtime coordinator observed provider disposal during shutdown.");
        }
        finally
        {
            await DisposeProvidersAsync();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping runtime coordinator.");

        try
        {
            await base.StopAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Runtime coordinator stop timed out or was cancelled.");
        }
        finally
        {
            await DisposeProvidersAsync();
        }
    }

    private async Task ActivateProvidersAsync(ProfileDocument profile, CancellationToken cancellationToken)
    {
        await DisposeProvidersAsync();
        cancellationToken.ThrowIfCancellationRequested();

        await providerGate.WaitAsync(cancellationToken);
        try
        {
            // Issue #9: Removed the virtual-source-device filter. Previously, any device
            // that appeared after the output sink was created (i.e. the ViGEm virtual
            // controller) was automatically hidden from the source selection list.
            // Users can now deliberately select the virtual device as an input source
            // if they want to chain pipelines. The selection is purely opt-in and does
            // not create any automatic feedback loop unless the user explicitly picks it.
            inputDeviceCatalog.SetIgnoredDeviceIds([]);
            currentInputSource = inputSourceFactory.Create(profile.InputProvider);

            _ = await currentInputSource.ReadAsync(cancellationToken);

            currentOutputSink = outputSinkFactory.Create(profile.OutputProvider);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Runtime activated input provider {InputProvider} and output provider {OutputProvider} for profile {ProfileId}.",
                    currentInputSource.DisplayName,
                    currentOutputSink.DisplayName,
                    profile.Id);
            }
        }
        finally
        {
            _ = providerGate.Release();
        }
    }

    private async Task DisposeProvidersAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) == 1)
        {
            return;
        }

        try
        {
            await providerGate.WaitAsync();
            try
            {
                var inputSource = Interlocked.Exchange(ref currentInputSource, null);
                if (inputSource is not null)
                {
                    try
                    {
                        await inputSource.DisposeAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogDebug("Input source disposal was cancelled.");
                    }
                    catch (ObjectDisposedException)
                    {
                        logger.LogDebug("Input source was already disposed.");
                    }
                    catch (Exception exception)
                    {
                        logger.LogDebug(exception, "Input source disposal reported an error during shutdown.");
                    }
                }

                var outputSink = Interlocked.Exchange(ref currentOutputSink, null);
                if (outputSink is not null)
                {
                    try
                    {
                        await outputSink.DisposeAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogDebug("Output sink disposal was cancelled.");
                    }
                    catch (ObjectDisposedException)
                    {
                        logger.LogDebug("Output sink was already disposed.");
                    }
                    catch (Exception exception)
                    {
                        logger.LogDebug(exception, "Output sink disposal reported an error during shutdown.");
                    }
                }
            }
            finally
            {
                _ = providerGate.Release();
            }
        }
        finally
        {
            inputDeviceCatalog.SetIgnoredDeviceIds([]);
            _ = Interlocked.Exchange(ref disposeStarted, 0);
        }
    }

    private static TimeSpan GetPollingInterval(int pollingRateHz)
    {
        var normalizedRate = Math.Clamp(pollingRateHz, 30, 1000);
        return TimeSpan.FromMilliseconds(1000d / normalizedRate);
    }
}
