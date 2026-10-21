using System.Runtime.Versioning;

namespace GameFlow.HidMaestro;

/// <summary>
/// Installs the HIDMaestro driver once per process and remembers a failure.
/// A failed install requires a restart: retrying a global SDK sweep after other
/// slots have started could destroy their devices.
/// </summary>
/// <remarks>
/// The lock makes concurrent first calls safe: one caller installs, the others
/// wait and then read the same result.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class HidMaestroDriverInitialization(Action installDriver)
{
    private readonly object gate = new();
    private bool attempted;
    private string? installationFailure;

    /// <summary>Runs the install at most once. Returns false, with the kept text, after a failure.</summary>
    public bool TryEnsureInstalled(out string? failure)
    {
        lock (gate)
        {
            if (!attempted)
            {
                attempted = true;
                try
                {
                    installDriver();
                }
                catch (Exception exception)
                {
                    installationFailure = $"HIDMaestro driver installation failed: {exception.Message} Resolve the error and restart GameFlow.";
                }
            }

            failure = installationFailure;
            return failure is null;
        }
    }
}
