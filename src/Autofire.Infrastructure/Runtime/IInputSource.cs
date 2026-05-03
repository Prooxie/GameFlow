using Autofire.Core.Models;

namespace Autofire.Infrastructure.Runtime;

public interface IInputSource : IAsyncDisposable
{
    string DisplayName { get; }

    ValueTask<ControllerSnapshot> ReadAsync(CancellationToken cancellationToken);
}
