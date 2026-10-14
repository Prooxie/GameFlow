namespace GameFlow.App.Bootstrap;

/// <summary>
/// Logs the exceptions that no code catches, so that they reach the log file.
/// </summary>
/// <remarks>
/// <para>
/// <c>Program.Main</c> logs an exception that leaves the UI loop. Two kinds of
/// failure never pass there:
/// </para>
/// <list type="bullet">
/// <item>An exception on a background thread. It ends the process at once.
///   <see cref="OnUnhandledException"/> logs it as Fatal and flushes the log first.</item>
/// <item>A failed task that no code awaits, for example a fire-and-forget save.
///   <see cref="OnUnobservedTaskException"/> logs it as Error and marks it observed.</item>
/// </list>
/// </remarks>
/// <param name="getLogger">
/// Gets the logger at the time of the error. The host replaces the static
/// <c>Serilog.Log.Logger</c> after start-up, so a logger read at install time
/// would be the silent default one.
/// </param>
internal sealed class UnhandledExceptionLogger(Func<Serilog.ILogger> getLogger)
{
    private readonly Func<Serilog.ILogger> getLogger = getLogger;

    /// <summary>Subscribes to the process-wide events. Call once, at start-up.</summary>
    public void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>Logs an exception that ends the process, and flushes the log.</summary>
    internal void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var logger = getLogger();
        logger.Fatal(
            e.ExceptionObject as Exception,
            "Unhandled exception (terminating: {IsTerminating}). Object: {ExceptionObject}",
            e.IsTerminating,
            e.ExceptionObject is Exception ? null : e.ExceptionObject);

        if (e.IsTerminating)
        {
            // The process ends after this handler. Disposing the logger
            // flushes its sinks; without it, the file sink can lose the one
            // line that explains the crash.
            (logger as IDisposable)?.Dispose();
        }
    }

    /// <summary>Logs a failed task that no code awaited, and marks it observed.</summary>
    internal void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        getLogger().Error(e.Exception, "A background task failed and no code observed the failure.");
        e.SetObserved();
    }
}
