using PalOps.Web.SaveGames.Index;

namespace PalOps.Web.SaveGames.Diff;

public sealed class SaveDiffBackfillService : BackgroundService
{
    private readonly ISaveIndexRepository _indexRepository;
    private readonly ISaveChangeSnapshotProjector _projector;
    private readonly ISaveChangeSnapshotRepository _changeRepository;
    private readonly ILogger<SaveDiffBackfillService> _logger;

    public SaveDiffBackfillService(
        ISaveIndexRepository indexRepository,
        ISaveChangeSnapshotProjector projector,
        ISaveChangeSnapshotRepository changeRepository,
        ILogger<SaveDiffBackfillService> logger)
    {
        _indexRepository = indexRepository;
        _projector = projector;
        _changeRepository = changeRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var existing = await _indexRepository.GetRecentSnapshotsAsync(30, stoppingToken);
            var imported = 0;
            foreach (var snapshot in existing.OrderBy(item => item.ParsedAt))
            {
                stoppingToken.ThrowIfCancellationRequested();
                try
                {
                    if (await _changeRepository.ExistsAsync(snapshot.SnapshotId, stoppingToken)) continue;
                    await _changeRepository.PublishAsync(_projector.Project(snapshot), stoppingToken);
                    imported++;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Historical save index {SnapshotId} could not be converted to a compact change snapshot. Backfill will continue.",
                        snapshot.SnapshotId);
                }
            }
            if (imported > 0)
                _logger.LogInformation("Backfilled {Count} compact save change snapshots from the existing index history.", imported);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save diff snapshot backfill failed. Existing save indexes remain available.");
        }
    }
}
