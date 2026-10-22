using GameFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime;

internal static class OutputSafety
{
    internal static async ValueTask<bool> TryNeutralizeAsync(IOutputSink sink, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await sink.WriteAsync(ControllerSnapshot.Empty("Input unavailable"), cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not release held output controls after an input/output failure.");
            return false;
        }
    }
}
