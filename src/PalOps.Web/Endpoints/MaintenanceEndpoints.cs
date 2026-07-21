using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.Events;
using PalOps.Web.Maintenance;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class MaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/maintenance")
            .RequireAuthorization()
            .WithTags("Maintenance");

        group.MapGet("/dashboard", async (
            IMaintenanceExecutionService execution,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await execution.GetDashboardAsync(cancellationToken), context)));

        group.MapGet("/plans", async (
            IMaintenanceRepository repository,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response<IReadOnlyList<MaintenancePlan>>(
                await repository.ListPlansAsync(cancellationToken), context)));

        group.MapPost("/plans", async (
            MaintenancePlanWriteRequest request,
            IMaintenanceRepository repository,
            MaintenanceValidator validator,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var plan = validator.Create(request, null, null, User(context), DateTimeOffset.UtcNow);
            await repository.UpsertPlanAsync(plan, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "maintenance.plan.create",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已创建维护计划 {plan.Name}。",
                new { plan.Id, plan.ScheduleType, plan.ScheduleExpression, plan.Enabled, plan.ScriptEnabled });
            return Results.Created($"/api/v1/maintenance/plans/{plan.Id}", Response(plan, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/plans/{id}", async (
            string id,
            MaintenancePlanWriteRequest request,
            IMaintenanceRepository repository,
            MaintenanceValidator validator,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var existing = await repository.FindPlanAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException("维护计划不存在。");
            var plan = validator.Create(request, existing.Id, existing, User(context), DateTimeOffset.UtcNow);
            await repository.UpsertPlanAsync(plan, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "maintenance.plan.update",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已更新维护计划 {plan.Name}。",
                new { plan.Id, plan.ScheduleType, plan.ScheduleExpression, plan.Enabled, plan.ScriptEnabled });
            return Results.Ok(Response(plan, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/plans/{id}", async (
            string id,
            string confirmation,
            IMaintenanceRepository repository,
            IMaintenanceExecutionService execution,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!string.Equals(confirmation?.Trim(), "CONFIRM", StringComparison.Ordinal))
                throw new ArgumentException("删除维护计划必须输入 CONFIRM。");
            if (execution.RunningPlanIds.Contains(id))
                throw new InvalidOperationException("维护计划正在执行，不能删除。");
            var plan = await repository.FindPlanAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException("维护计划不存在。");
            await repository.DeletePlanAsync(id, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "maintenance.plan.delete",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已删除维护计划 {plan.Name}。",
                new { plan.Id });
            return Results.NoContent();
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/plans/{id}/run", async (
            string id,
            MaintenanceRunRequest request,
            IMaintenanceExecutionService execution,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            MaintenanceValidator.RequireExecutionConfirmation(request.Confirmation);
            var run = await execution.StartAsync(
                id,
                "manual",
                User(context),
                EndpointHelpers.RemoteIp(context),
                cancellationToken);
            return Results.Accepted($"/api/v1/maintenance/runs/{run.Id}", Response(run, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/runs", async (
            int? limit,
            IMaintenanceExecutionService execution,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response<IReadOnlyList<MaintenanceRun>>(
                await execution.ListRunsAsync(limit ?? 100, cancellationToken), context)));

        group.MapGet("/runs/{id}", async (
            string id,
            IMaintenanceExecutionService execution,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var run = await execution.FindRunAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException("维护运行记录不存在。");
            return Results.Ok(Response(run, context));
        });

        group.MapPost("/runs/{id}/cancel", async (
            string id,
            MaintenanceReasonRequest request,
            IMaintenanceExecutionService execution,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var reason = MaintenanceValidator.ValidateReason(request.Reason);
            if (!await execution.CancelAsync(id, cancellationToken))
                throw new InvalidOperationException("指定维护流程未运行或已经结束。");
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "maintenance.run.cancel",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已请求取消维护流程 {id}。",
                new { id, reason, user = User(context) });
            return Results.Accepted();
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/crash-guard", async (
            CrashGuardConfigurationWriteRequest request,
            IMaintenanceRepository repository,
            MaintenanceValidator validator,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var configuration = validator.Validate(request, User(context), DateTimeOffset.UtcNow);
            await repository.SaveCrashGuardConfigurationAsync(configuration, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "maintenance.crash-guard.configure",
                "success",
                EndpointHelpers.RemoteIp(context),
                configuration.Enabled ? "已启用并更新崩溃守护配置。" : "已禁用并更新崩溃守护配置。",
                new
                {
                    configuration.Enabled,
                    configuration.MaximumCrashes,
                    configuration.WindowMinutes,
                    configuration.RestartDelaySeconds,
                    configuration.OperationTimeoutSeconds
                });
            return Results.Ok(Response(configuration, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/crash-guard/suspend", async (
            MaintenanceReasonRequest request,
            IMaintenanceRepository repository,
            IPalOpsEventPublisher publisher,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var reason = MaintenanceValidator.ValidateReason(request.Reason);
            var state = await repository.GetCrashGuardStateAsync(cancellationToken);
            state = state with { Suspended = true, LastMessage = "崩溃守护已暂停：" + reason };
            await repository.SaveCrashGuardStateAsync(state, cancellationToken);
            await PublishStateEventAsync(publisher, "maintenance.crash-guard.suspended", "warning", state.LastMessage, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "maintenance.crash-guard.suspend",
                "success",
                EndpointHelpers.RemoteIp(context),
                state.LastMessage,
                new { reason, user = User(context) });
            return Results.Ok(Response(state, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/crash-guard/resume", async (
            MaintenanceReasonRequest request,
            IMaintenanceRepository repository,
            IPalOpsEventPublisher publisher,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var reason = MaintenanceValidator.ValidateReason(request.Reason);
            var state = await repository.GetCrashGuardStateAsync(cancellationToken);
            state = state with { Suspended = false, LastMessage = "崩溃守护已恢复：" + reason };
            await repository.SaveCrashGuardStateAsync(state, cancellationToken);
            await PublishStateEventAsync(publisher, "maintenance.crash-guard.resumed", "information", state.LastMessage, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "maintenance.crash-guard.resume",
                "success",
                EndpointHelpers.RemoteIp(context),
                state.LastMessage,
                new { reason, user = User(context) });
            return Results.Ok(Response(state, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/crash-guard/reset", async (
            CrashGuardResetRequest request,
            IMaintenanceRepository repository,
            IPalOpsEventPublisher publisher,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            MaintenanceValidator.RequireResetConfirmation(request.Confirmation);
            var reason = MaintenanceValidator.ValidateReason(request.Reason);
            var now = DateTimeOffset.UtcNow;
            var state = await repository.GetCrashGuardStateAsync(cancellationToken);
            state = state with
            {
                CircuitOpen = false,
                CircuitOpenedAt = null,
                LastResetAt = now,
                LastMessage = "崩溃守护熔断器已重置：" + reason
            };
            await repository.SaveCrashGuardStateAsync(state, cancellationToken);
            await repository.AppendCrashEventAsync(new MaintenanceCrashEvent(
                Guid.NewGuid().ToString("N"),
                "circuit-reset",
                now,
                null,
                "success",
                state.LastMessage,
                null,
                null), cancellationToken);
            await PublishStateEventAsync(publisher, "maintenance.crash-guard.reset", "information", state.LastMessage, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "maintenance.crash-guard.reset",
                "success",
                EndpointHelpers.RemoteIp(context),
                state.LastMessage,
                new { reason, user = User(context) });
            return Results.Ok(Response(state, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }

    private static async Task PublishStateEventAsync(
        IPalOpsEventPublisher publisher,
        string eventType,
        string severity,
        string message,
        CancellationToken cancellationToken)
    {
        await publisher.PublishAsync(PalOpsEvent.Create(
            eventType,
            severity,
            metadata: new Dictionary<string, object?> { ["message"] = message }), cancellationToken);
    }

    private static ApiResponse<T> Response<T>(T data, HttpContext context) =>
        new(data, context.TraceIdentifier, []);

    private static string User(HttpContext context) => context.User.Identity?.Name ?? "unknown";
}
