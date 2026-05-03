using Autofire.Core.Pipeline;

namespace Autofire.Infrastructure.Runtime;

public sealed class RuntimeSnapshotStore
{
    private readonly Lock syncRoot = new();

    public RuntimeSnapshot Current
    {
        get
        {
            lock (syncRoot)
            {
                return field;
            }
        }

        private set;
    } = new();

    public void Update(string inputProvider, string outputProvider, ControllerFrameResult result)
    {
        lock (syncRoot)
        {
            Current = new RuntimeSnapshot
            {
                LastUpdated = DateTimeOffset.UtcNow,
                InputProvider = inputProvider,
                OutputProvider = outputProvider,
                PhysicalSnapshot = result.PhysicalSnapshot,
                VirtualSnapshot = result.VirtualSnapshot,
                Notes = result.Notes
            };
        }
    }
}
