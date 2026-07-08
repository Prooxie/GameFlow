using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime;

public interface IInputSource : IAsyncDisposable
{
    string DisplayName { get; }

    ValueTask<ControllerSnapshot> ReadAsync(CancellationToken cancellationToken);
}
