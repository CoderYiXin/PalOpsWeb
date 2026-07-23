using System.Diagnostics;

namespace PalOps.Web.Infrastructure;

/// <summary>
/// Adds stable request-correlation and server timing metadata to every API response.
/// The body contracts remain backward compatible; clients can use the headers even
/// for empty, streaming, or framework-generated responses.
/// </summary>
public sealed class ApiResponseMetadataMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        context.Response.OnStarting(() =>
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
            context.Response.Headers["Server-Timing"] = $"app;dur={elapsed.TotalMilliseconds:F1}";
            return Task.CompletedTask;
        });

        await next(context);
    }
}