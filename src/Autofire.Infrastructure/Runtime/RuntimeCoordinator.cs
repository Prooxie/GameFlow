using Autofire.Core.Models;
using Autofire.Core.Pipeline;
using Autofire.Infrastructure.Profiles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime;

/// <summary>
/// The hosted background service that owns the controller mapping loop.
///
/// <para>
/// Activates the input source and output sink configured by the active
/// profile, then ticks the mapping pipeline at the profile's polling rate
/// (clamped to 30–1000 Hz). Reacts to profile changes mid-loop without a
/// host restart, swapping providers when the profile's input/output
/// selection has actually changed.
/// </para>
///
/// <para>
/// Reliability contract:
/// <list type="bullet">
///   <item>
///     <description>
///       Per-tick exceptions are caught, logged, and the loop continues.
///       The first failure after a healthy stretch is logged at Warning
///       with the full stack; subsequent consecutive failures collapse to
///       Debug to keep the log readable. A successful tick after failures
///       emits an Information line so operators know recovery happened.
///     </description>
///   </item>
///   <item>
///     <description>
///       Anything other than cancellation or provider disposal that
///       escapes the loop is logged at Critical and rethrown — the
///       background service host will see it and the user will see
///       diagnostics instead of silence.
///     </description>
///   </item>
/// </list>
/// </para>
/// </summary>
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

            logger.LogInformation(
                "Runtime loop starting at {Hz} Hz (tick interval {IntervalMs:F2} ms) for profile {ProfileId}.",
                Math.Clamp(activeProfile.PollingRateHz, 30, 1000),
                interval.TotalMilliseconds,
                activeProfile.Id);

            // Counter for transient per-tick failures. We log every failure
            // at Warning, but throttle the secondary "still failing" line so
            // a stuck pad can't flood the file sink.
            var consecutiveTickFailures = 0;

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
                        logger.LogInformation(
                            "Reloaded pipeline for profile {ProfileId} at {Hz} Hz.",
                            activeProfile.Id,
                            Math.Clamp(activeProfile.PollingRateHz, 30, 1000));
                    }

                    if (!string.Equals(previousProfile.InputProvider, activeProfile.InputProvider, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(previousProfile.OutputProvider, activeProfile.OutputProvider, StringComparison.OrdinalIgnoreCase))
                    {
                        await ActivateProvidersAsync(activeProfile, stoppingToken);
                    }
                }

                interval = GetPollingInterval(activeProfile.PollingRateHz);

                try
                {
                    var inputSource = currentInputSource ?? throw new InvalidOperationException("Input source is not initialized.");
                    var outputSink = currentOutputSink ?? throw new InvalidOperationException("Output sink is not initialized.");
                    var now = DateTimeOffset.UtcNow;
                    var physical = await inputSource.ReadAsync(stoppingToken);
                    var result = pipeline.Process(physical, now);

                    await outputSink.WriteAsync(result.VirtualSnapshot, stoppingToken);
                    snapshotStore.Update(inputSource.DisplayName, outputSink.DisplayName, result);

                    if (consecutiveTickFailures > 0)
                    {
                        logger.LogInformation(
                            "Runtime tick recovered after {FailureCount} consecutive failure(s).",
                            consecutiveTickFailures);
                        consecutiveTickFailures = 0;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Outer catch will handle the cancellation message.
                    throw;
                }
                catch (ObjectDisposedException)
                {
                    // Provider was torn down out from under us (e.g. profile
                    // switch in flight). Outer catch logs at Debug.
                    throw;
                }
                catch (Exception exception)
                {
                    consecutiveTickFailures++;

                    // First failure after a stretch of healthy ticks gets a
                    // full Warning with stack; repeated failures collapse to
                    // a Debug line so the log stays readable.
                    if (consecutiveTickFailures == 1)
                    {
                        logger.LogWarning(
                            exception,
                            "Runtime tick failed (input={InputProvider}, output={OutputProvider}). " +
                            "Continuing with empty frame; will log recovery once ticks succeed again.",
                            currentInputSource?.DisplayName ?? "(none)",
                            currentOutputSink?.DisplayName ?? "(none)");
                    }
                    else if (consecutiveTickFailures % 100 == 0)
                    {
                        logger.LogWarning(
                            "Runtime tick still failing after {FailureCount} consecutive attempts. " +
                            "Last error: {ErrorType}: {ErrorMessage}.",
                            consecutiveTickFailures,
                            exception.GetType().Name,
                            exception.Message);
                    }
                    else
                    {
                        logger.LogDebug(exception, "Runtime tick failure #{FailureCount}.", consecutiveTickFailures);
                    }
                }

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
        catch (Exception exception)
        {
            // Anything that isn't cancellation or disposal escaping the loop
            // means the runtime has died unrecoverably. Log loudly so it
            // shows up in support bundles.
            logger.LogCritical(
                exception,
                "Runtime coordinator exited unexpectedly. The mapping pipeline is no longer running.");
            throw;
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

            // Hide the runtime's own virtual output device from the input source
            // dropdown (e.g. when the ViGEm DualShock 4 sink is active, the
            // virtual DS4 it creates would otherwise show up as a selectable
            // SDL3 input device — confusing and almost never useful). Sinks
            // that don't materialise an OS-visible device return null here and
            // contribute nothing to the filter.
            var ownedSignature = currentOutputSink.OwnedHardwareSignature;
            inputDeviceCatalog.SetIgnoredHardwareSignatures(
                ownedSignature is null ? [] : [ownedSignature.Value]);

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
            inputDeviceCatalog.SetIgnoredHardwareSignatures([]);
            _ = Interlocked.Exchange(ref disposeStarted, 0);
        }
    }

    private static TimeSpan GetPollingInterval(int pollingRateHz)
    {
        var normalizedRate = Math.Clamp(pollingRateHz, 30, 1000);
        return TimeSpan.FromMilliseconds(1000d / normalizedRate);
    }
}
