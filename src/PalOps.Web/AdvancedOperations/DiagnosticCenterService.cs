using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PalOps.Web.Backups;
using PalOps.Web.Health;
using PalOps.Web.Infrastructure;
using PalOps.Web.Logging;
using PalOps.Web.Settings;
using PalOps.Web.Versioning;

namespace PalOps.Web.AdvancedOperations;

public interface IDiagnosticCenterService
{
    Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default);
    Task<DiagnosticSupportBundle> CreateSupportBundleAsync(CancellationToken cancellationToken = default);
}

public sealed class DiagnosticCenterService(
    ISystemHealthService health,
    IServerSettingsStore settingsStore,
    IBackupService backupService,
    ISystemLogStore logs,
    IRuntimePathResolver paths,
    IApplicationVersionProvider versionProvider,
    ILogger<DiagnosticCenterService> logger) : IDiagnosticCenterService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex SecretPattern = new(
        "(?i)(authorization|bearer|password|passwd|token|secret|api[-_ ]?key|rcon)(\\s*[:=]\\s*|\\s+)([^\\s,;\"'}]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public async Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<DiagnosticCheckResult>();
        await health.RefreshAsync(cancellationToken);
        checks.AddRange(health.Components.Select(MapHealthComponent));

        var settings = await settingsStore.GetAsync(cancellationToken);
        checks.Add(CheckConfiguredPath(
            "save.world-directory",
            "世界存档目录",
            "storage",
            settings.SaveGame.WorldDirectory,
            false,
            "在系统设置中配置 Palworld 世界存档目录。"));
        checks.Add(CheckConfiguredPath(
            "backup.directory",
            "备份目录",
            "storage",
            settings.Backup.Directory,
            true,
            "配置可写的独立备份目录，并避免与世界存档目录重叠。"));
        checks.Add(await CheckDataDirectoryAsync(cancellationToken));
        checks.Add(CheckDiskSpace());
        checks.Add(await CheckBackupSummaryAsync(cancellationToken));

        var healthy = checks.Count(static item => item.Status == AdvancedOperationStatus.Healthy);
        var critical = checks.Count(static item => item.Status == AdvancedOperationStatus.Critical);
        var warning = checks.Count - healthy - critical;
        var overall = critical > 0
            ? AdvancedOperationStatus.Critical
            : warning > 0 ? AdvancedOperationStatus.Warning : AdvancedOperationStatus.Healthy;
        return new(overall, healthy, warning, critical, DateTimeOffset.UtcNow, checks);
    }

    public async Task<DiagnosticSupportBundle> CreateSupportBundleAsync(CancellationToken cancellationToken = default)
    {
        var report = await RunAsync(cancellationToken);
        var sensitivePaths = report.Checks
            .SelectMany(static check => check.Details)
            .Where(static pair => pair.Key.Contains("path", StringComparison.OrdinalIgnoreCase)
                                  || pair.Key.Contains("directory", StringComparison.OrdinalIgnoreCase)
                                  || pair.Key.Equals("root", StringComparison.OrdinalIgnoreCase))
            .Select(static pair => pair.Value)
            .Append(paths.DataDirectory)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static value => value.Length)
            .ToArray();
        var sanitizedReport = SanitizeSupportReport(report, sensitivePaths);
        var logPage = await logs.ReadAsync(1, 200, null, null, cancellationToken);
        var sanitizedLogs = logPage.Entries.Select(entry => entry with
        {
            Message = Redact(entry.Message, sensitivePaths),
            Exception = string.IsNullOrWhiteSpace(entry.Exception) ? entry.Exception : Redact(entry.Exception, sensitivePaths)
        }).ToArray();
        var version = versionProvider.Get();
        var directory = paths.ResolveDataPath("advanced-operations", "support-bundles");
        Directory.CreateDirectory(directory);
        TrimOldBundles(directory, TimeSpan.FromDays(7));

        var fileName = $"palops-support-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip";
        var fullPath = Path.Combine(directory, fileName);
        await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, true))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            await WriteJsonEntryAsync(archive, "diagnostics.json", sanitizedReport, cancellationToken);
            await WriteJsonEntryAsync(archive, "environment.json", new
            {
                version.Application,
                version.CurrentVersion,
                version.Runtime,
                version.OperatingSystem,
                version.Architecture,
                DataDirectoryConfigured = Directory.Exists(paths.DataDirectory),
                GeneratedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
            await WriteJsonEntryAsync(archive, "recent-logs.json", sanitizedLogs, cancellationToken);
        }

        var info = new FileInfo(fullPath);
        logger.LogInformation("Created diagnostic support bundle {FileName} ({SizeBytes} bytes).", fileName, info.Length);
        return new(fileName, fullPath, info.Length, DateTimeOffset.UtcNow);
    }

    private DiagnosticCheckResult CheckDiskSpace()
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var root = Path.GetPathRoot(paths.DataDirectory);
            if (string.IsNullOrWhiteSpace(root))
                return Result("disk.free", "数据盘剩余空间", "storage", AdvancedOperationStatus.Unknown, IncidentSeverity.Low, "无法确定数据目录所在磁盘。", "确认数据目录位于本地固定磁盘。", now);
            var drive = new DriveInfo(root);
            var free = drive.AvailableFreeSpace;
            var total = Math.Max(1L, drive.TotalSize);
            var percent = free * 100d / total;
            var status = free < 2L * 1024 * 1024 * 1024 || percent < 5
                ? AdvancedOperationStatus.Critical
                : free < 10L * 1024 * 1024 * 1024 || percent < 15
                    ? AdvancedOperationStatus.Warning
                    : AdvancedOperationStatus.Healthy;
            var severity = status == AdvancedOperationStatus.Critical ? IncidentSeverity.Critical
                : status == AdvancedOperationStatus.Warning ? IncidentSeverity.Medium : IncidentSeverity.Information;
            return Result(
                "disk.free", "数据盘剩余空间", "storage", status, severity,
                $"可用 {FormatBytes(free)} / 总计 {FormatBytes(total)}（{percent:F1}%）。",
                status == AdvancedOperationStatus.Healthy ? "无需处理。" : "清理历史备份、日志或迁移数据目录。",
                now,
                new Dictionary<string, string> { ["root"] = root, ["freeBytes"] = free.ToString(), ["totalBytes"] = total.ToString() });
        }
        catch (Exception ex)
        {
            return Result("disk.free", "数据盘剩余空间", "storage", AdvancedOperationStatus.Warning, IncidentSeverity.Medium, ex.Message, "检查数据目录所在磁盘是否可访问。", now);
        }
    }

    private async Task<DiagnosticCheckResult> CheckDataDirectoryAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;
        var probe = Path.Combine(paths.DataDirectory, $".write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(probe, "palops", cancellationToken);
            File.Delete(probe);
            stopwatch.Stop();
            return Result("data.write", "数据目录写入", "storage", AdvancedOperationStatus.Healthy, IncidentSeverity.Information, "数据目录可写。", "无需处理。", now, latencyMs: stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
            return Result("data.write", "数据目录写入", "storage", AdvancedOperationStatus.Critical, IncidentSeverity.Critical, ex.Message, "授予 PalOps 服务账户对数据目录的读写和删除权限。", now, latencyMs: stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<DiagnosticCheckResult> CheckBackupSummaryAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var summary = await backupService.GetSummaryAsync(cancellationToken);
            var stale = !summary.LatestCreatedAt.HasValue || DateTimeOffset.UtcNow - summary.LatestCreatedAt.Value > TimeSpan.FromDays(2);
            var status = stale ? AdvancedOperationStatus.Warning : AdvancedOperationStatus.Healthy;
            return Result(
                "backup.freshness", "备份新鲜度", "backup", status,
                stale ? IncidentSeverity.Medium : IncidentSeverity.Information,
                summary.LatestCreatedAt.HasValue ? $"最近备份：{summary.LatestCreatedAt:O}。" : "尚无备份记录。",
                stale ? "立即创建一次备份，并检查自动任务。" : "无需处理。",
                now,
                new Dictionary<string, string> { ["count"] = summary.Count.ToString(), ["directory"] = summary.Directory });
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Result("backup.freshness", "备份新鲜度", "backup", AdvancedOperationStatus.Warning, IncidentSeverity.Medium, ex.Message, "打开备份管理检查目录和权限。", now);
        }
    }

    private static DiagnosticCheckResult CheckConfiguredPath(string code, string name, string category, string configured, bool createIfMissing, string remediation)
    {
        var now = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(configured))
            return Result(code, name, category, AdvancedOperationStatus.Warning, IncidentSeverity.Medium, "尚未配置路径。", remediation, now);
        try
        {
            var fullPath = Path.GetFullPath(configured.Trim());
            if (createIfMissing) Directory.CreateDirectory(fullPath);
            var exists = Directory.Exists(fullPath);
            return Result(code, name, category, exists ? AdvancedOperationStatus.Healthy : AdvancedOperationStatus.Critical,
                exists ? IncidentSeverity.Information : IncidentSeverity.High,
                exists ? $"路径可访问：{fullPath}" : $"路径不存在：{fullPath}",
                exists ? "无需处理。" : remediation,
                now,
                new Dictionary<string, string> { ["path"] = fullPath });
        }
        catch (Exception ex)
        {
            return Result(code, name, category, AdvancedOperationStatus.Critical, IncidentSeverity.High, ex.Message, remediation, now);
        }
    }

    private static DiagnosticCheckResult MapHealthComponent(PalOps.Web.Contracts.HealthComponentV1 component)
    {
        var status = component.Status switch
        {
            "healthy" => AdvancedOperationStatus.Healthy,
            "unavailable" => AdvancedOperationStatus.Critical,
            _ => AdvancedOperationStatus.Warning
        };
        var severity = status == AdvancedOperationStatus.Critical ? IncidentSeverity.High
            : status == AdvancedOperationStatus.Warning ? IncidentSeverity.Medium : IncidentSeverity.Information;
        return Result(
            "component." + component.Name,
            component.Name,
            "component",
            status,
            severity,
            component.Message ?? component.Status,
            status == AdvancedOperationStatus.Healthy ? "无需处理。" : "打开系统设置执行连接测试，并检查对应服务日志。",
            component.CheckedAt,
            latencyMs: component.LatencyMs);
    }

    private static DiagnosticCheckResult Result(
        string code,
        string name,
        string category,
        string status,
        string severity,
        string message,
        string remediation,
        DateTimeOffset checkedAt,
        IReadOnlyDictionary<string, string>? details = null,
        long? latencyMs = null) =>
        new(code, name, category, status, severity, latencyMs, Limit(message, 1000), Limit(remediation, 1000), checkedAt, details ?? new Dictionary<string, string>());


    private static DiagnosticReport SanitizeSupportReport(DiagnosticReport report, IReadOnlyList<string> sensitivePaths)
    {
        var checks = report.Checks.Select(check => check with
        {
            Message = Redact(check.Message, sensitivePaths),
            Remediation = Redact(check.Remediation, sensitivePaths),
            Details = check.Details.ToDictionary(
                static pair => pair.Key,
                pair => pair.Key.Contains("path", StringComparison.OrdinalIgnoreCase)
                        || pair.Key.Contains("directory", StringComparison.OrdinalIgnoreCase)
                        || pair.Key.Equals("root", StringComparison.OrdinalIgnoreCase)
                    ? "[REDACTED_PATH]"
                    : Redact(pair.Value, sensitivePaths),
                StringComparer.OrdinalIgnoreCase)
        }).ToArray();
        return report with { Checks = checks };
    }

    private static string Redact(string value, IReadOnlyList<string> sensitivePaths)
    {
        var redacted = value ?? string.Empty;
        foreach (var path in sensitivePaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
                redacted = redacted.Replace(path, "[REDACTED_PATH]", StringComparison.OrdinalIgnoreCase);
        }
        try { return SecretPattern.Replace(redacted, "$1=[REDACTED]"); }
        catch (RegexMatchTimeoutException) { return "[REDACTED_UNSAFE_TEXT]"; }
    }

    private static async Task WriteJsonEntryAsync(ZipArchive archive, string entryName, object value, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, value, JsonOptions, cancellationToken);
    }

    private static void TrimOldBundles(string directory, TimeSpan maximumAge)
    {
        var threshold = DateTime.UtcNow - maximumAge;
        foreach (var file in Directory.EnumerateFiles(directory, "palops-support-*.zip"))
        {
            try { if (File.GetCreationTimeUtc(file) < threshold) File.Delete(file); } catch { }
        }
    }

    private static string FormatBytes(long value) => value switch
    {
        >= 1024L * 1024 * 1024 => $"{value / 1024d / 1024d / 1024d:F1} GiB",
        >= 1024L * 1024 => $"{value / 1024d / 1024d:F1} MiB",
        _ => $"{value / 1024d:F1} KiB"
    };

    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
