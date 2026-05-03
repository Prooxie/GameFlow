namespace Autofire.Infrastructure.Runtime;

public interface IInputSourceFactory
{
    IInputSource Create(string? providerId);
}
