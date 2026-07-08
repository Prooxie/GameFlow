using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Configuration;

/// <summary>
/// Tiny abstraction over a runtime-mutable logging minimum level.
///
/// <para>
/// The concrete implementation typically wraps the underlying logging
/// framework's switch (e.g. Serilog's <c>LoggingLevelSwitch</c>). Keeping the
/// interface here means <see cref="UserSettingsService"/> — and any other
/// piece of the Infrastructure layer that wants to react to log-level
/// changes — does not need a direct package reference to the chosen logging
/// backend.
/// </para>
/// </summary>
public interface ILogLevelSwitch
{
    /// <summary>
    /// Sets the minimum level emitted by every sink wired to this switch.
    /// Implementations must be thread-safe and synchronous (the change
    /// must be observable on the next log call from any thread).
    /// </summary>
    /// <param name="level">The new minimum level.</param>
    void Set(LogLevel level);
}
