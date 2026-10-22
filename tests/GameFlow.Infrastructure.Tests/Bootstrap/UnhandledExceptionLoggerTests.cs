using GameFlow.App.Bootstrap;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Bootstrap;

/// <summary>Tests for <see cref="UnhandledExceptionLogger"/>.</summary>
public sealed class UnhandledExceptionLoggerTests
{
    private readonly CapturingSink sink = new();
    private readonly UnhandledExceptionLogger handler;

    public UnhandledExceptionLoggerTests()
    {
        var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        handler = new UnhandledExceptionLogger(() => logger);
    }

    /// <summary>
    /// A fire-and-forget task that fails is otherwise lost without a trace.
    /// </summary>
    [Fact]
    public void OnUnobservedTaskExceptionFailedTaskLogsErrorAndMarksObserved()
    {
        var failure = new AggregateException(new IOException("save failed"));
        var args = new UnobservedTaskExceptionEventArgs(failure);

        handler.OnUnobservedTaskException(null, args);

        Assert.True(args.Observed);
        var logged = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Error, logged.Level);
        Assert.Same(failure, logged.Exception);
    }

    [Fact]
    public void OnUnhandledExceptionBackgroundThreadFailureLogsFatal()
    {
        var failure = new InvalidOperationException("worker died");

        handler.OnUnhandledException(null, new UnhandledExceptionEventArgs(failure, isTerminating: true));

        var logged = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Fatal, logged.Level);
        Assert.Same(failure, logged.Exception);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
