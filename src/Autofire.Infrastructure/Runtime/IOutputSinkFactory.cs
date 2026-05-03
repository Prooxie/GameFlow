namespace Autofire.Infrastructure.Runtime;

public interface IOutputSinkFactory
{
    IOutputSink Create(string? providerId);
}
