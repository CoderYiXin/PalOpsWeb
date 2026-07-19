using System.Text.Json;
using PalOps.Web.Audit;
using PalOps.Web.Automation;
using PalOps.Web.Contracts;
using PalOps.Web.Infrastructure;
using PalOps.Web.Security;
using PalOps.Web.Settings;

namespace PalOps.Web.Endpoints;

public static class AutomationEndpoints
{
    public static IEndpointRouteBuilder MapAutomationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/automation").RequireAuthorization("Operator").WithTags("Automation");

        group.MapGet("/jobs", async (
            IAutomationRepository repository,
            IAutomationExecutionService execution,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var running = execution.RunningJobIds;
            var jobs = (await repository.ListJobsAsync(cancellationToken)).Select(job => ToContract(job, running.Contains(job.Id))).ToArray();
            return Results.Ok(new ApiResponse<IReadOnlyList<AutomationJobV1>>(jobs, context.TraceIdentifier, []));
        });

        group.MapGet("/summary", async (
            IAutomationRepository repository,
            IAutomationExecutionService execution,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var jobs = await repository.ListJobsAsync(cancellationToken);
            var running = execution.RunningJobIds;
            var nextRun = jobs.Where(x => x.Enabled && x.NextRunAt.HasValue)
                .OrderBy(x => x.NextRunAt)
                .Select(x => x.NextRunAt)
                .FirstOrDefault();
            var summary = new AutomationSummaryV1(
                jobs.Count,
                jobs.Count(x => x.Enabled),
                jobs.Count(x => running.Contains(x.Id)),
                jobs.Count(x => x.LastStatus.Equals("failed", StringComparison.OrdinalIgnoreCase)),
                nextRun);
            return Results.Ok(new ApiResponse<AutomationSummaryV1>(summary, context.TraceIdentifier, []));
        });

        group.MapGet("/runs", async (
            string? jobId,
            int? limit,
            IAutomationRepository repository,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var runs = (await repository.ListRunsAsync(jobId, limit ?? 200, cancellationToken))
                .Select(ToContract).ToArray();
            return Results.Ok(new ApiResponse<IReadOnlyList<AutomationRunRecordV1>>(runs, context.TraceIdentifier, []));
        });

        group.MapPost("/jobs", async (
            AutomationJobWriteRequest request,
            IAutomationRepository repository,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var job = CreateJob(request, null);
            RequireAdministratorForHighRisk(context, job.RiskLevel);
            await repository.UpsertJobAsync(job, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "automation.create",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已创建自动化任务 {job.Name}。",
                new { job.Id, job.JobType, job.ScheduleType, job.ScheduleExpression, job.RiskLevel });
            return Results.Created($"/api/v1/automation/jobs/{job.Id}", new ApiResponse<AutomationJobV1>(ToContract(job, false), context.TraceIdentifier, []));
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/jobs/{id}", async (
            string id,
            AutomationJobWriteRequest request,
            IAutomationRepository repository,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var existing = await repository.FindJobAsync(id, cancellationToken) ?? throw new KeyNotFoundException("自动化任务不存在。");
            var job = CreateJob(request, existing);
            RequireAdministratorForHighRisk(context, job.RiskLevel);
            await repository.UpsertJobAsync(job, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "automation.update",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已更新自动化任务 {job.Name}。",
                new { job.Id, job.JobType, job.Enabled, job.ScheduleType, job.ScheduleExpression, job.RiskLevel });
            return Results.Ok(new ApiResponse<AutomationJobV1>(ToContract(job, false), context.TraceIdentifier, []));
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/jobs/{id}/run", async (
            string id,
            string? confirmation,
            IAutomationRepository repository,
            IAutomationExecutionService execution,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var job = await repository.FindJobAsync(id, cancellationToken) ?? throw new KeyNotFoundException("自动化任务不存在。");
            RequireAdministratorForHighRisk(context, job.RiskLevel);
            RequireHighRiskConfirmation(job.RiskLevel, confirmation);
            var result = await execution.ExecuteAsync(job, "manual", cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "automation.run",
                result.Status == "success" ? "success" : "failed",
                EndpointHelpers.RemoteIp(context),
                $"手动执行自动化任务 {job.Name}：{result.Status}。",
                new { job.Id, job.JobType, result.Status, result.DurationMs });
            return Results.Ok(new ApiResponse<AutomationExecutionResult>(result, context.TraceIdentifier, []));
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/jobs/{id}", async (
            string id,
            string? confirmation,
            IAutomationRepository repository,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var job = await repository.FindJobAsync(id, cancellationToken) ?? throw new KeyNotFoundException("自动化任务不存在。");
            RequireAdministratorForHighRisk(context, job.RiskLevel);
            RequireHighRiskConfirmation(job.RiskLevel, confirmation);
            await repository.DeleteJobAsync(id, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "automation.delete",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已删除自动化任务 {job.Name}。",
                new { job.Id, job.JobType });
            return Results.NoContent();
        }).AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }

    private static AutomationJob CreateJob(AutomationJobWriteRequest request, AutomationJob? existing)
    {
        var name = (request.Name ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (name.Length is < 1 or > 100) throw new ArgumentException("任务名称长度必须在 1 到 100 个字符之间。");
        var jobType = (request.JobType ?? string.Empty).Trim();
        if (!AutomationJobTypes.All.Contains(jobType)) throw new ArgumentException("自动化任务类型无效。");
        var scheduleType = (request.ScheduleType ?? string.Empty).Trim().ToLowerInvariant();
        var expression = (request.ScheduleExpression ?? string.Empty).Trim();
        AutomationSchedule.Validate(scheduleType, expression);
        var payload = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson.Trim();
        try { using var _ = JsonDocument.Parse(payload); }
        catch (JsonException ex) { throw new ArgumentException("PayloadJson 不是有效 JSON。", ex); }
        if (payload.Length > 10_000) throw new ArgumentException("PayloadJson 不能超过 10000 个字符。");
        var risk = jobType.Equals(AutomationJobTypes.ScheduledShutdown, StringComparison.OrdinalIgnoreCase) ? "High" :
            jobType.Equals(AutomationJobTypes.Broadcast, StringComparison.OrdinalIgnoreCase) ||
            jobType.Equals(AutomationJobTypes.ReloadPalDefender, StringComparison.OrdinalIgnoreCase) ? "Elevated" : "Safe";
        RequireHighRiskConfirmation(risk, request.Confirmation);
        var now = DateTimeOffset.UtcNow;
        return new AutomationJob(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            name,
            jobType,
            request.Enabled,
            scheduleType,
            expression,
            payload,
            risk,
            existing?.CreatedAt ?? now,
            now,
            existing?.LastRunAt,
            request.Enabled ? AutomationSchedule.GetNextRun(scheduleType, expression, now) : null,
            existing?.LastStatus ?? "never",
            existing?.LastMessage ?? string.Empty,
            existing?.ConsecutiveFailures ?? 0);
    }

    private static void RequireAdministratorForHighRisk(HttpContext context, string risk)
    {
        if (risk.Equals("High", StringComparison.OrdinalIgnoreCase) &&
            !context.User.IsInRole(PalOpsRoles.Owner) && !context.User.IsInRole(PalOpsRoles.Administrator))
            throw new ForbiddenOperationException("高危自动化任务仅允许所有者或管理员管理和执行。");
    }

    private static void RequireHighRiskConfirmation(string risk, string? confirmation)
    {
        if (risk.Equals("High", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(confirmation?.Trim(), "CONFIRM", StringComparison.Ordinal))
            throw new ArgumentException("高危自动化任务必须输入 CONFIRM。");
    }

    private static AutomationJobV1 ToContract(AutomationJob job, bool running) => new(
        job.Id, job.Name, job.JobType, job.Enabled, job.ScheduleType, job.ScheduleExpression,
        job.PayloadJson, job.RiskLevel, job.CreatedAt, job.UpdatedAt, job.LastRunAt, job.NextRunAt,
        job.LastStatus, job.LastMessage, job.ConsecutiveFailures, running);

    private static AutomationRunRecordV1 ToContract(AutomationRunRecord run) => new(
        run.Id, run.JobId, run.StartedAt, run.CompletedAt, run.Status, run.Message, run.DurationMs);
}
