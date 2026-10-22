namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// Keeps feedback teardown ordered: consumers first observe an explicit
/// motor stop, then the virtual controller that could have produced that
/// stop is detached. Each subscriber is attempted even if another throws.
/// This matters when a cached output sink reconfigures
/// itself without SlotRuntime replacing the sink object.
/// </summary>
internal static class HidMaestroRumbleLifecycle
{
    public static void StopAndTeardown(
        Action<double, double>? rumbleReceived,
        Action teardown,
        Action<Exception>? onFeedbackFailure = null)
    {
        try
        {
            foreach (Action<double, double> handler in rumbleReceived?.GetInvocationList() ?? [])
            {
                try
                {
                    handler(0d, 0d);
                }
                catch (Exception exception)
                {
                    onFeedbackFailure?.Invoke(exception);
                }
            }
        }
        finally
        {
            teardown();
        }
    }
}
