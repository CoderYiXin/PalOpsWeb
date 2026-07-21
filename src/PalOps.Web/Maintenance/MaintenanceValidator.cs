using PalOps.Web.Automation;

namespace PalOps.Web.Maintenance;

public sealed class MaintenanceValidator
{
    private static readonly HashSet<string> AllowedScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cmd", ".bat", ".ps1", ".exe"
    };

    public CrashGuardConfiguration Validate(
        CrashGuardConfigurationWriteRequest request,
        string userName,
        DateTimeOffset now)
    {
        if (request.MaximumCrashes is < 2 or > 20)
            throw new ArgumentException("熔断阈值必须在 2 到 20 次之间。");
        if (request.WindowMinutes is < 1 or > 1440)
            throw new ArgumentException("崩溃统计窗口必须在 1 到 1440 分钟之间。");
        if (request.RestartDelaySeconds is < 0 or > 1800)
            throw new ArgumentException("自动重启延迟必须在 0 到 1800 秒之间。");
        if (request.OperationTimeoutSeconds is < 30 or > 1800)
            throw new ArgumentException("启动操作超时必须在 30 到 1800 秒之间。");

        return new CrashGuardConfiguration(
            request.Enabled,
            request.MaximumCrashes,
            request.WindowMinutes,
            request.RestartDelaySeconds,
            request.OperationTimeoutSeconds,
            request.NotifyOnRestart,
            now,
            NormalizeUser(userName));
    }

    public MaintenancePlan Create(
        MaintenancePlanWriteRequest request,
        string? id,
        MaintenancePlan? existing,
        string userName,
        DateTimeOffset now)
    {
        var name = Normalize(request.Name, 100, "维护计划名称");
        if (name.Length < 2) throw new ArgumentException("维护计划名称至少需要 2 个字符。");

        var scheduleType = (request.ScheduleType ?? string.Empty).Trim().ToLowerInvariant();
        if (scheduleType is not ("manual" or "daily" or "once"))
            throw new ArgumentException("维护计划仅支持手动、每日或单次执行。");
        var scheduleExpression = (request.ScheduleExpression ?? string.Empty).Trim();
        AutomationSchedule.Validate(scheduleType, scheduleExpression);

        if (request.AnnouncementEnabled)
        {
            if (request.AnnouncementCountdownSeconds is < 10 or > 3600)
                throw new ArgumentException("公告倒计时必须在 10 到 3600 秒之间。");
            var announcement = Normalize(request.AnnouncementMessage, 500, "维护公告");
            if (!announcement.Contains("{seconds}", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("维护公告必须包含 {seconds} 倒计时占位符。");
        }

        if (request.ScriptEnabled)
        {
            if (!request.StopServer)
                throw new ArgumentException("执行维护脚本前必须启用安全停服步骤。");
            ValidateScriptPath(request.ScriptPath);
            if ((request.ScriptArguments ?? string.Empty).Length > 2000)
                throw new ArgumentException("脚本参数不能超过 2000 个字符。");
            if (request.ScriptTimeoutSeconds is < 10 or > 7200)
                throw new ArgumentException("脚本超时必须在 10 到 7200 秒之间。");
        }

        if (request.StartServer && !request.StopServer)
            throw new ArgumentException("启用启动服务器时必须同时启用安全停服步骤。");
        if ((request.VerifyProcess || request.VerifyRest || request.VerifyRcon) && !request.StartServer)
            throw new ArgumentException("健康验证需要先启用启动服务器步骤。");
        if (request.HealthTimeoutSeconds is < 15 or > 900)
            throw new ArgumentException("健康验证超时必须在 15 到 900 秒之间。");
        if (request.HealthRetrySeconds is < 1 or > 60)
            throw new ArgumentException("健康验证重试间隔必须在 1 到 60 秒之间。");
        if (request.HealthRetrySeconds >= request.HealthTimeoutSeconds)
            throw new ArgumentException("健康验证重试间隔必须小于总超时。");

        var anyStep = request.AnnouncementEnabled || request.SaveWorld || request.CreateBackup ||
                      request.StopServer || request.ScriptEnabled || request.StartServer;
        if (!anyStep) throw new ArgumentException("维护计划至少需要启用一个执行步骤。");

        var planId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        var nextRun = request.Enabled
            ? AutomationSchedule.GetNextRun(scheduleType, scheduleExpression, now)
            : null;
        if (request.Enabled && scheduleType == "once" && nextRun is null)
            throw new ArgumentException("一次性维护时间必须晚于当前时间。");

        return new MaintenancePlan(
            planId,
            name,
            request.Enabled,
            scheduleType,
            scheduleExpression,
            nextRun,
            existing?.LastRunAt,
            existing?.LastStatus ?? "never",
            existing?.LastMessage ?? "尚未执行。",
            request.AnnouncementEnabled,
            request.AnnouncementCountdownSeconds,
            Normalize(request.AnnouncementMessage, 500, "维护公告", allowEmpty: !request.AnnouncementEnabled),
            request.SaveWorld,
            request.CreateBackup,
            Normalize(request.BackupNote, 300, "备份备注", allowEmpty: true),
            request.StopServer,
            request.ScriptEnabled,
            (request.ScriptPath ?? string.Empty).Trim(),
            (request.ScriptArguments ?? string.Empty).Trim(),
            request.ScriptTimeoutSeconds,
            request.StartServer,
            request.VerifyProcess,
            request.VerifyRest,
            request.VerifyRcon,
            request.HealthTimeoutSeconds,
            request.HealthRetrySeconds,
            existing?.CreatedAt ?? now,
            now,
            NormalizeUser(userName));
    }

    public static void RequireExecutionConfirmation(string? confirmation)
    {
        if (!string.Equals(confirmation?.Trim(), "RUN MAINTENANCE", StringComparison.Ordinal))
            throw new ArgumentException("执行维护计划必须精确输入 RUN MAINTENANCE。");
    }

    public static string ValidateReason(string? reason)
    {
        var value = (reason ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (value.Length is < 5 or > 500)
            throw new ArgumentException("操作原因长度必须在 5 到 500 个字符之间。");
        return value;
    }

    public static void RequireResetConfirmation(string? confirmation)
    {
        if (!string.Equals(confirmation?.Trim(), "RESET CRASH GUARD", StringComparison.Ordinal))
            throw new ArgumentException("重置熔断器必须精确输入 RESET CRASH GUARD。");
    }

    private static void ValidateScriptPath(string? rawPath)
    {
        var path = (rawPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("已启用维护脚本，但脚本路径为空。");
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("维护脚本不允许使用网络共享路径。");
        if (!IsAbsoluteLocalPath(path)) throw new ArgumentException("维护脚本必须使用绝对本地路径。");
        if (!AllowedScriptExtensions.Contains(Path.GetExtension(path)))
            throw new ArgumentException("维护脚本仅支持 .cmd、.bat、.ps1 或 .exe。");
    }

    private static bool IsAbsoluteLocalPath(string path)
    {
        if (Path.IsPathFullyQualified(path)) return true;
        return path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }

    private static string Normalize(string? value, int maximum, string fieldName, bool allowEmpty = false)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (!allowEmpty && string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException($"{fieldName}不能为空。");
        if (normalized.Length > maximum) throw new ArgumentException($"{fieldName}不能超过 {maximum} 个字符。");
        return normalized;
    }

    private static string NormalizeUser(string? userName)
    {
        var value = (userName ?? "unknown").Trim();
        return value.Length <= 100 ? value : value[..100];
    }
}
