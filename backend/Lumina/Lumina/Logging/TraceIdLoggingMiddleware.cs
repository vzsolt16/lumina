using System.Diagnostics;
using Serilog.Context;

namespace Lumina.Logging;

public static class TraceIdLogScope
{
    public static IDisposable? PushCurrent(HttpContext? context = null)
    {
        var traceId = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(traceId)) traceId = context?.TraceIdentifier;
        return string.IsNullOrWhiteSpace(traceId) ? null : LogContext.PushProperty("TraceId", traceId);
    }
}

public sealed class TraceIdLoggingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using (TraceIdLogScope.PushCurrent(context))
            await next(context);
    }
}
