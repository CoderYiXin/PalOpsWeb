namespace PalOps.Web.Maintenance;

public sealed class MaintenanceSchedulerService(
    IMaintenanceRepository repository,
    IMaintenanceExecutionService execution,
    ILogger<MaintenanceSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await execution.RecoverInterruptedRunsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Maintenance startup reconciliation failed; scheduler will continue and retry due plans.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var plans = await repository.ListPlansAsync(stoppingToken);
                foreach (var plan in plans.Where(item =>
                             item.Enabled && item.NextRunAt.HasValue && item.NextRunAt.Value <= now))
                {
                    if (execution.RunningPlanIds.Contains(plan.Id)) continue;
                    try
                    {
                        await execution.StartAsync(plan.Id, "scheduler", "scheduler", "local", stoppingToken);
                    }
                    catch (InvalidOperationException ex)
                    {
                        logger.LogInformation(ex, "Maintenance plan {PlanId} remains due because another maintenance activity is running.", plan.Id);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Maintenance scheduler iteration failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
