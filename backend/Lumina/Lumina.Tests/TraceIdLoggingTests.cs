using System.Diagnostics;
using Lumina.Logging;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Lumina.Tests;

public sealed class TraceIdLoggingTests
{
    [Fact]
    public async Task MiddlewarePropagatesActivityTraceIdAcrossAsyncWork()
    {
        var sink = new InMemorySink();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        var previous = Activity.Current;
        try
        {
            using var activity = new Activity("test-request").SetIdFormat(ActivityIdFormat.W3C).Start();
            var expected = activity.TraceId.ToString();
            var context = new DefaultHttpContext();
            context.TraceIdentifier = "http-fallback";
            var middleware = new TraceIdLoggingMiddleware(async _ =>
            {
                await Task.Yield();
                logger.Information("service log one");
                await Task.Yield();
                logger.Information("service log two");
            });

            await middleware.InvokeAsync(context);
            var logs = sink.Events.ToArray();
            Assert.Equal(2, logs.Length);
            Assert.All(logs, item => Assert.Equal(expected, item.Properties["TraceId"].ToString().Trim('"')));
        }
        finally { Activity.Current = previous; }
    }

    [Fact]
    public void MissingRequestContextDoesNotInventTraceId()
    {
        var sink = new InMemorySink();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        var previous = Activity.Current;
        try
        {
            Activity.Current = null;
            using (TraceIdLogScope.PushCurrent())
                logger.Information("background operation");
            Assert.DoesNotContain("TraceId", sink.Events.Single().Properties.Keys);
        }
        finally { Activity.Current = previous; }
    }

    private sealed class InMemorySink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
    }
}
