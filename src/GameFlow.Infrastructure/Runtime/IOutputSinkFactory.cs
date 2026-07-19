namespace GameFlow.Infrastructure.Runtime;

public interface IOutputSinkFactory
{
    IOutputSink Create(string? providerId);

    /// <summary>
    /// A sink that never materialises an OS device — for pipelines that
    /// must run (snapshot/store plumbing) but must not emit anything.
    /// Exists because <see cref="Create"/> routes through
    /// <see cref="OutputProviderPolicy"/>, which on Windows resolves
    /// EVERY id to the real backend by design.
    /// </summary>
    IOutputSink CreateNoOp();
}
