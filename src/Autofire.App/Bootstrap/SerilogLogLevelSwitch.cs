using Autofire.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Autofire.App.Bootstrap;

/// <summary>
/// Adapter that joins the framework-neutral
/// <see cref="ILogLevelSwitch"/> abstraction to Serilog's
/// <see cref="LoggingLevelSwitch"/>.
///
/// <para>
/// Lives in the App layer because that is where the Serilog package
/// reference lives. <see cref="HostBuilderFactory"/> creates the underlying
/// <see cref="LoggingLevelSwitch"/>, hands it to Serilog via
/// <c>MinimumLevel.ControlledBy(switch)</c>, and registers an instance of
/// this class against <see cref="ILogLevelSwitch"/> so
/// <c>UserSettingsService</c> can drive the level at runtime.
/// </para>
/// </summary>
public sealed class SerilogLogLevelSwitch : ILogLevelSwitch
{
    private readonly LoggingLevelSwitch inner;

    /// <summary>
    /// Wraps the supplied Serilog <paramref name="serilogSwitch"/>.
    /// </summary>
    public SerilogLogLevelSwitch(LoggingLevelSwitch serilogSwitch)
    {
        inner = serilogSwitch ?? throw new ArgumentNullException(nameof(serilogSwitch));
    }

    /// <inheritdoc />
    public void Set(LogLevel level)
    {
        inner.MinimumLevel = ToSerilogLevel(level);
    }

    /// <summary>
    /// Translates a <see cref="LogLevel"/> value (the
    /// Microsoft.Extensions.Logging vocabulary) to its closest
    /// <see cref="LogEventLevel"/> equivalent (the Serilog vocabulary).
    /// </summary>
    private static LogEventLevel ToSerilogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        LogLevel.None => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}
