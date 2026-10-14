using GameFlow.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Tests.Configuration;

/// <summary>An <see cref="ILogLevelSwitch"/> that does nothing, for tests that do not check logging.</summary>
internal sealed class NoLogLevelSwitch : ILogLevelSwitch
{
    public void Set(LogLevel level)
    {
    }
}
