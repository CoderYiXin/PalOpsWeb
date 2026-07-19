using Microsoft.Extensions.Logging;

namespace PalOps.Web.Audit;

/// <summary>
/// Writes an audit record without allowing a secondary audit-storage failure to
/// change the HTTP result of a business operation that has already succeeded.
/// </summary>
public static class AuditLogExtensions
{
    public static async Task WriteBestEffortAsync(
        this IAuditLogService audit,
        ILogger logger,
        string eventType,
        string outcome,
        string remoteIp,
        string summary,
        object? data = null)
    {
        try
        {
            // Do not reuse HttpContext.RequestAborted here. The external side
            // effect may already be complete when the browser disconnects.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await audit.WriteAsync(eventType, outcome, remoteIp, summary, data, timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Audit write failed after operation completion. EventType={EventType}, Outcome={Outcome}, Summary={Summary}",
                eventType,
                outcome,
                summary);
        }
    }
}
