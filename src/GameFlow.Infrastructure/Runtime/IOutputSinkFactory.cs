namespace GameFlow.Infrastructure.Runtime;

public interface IOutputSinkFactory
{
    IOutputSink Create(string? providerId);
}
