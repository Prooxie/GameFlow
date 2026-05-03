using Autofire.Core.Models;

namespace Autofire.Infrastructure.Runtime;

public interface IOutputSink : IAsyncDisposable
{
    string DisplayName { get; }

    ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken);
}
