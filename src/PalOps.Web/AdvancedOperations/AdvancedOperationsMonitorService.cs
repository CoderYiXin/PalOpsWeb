namespace PalOps.Web.AdvancedOperations;

public sealed class AdvancedOperationsMonitorService(
    IIncidentCenterService incidents,
    ILogger<AdvancedOperationsMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            await EvaluateAsync(stoppingToken);
            using var timer = new PeriodicTimer(EvaluationInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await EvaluateAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var changed = await incidents.EvaluateHealthAsync(cancellationToken);
            if (changed > 0)
                logger.LogInformation("Advanced operations health evaluation changed {ChangedCount} incident records.", changed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Advanced operations health evaluation failed.");
        }
    }
}
