using PalOps.Web.Backups;

namespace PalOps.Web.AdvancedOperations;

public interface IDisasterRecoveryService
{
    Task<DisasterRecoveryDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<DisasterRecoveryTarget> UpsertTargetAsync(string? id, DisasterRecoveryTargetWriteRequest request, string actor, CancellationToken cancellationToken = default);
    Task<DisasterRecoveryTarget> ValidateTargetAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteTargetAsync(string id, CancellationToken cancellationToken = default);
    Task<DisasterRecoveryDrill> RunDrillAsync(string id, DisasterRecoveryDrillRequest request, string actor, CancellationToken cancellationToken = default);
}

public sealed class DisasterRecoveryService(
    IAdvancedOperationsRepository repository,
    IBackupService backupService,
    AdvancedOperationsValidator validator,
    ILogger<DisasterRecoveryService> logger) : IDisasterRecoveryService
{
    public async Task<DisasterRecoveryDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var state = await repository.ReadAsync(cancellationToken);
        return new(
            state.DisasterRecoveryTargets.Count(static item => item.Enabled),
            state.DisasterRecoveryTargets.Count,
            state.DisasterRecoveryDrills.Count(static item => item.Status == AdvancedOperationStatus.Succeeded),
            state.DisasterRecoveryDrills.Count(static item => item.Status == AdvancedOperationStatus.Failed),
            state.DisasterRecoveryTargets.OrderByDescending(static item => item.Enabled).ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            state.DisasterRecoveryDrills.OrderByDescending(static item => item.StartedAt).Take(100).ToArray(),
            DateTimeOffset.UtcNow);
    }

    public Task<DisasterRecoveryTarget> UpsertTargetAsync(string? id, DisasterRecoveryTargetWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var name = validator.ValidateName(request.Name, nameof(request.Name), 120);
        var type = validator.NormalizeTargetType(request.TargetType);
        var endpoint = validator.ValidateEndpoint(type, request.Endpoint);
        var credentialReference = validator.LimitText(request.CredentialReference, 200);
        var retention = Math.Clamp(request.RetentionCount, 1, 500);
        var now = DateTimeOffset.UtcNow;
        return repository.MutateAsync(state =>
        {
            var existing = string.IsNullOrWhiteSpace(id)
                ? null
                : state.DisasterRecoveryTargets.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            var target = new DisasterRecoveryTarget(
                existing?.Id ?? Guid.NewGuid().ToString("N"),
                name,
                type,
                endpoint,
                credentialReference,
                request.Enabled,
                retention,
                existing?.CreatedAt ?? now,
                now,
                actor,
                existing?.LastValidationStatus ?? AdvancedOperationStatus.Unknown,
                existing?.LastValidationMessage ?? "尚未验证。",
                existing?.LastValidatedAt);
            Replace(state.DisasterRecoveryTargets, target);
            return target;
        }, cancellationToken);
    }

    public async Task<DisasterRecoveryTarget> ValidateTargetAsync(string id, CancellationToken cancellationToken = default)
    {
        var state = await repository.ReadAsync(cancellationToken);
        var target = state.DisasterRecoveryTargets.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                     ?? throw new KeyNotFoundException("Disaster recovery target not found.");
        var now = DateTimeOffset.UtcNow;
        string status;
        string message;
        try
        {
            if (target.TargetType is DisasterRecoveryTargetType.Local or DisasterRecoveryTargetType.Unc)
            {
                Directory.CreateDirectory(target.Endpoint);
                var probe = Path.Combine(target.Endpoint, $".palops-dr-probe-{Guid.NewGuid():N}.tmp");
                await File.WriteAllTextAsync(probe, "palops", cancellationToken);
                File.Delete(probe);
                status = AdvancedOperationStatus.Healthy;
                message = "目标目录可写。";
            }
            else
            {
                _ = new Uri(target.Endpoint, UriKind.Absolute);
                status = AdvancedOperationStatus.Warning;
                message = "远程端点格式有效；凭据仅保存引用，实际上传通道需要由部署环境提供。";
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            status = AdvancedOperationStatus.Critical;
            message = Limit(ex.Message);
        }

        var updated = target with { LastValidationStatus = status, LastValidationMessage = message, LastValidatedAt = now, UpdatedAt = now };
        return await repository.MutateAsync(document =>
        {
            Replace(document.DisasterRecoveryTargets, updated);
            return updated;
        }, cancellationToken);
    }

    public async Task DeleteTargetAsync(string id, CancellationToken cancellationToken = default)
    {
        _ = await repository.MutateAsync(state =>
        {
            var removed = state.DisasterRecoveryTargets.RemoveAll(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) throw new KeyNotFoundException("Disaster recovery target not found.");
            return true;
        }, cancellationToken);
    }

    public async Task<DisasterRecoveryDrill> RunDrillAsync(string id, DisasterRecoveryDrillRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var state = await repository.ReadAsync(cancellationToken);
        var target = state.DisasterRecoveryTargets.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                     ?? throw new KeyNotFoundException("Disaster recovery target not found.");
        if (!target.Enabled) throw new InvalidOperationException("Disaster recovery target is disabled.");
        var backups = await backupService.ListAsync(cancellationToken);
        var backup = backups.FirstOrDefault(item => item.Id.Equals(request.BackupId, StringComparison.OrdinalIgnoreCase))
                     ?? throw new KeyNotFoundException("Backup not found.");
        var startedAt = DateTimeOffset.UtcNow;
        var status = AdvancedOperationStatus.Succeeded;
        var message = "备份校验通过；未覆盖正式存档。";
        try
        {
            var verification = await backupService.VerifyAsync(backup.Id, cancellationToken);
            if (!verification.Valid) throw new InvalidDataException(verification.Message ?? "Backup verification failed.");
            if (target.TargetType is DisasterRecoveryTargetType.Local or DisasterRecoveryTargetType.Unc)
            {
                Directory.CreateDirectory(target.Endpoint);
                var source = await backupService.GetArchivePathAsync(backup.Id, cancellationToken);
                var drillDirectory = Path.Combine(target.Endpoint, "palops-drill");
                Directory.CreateDirectory(drillDirectory);
                var destination = Path.Combine(drillDirectory, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Path.GetFileName(source)}");
                File.Copy(source, destination, false);
                if (new FileInfo(destination).Length != new FileInfo(source).Length)
                    throw new InvalidDataException("Copied archive size does not match source archive.");
                message = $"备份校验及隔离复制完成：{destination}。未覆盖正式存档。";
                TrimCopies(drillDirectory, target.RetentionCount);
            }
            else
            {
                message = "备份校验通过；远程目标凭据采用外部引用，本次演练完成到上传前检查阶段。未覆盖正式存档。";
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            status = AdvancedOperationStatus.Failed;
            message = Limit(ex.Message);
            logger.LogWarning(ex, "Disaster recovery drill failed for target {TargetId} and backup {BackupId}.", target.Id, backup.Id);
        }
        var completedAt = DateTimeOffset.UtcNow;
        var drill = new DisasterRecoveryDrill(
            Guid.NewGuid().ToString("N"), target.Id, target.Name, backup.Id, backup.FileName, status, message, startedAt, completedAt, actor);
        await repository.MutateAsync(document => { document.DisasterRecoveryDrills.Add(drill); return true; }, cancellationToken);
        return drill;
    }

    private static void TrimCopies(string directory, int keep)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.zip").Select(static path => new FileInfo(path))
                     .OrderByDescending(static item => item.CreationTimeUtc).Skip(Math.Clamp(keep, 1, 500)))
        {
            try { file.Delete(); } catch { }
        }
    }

    private static void Replace(List<DisasterRecoveryTarget> items, DisasterRecoveryTarget updated)
    {
        var index = items.FindIndex(item => item.Id.Equals(updated.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) items.Add(updated); else items[index] = updated;
    }

    private static string Limit(string value) => value.Length <= 1000 ? value : value[..1000];
}
