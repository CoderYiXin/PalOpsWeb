using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using PalOps.Web.Backups;
using PalOps.Web.External;
using PalOps.Web.Rcon;
using PalOps.Web.SaveGames;
using PalOps.Web.Settings;

namespace PalOps.Web.Automation;

public sealed record AutomationExecutionResult(bool Started, string Status, string Message, long DurationMs);

public interface IAutomationExecutionService
{
    IReadOnlySet<string> RunningJobIds { get; }
    Task<AutomationExecutionResult> ExecuteAsync(AutomationJob job, string trigger, CancellationToken cancellationToken = default);
}

public sealed class AutomationExecutionService(
    IAutomationRepository repository,
    IServerSettingsStore settingsStore,
    IRconClient rcon,
    IPalDefenderApiClient palDefender,
    IPalworldApiClient palworld,
    IBackupService backups,
    ISaveIndexingService saveIndexing,
    ILogger<AutomationExecutionService> logger) : IAutomationExecutionService
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> RunningJobIds => _running.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<AutomationExecutionResult> ExecuteAsync(AutomationJob job, string trigger, CancellationToken cancellationToken = default)
    {
        if (!_running.TryAdd(job.Id, 0))
            return new AutomationExecutionResult(false, "skipped", "任务仍在执行，已跳过重叠运行。", 0);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        string status;
        string message;
        try
        {
            message = await ExecuteCoreAsync(job, cancellationToken);
            status = "success";
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            status = "failed";
            message = Limit(ex.Message, 500);
            logger.LogError(ex, "Automation job failed. JobId={JobId}, Type={JobType}, Trigger={Trigger}", job.Id, job.JobType, trigger);
        }
        finally
        {
            stopwatch.Stop();
            _running.TryRemove(job.Id, out _);
        }

        var completedAt = DateTimeOffset.UtcNow;
        var settings = await settingsStore.GetAsync(CancellationToken.None);
        var nextRun = job.Enabled ? AutomationSchedule.GetNextRun(job.ScheduleType, job.ScheduleExpression, completedAt) : null;
        var updated = job with
        {
            LastRunAt = completedAt,
            NextRunAt = nextRun,
            LastStatus = status,
            LastMessage = message,
            ConsecutiveFailures = status == "success" ? 0 : job.ConsecutiveFailures + 1,
            UpdatedAt = completedAt
        };
        await repository.UpsertJobAsync(updated, CancellationToken.None);
        await repository.AppendRunAsync(
            new AutomationRunRecord(Guid.NewGuid().ToString("N"), job.Id, startedAt, completedAt, status, message, stopwatch.ElapsedMilliseconds),
            settings.Automation.MaximumHistoryEntries,
            CancellationToken.None);
        return new AutomationExecutionResult(true, status, message, stopwatch.ElapsedMilliseconds);
    }

    private async Task<string> ExecuteCoreAsync(AutomationJob job, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        using var payload = ParsePayload(job.PayloadJson);
        var root = payload.RootElement;
        switch (job.JobType)
        {
            case AutomationJobTypes.SaveWorld:
            {
                var result = await rcon.ExecuteAsync(settings.Rcon, "Save", cancellationToken);
                EnsureRconSuccess(result.Response);
                return "世界存档保存命令已执行。";
            }
            case AutomationJobTypes.Broadcast:
            {
                var message = ReadString(root, "message", required: true, maximumLength: 1000);
                var alert = ReadBoolean(root, "alert", false);
                await palDefender.BroadcastAsync(message, alert, cancellationToken);
                return alert ? "醒目警报已发送。" : "全服广播已发送。";
            }
            case AutomationJobTypes.CreateBackup:
            {
                var note = ReadString(root, "note", required: false, maximumLength: 300);
                bool? saveFirst = root.TryGetProperty("executeSaveFirst", out var saveElement) && saveElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? saveElement.GetBoolean()
                    : null;
                var backup = await backups.CreateAsync(note, saveFirst, cancellationToken);
                return $"备份已创建：{backup.FileName}";
            }
            case AutomationJobTypes.ReloadPalDefender:
                await palDefender.ReloadConfigAsync(cancellationToken);
                return "PalDefender 配置已重载。";
            case AutomationJobTypes.ScheduledShutdown:
            {
                var seconds = ReadInteger(root, "seconds", 60, 10, 3600);
                var message = ReadString(root, "message", required: false, maximumLength: 300);
                if (string.IsNullOrWhiteSpace(message)) message = "Server scheduled shutdown";
                var result = await rcon.ExecuteAsync(settings.Rcon, $"Shutdown {seconds} {message}", cancellationToken);
                EnsureRconSuccess(result.Response);
                return $"已计划在 {seconds} 秒后关闭服务器。";
            }
            case AutomationJobTypes.HealthCheck:
            {
                var info = await palworld.GetInfoAsync(cancellationToken);
                var result = await rcon.ExecuteAsync(settings.Rcon, "Info", cancellationToken);
                EnsureRconSuccess(result.Response);
                return $"健康检查完成，REST 返回 {Math.Min(info.Length, 9999)} 字符，RCON 延迟 {result.ElapsedMilliseconds}ms。";
            }
            case AutomationJobTypes.ParseSave:
            {
                var result = await saveIndexing.TriggerAsync("automation:" + job.Id, cancellationToken);
                return result.Started ? "存档解析任务已启动。" : result.Message;
            }
            default:
                throw new ArgumentException("不支持的自动化任务类型。");
        }
    }

    private static JsonDocument ParsePayload(string payloadJson)
    {
        var json = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
        try { return JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new ArgumentException("自动化任务 PayloadJson 不是有效 JSON。", ex); }
    }

    private static string ReadString(JsonElement root, string name, bool required, int maximumLength)
    {
        var value = root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim() ?? string.Empty
            : string.Empty;
        if (required && string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Payload 缺少 {name}。");
        if (value.Length > maximumLength) throw new ArgumentException($"Payload.{name} 不能超过 {maximumLength} 个字符。");
        return value;
    }

    private static bool ReadBoolean(JsonElement root, string name, bool fallback)
        => root.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : fallback;

    private static int ReadInteger(JsonElement root, string name, int fallback, int minimum, int maximum)
    {
        var value = root.TryGetProperty(name, out var element) && element.TryGetInt32(out var parsed) ? parsed : fallback;
        if (value < minimum || value > maximum) throw new ArgumentException($"Payload.{name} 必须在 {minimum} 到 {maximum} 之间。");
        return value;
    }

    private static void EnsureRconSuccess(string response)
    {
        if (response.Contains("unknown command", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("command not found", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("invalid argument", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("RCON 返回失败：" + Limit(response.Trim(), 300));
    }

    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
