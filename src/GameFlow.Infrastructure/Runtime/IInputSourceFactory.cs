namespace GameFlow.Infrastructure.Runtime;

public interface IInputSourceFactory
{
    IInputSource Create(string? providerId);
}
