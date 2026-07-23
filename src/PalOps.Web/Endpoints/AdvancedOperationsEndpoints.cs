using Microsoft.AspNetCore.RateLimiting;
using PalOps.Web.AdvancedOperations;
using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.Health;
using PalOps.Web.Security;
using PalOps.Web.Versioning;

namespace PalOps.Web.Endpoints;

public static class AdvancedOperationsEndpoints
{
    public static IEndpointRouteBuilder MapAdvancedOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/advanced-operations")
            .RequireAuthorization()
            .WithTags("Advanced Operations");

        MapReadiness(group);
        MapDiagnostics(group);
        MapIncidents(group);
        MapPlayerInsights(group);
        MapWorldGovernance(group);
        MapDisasterRecovery(group);
        MapUpdates(group);
        MapConfigurationVersions(group);
        MapPlaybooks(group);
        MapSecurity(group);
        MapIntegrations(group);
        MapExternalIntegrations(endpoints);
        return endpoints;
    }


    private static void MapReadiness(RouteGroupBuilder group)
    {
        group.MapGet("/readiness/{module}", async (
            string module,
            IAdvancedOperationsReadinessService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Ok(await service.GetAsync(module, cancellationToken), context));
    }

    private static void MapDiagnostics(RouteGroupBuilder group)
    {
        group.MapGet("/diagnostics", async (IDiagnosticCenterService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.RunAsync(cancellationToken), context));

        group.MapPost("/diagnostics/run", async (
            IDiagnosticCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var report = await service.RunAsync(cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.diagnostics.run", "success", context, "已运行诊断中心体检。", new { report.OverallStatus, report.HealthyCount, report.WarningCount, report.CriticalCount });
            return Ok(report, context);
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/diagnostics/support-bundle", async (
            IDiagnosticCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var bundle = await service.CreateSupportBundleAsync(cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.diagnostics.support-bundle", "success", context, $"已生成诊断支持包 {bundle.FileName}。", new { bundle.FileName, bundle.SizeBytes });
            return Results.File(bundle.FullPath, "application/zip", bundle.FileName, enableRangeProcessing: true);
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();
    }

    private static void MapIncidents(RouteGroupBuilder group)
    {
        group.MapGet("/incidents", async (IIncidentCenterService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.GetDashboardAsync(cancellationToken), context));

        group.MapPost("/incidents", async (
            IncidentCreateRequest request,
            IIncidentCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var incident = await service.CreateAsync(request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.incident.create", "success", context, $"已创建故障 {incident.Title}。", new { incident.Id, incident.Severity, incident.Source });
            return Results.Created($"/api/v1/advanced-operations/incidents/{incident.Id}", new ApiResponse<IncidentRecord>(incident, context.TraceIdentifier, []));
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/incidents/{id}/actions", async (
            string id,
            IncidentActionRequest request,
            IIncidentCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var incident = await service.ApplyActionAsync(id, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.incident.action", "success", context, $"已对故障 {id} 执行 {request.Action}。", new { id, request.Action, incident.Status, incident.Assignee });
            return Ok(incident, context);
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/incidents/rules/{id?}", async (
            string? id,
            IncidentRuleWriteRequest request,
            IIncidentCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var rule = await service.UpsertRuleAsync(id, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.incident-rule.save", "success", context, $"已保存故障规则 {rule.Name}。", new { rule.Id, rule.Enabled, rule.SourceCode, rule.Severity });
            return Ok(rule, context);
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/incidents/rules/{id}", async (
            string id,
            IIncidentCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteRuleAsync(id, cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.incident-rule.delete", "success", context, $"已删除故障规则 {id}。", new { id });
            return Results.NoContent();
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/incidents/evaluate-health", async (
            IIncidentCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var changed = await service.EvaluateHealthAsync(cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.incident.evaluate", "success", context, $"已执行健康规则评估，变更 {changed} 项。", new { changed });
            return Ok(new { changed }, context);
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();
    }

    private static void MapPlayerInsights(RouteGroupBuilder group)
    {
        group.MapGet("/player-insights", async (IPlayerInsightsService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.GetDashboardAsync(cancellationToken), context));

        group.MapPut("/player-insights/{playerKey}/note", async (
            string playerKey,
            PlayerInsightNoteWriteRequest request,
            IPlayerInsightsService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var note = await service.UpdateNoteAsync(playerKey, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.player-insight.note", "success", context, $"已更新玩家 {playerKey} 的洞察备注。", new { playerKey, note.UpdatedAt });
            return Ok(note, context);
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();
    }

    private static void MapWorldGovernance(RouteGroupBuilder group)
    {
        group.MapGet("/world-governance", async (IWorldGovernanceService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.GetDashboardAsync(cancellationToken), context));

        group.MapPut("/world-governance/{candidateId}/review", async (
            string candidateId,
            GovernanceReviewWriteRequest request,
            IWorldGovernanceService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var review = await service.ReviewAsync(candidateId, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.world-governance.review", "success", context, $"已复核世界治理候选项 {candidateId}。", new { candidateId, review.Status });
            return Ok(review, context);
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();
    }

    private static void MapDisasterRecovery(RouteGroupBuilder group)
    {
        group.MapGet("/disaster-recovery", async (IDisasterRecoveryService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.GetDashboardAsync(cancellationToken), context));

        group.MapPost("/disaster-recovery/targets", async (
            DisasterRecoveryTargetWriteRequest request,
            IDisasterRecoveryService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var target = await service.UpsertTargetAsync(null, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.disaster-recovery.target.create", "success", context, $"已创建灾备目标 {target.Name}。", new { target.Id, target.TargetType, target.Endpoint });
            return Results.Created($"/api/v1/advanced-operations/disaster-recovery/targets/{target.Id}", new ApiResponse<DisasterRecoveryTarget>(target, context.TraceIdentifier, []));
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/disaster-recovery/targets/{id}", async (
            string id,
            DisasterRecoveryTargetWriteRequest request,
            IDisasterRecoveryService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var target = await service.UpsertTargetAsync(id, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.disaster-recovery.target.update", "success", context, $"已更新灾备目标 {target.Name}。", new { target.Id, target.Enabled });
            return Ok(target, context);
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/disaster-recovery/targets/{id}/validate", async (
            string id,
            IDisasterRecoveryService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var target = await service.ValidateTargetAsync(id, cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.disaster-recovery.target.validate", target.LastValidationStatus, context, $"灾备目标 {target.Name} 验证结果：{target.LastValidationStatus}。", new { target.Id, target.LastValidationStatus, target.LastValidationMessage });
            return Ok(target, context);
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/disaster-recovery/targets/{id}/drills", async (
            string id,
            DisasterRecoveryDrillRequest request,
            IDisasterRecoveryService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var drill = await service.RunDrillAsync(id, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.disaster-recovery.drill", drill.Status, context, $"灾备演练 {drill.Id}：{drill.Status}。", new { drill.Id, drill.TargetId, drill.BackupId, drill.Status });
            return Ok(drill, context);
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/disaster-recovery/targets/{id}", async (
            string id,
            IDisasterRecoveryService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteTargetAsync(id, cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.disaster-recovery.target.delete", "success", context, $"已删除灾备目标 {id}。", new { id });
            return Results.NoContent();
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();
    }

    private static void MapUpdates(RouteGroupBuilder group)
    {
        group.MapGet("/updates", async (bool? force, IUpdateCenterService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.GetDashboardAsync(force == true, cancellationToken), context));

        group.MapPost("/updates/preflight", async (
            IUpdateCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var preflight = await service.RunPreflightAsync(cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.update.preflight", preflight.Status, context, $"更新预检结果：{preflight.Status}。", preflight);
            return Ok(preflight, context);
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/updates/plans", async (
            UpdatePlanWriteRequest request,
            IUpdateCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var plan = await service.UpsertPlanAsync(null, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.update-plan.create", "success", context, $"已创建更新计划 {plan.Name}。", new { plan.Id, plan.TargetComponent, plan.TargetVersion });
            return Results.Created($"/api/v1/advanced-operations/updates/plans/{plan.Id}", new ApiResponse<UpdatePlanRecord>(plan, context.TraceIdentifier, []));
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/updates/plans/{id}", async (
            string id,
            UpdatePlanWriteRequest request,
            IUpdateCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var plan = await service.UpsertPlanAsync(id, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.update-plan.update", "success", context, $"已更新计划 {plan.Name}。", new { plan.Id, plan.TargetVersion });
            return Ok(plan, context);
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/updates/plans/{id}/approve", async (
            string id,
            UpdatePlanExecuteRequest request,
            IUpdateCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var plan = await service.ApprovePlanAsync(id, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.update-plan.approve", plan.Status, context, $"更新计划 {plan.Name} 审批结果：{plan.Status}。", new { plan.Id, plan.Status, plan.TargetComponent, plan.TargetVersion });
            return Ok(plan, context);
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/updates/plans/{id}", async (
            string id,
            IUpdateCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.DeletePlanAsync(id, cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.update-plan.delete", "success", context, $"已删除更新计划 {id}。", new { id });
            return Results.NoContent();
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();
    }

    private static void MapConfigurationVersions(RouteGroupBuilder group)
    {
        group.MapGet("/configuration-versions", async (IConfigurationVersionService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.GetDashboardAsync(cancellationToken), context));

        group.MapGet("/configuration-versions/{fromId}/diff/{toId}", async (string fromId, string toId, IConfigurationVersionService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.DiffAsync(fromId, toId, cancellationToken), context));

        group.MapPost("/configuration-versions", async (
            ConfigurationVersionCreateRequest request,
            IConfigurationVersionService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await service.CaptureAsync(request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.configuration-version.create", "success", context, $"已创建配置版本 {snapshot.Name}。", new { snapshot.Id, snapshot.Sha256, snapshot.SizeBytes });
            return Results.Created($"/api/v1/advanced-operations/configuration-versions/{snapshot.Id}", new ApiResponse<ConfigurationVersionSnapshot>(snapshot, context.TraceIdentifier, []));
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/configuration-versions/{id}/restore", async (
            string id,
            ConfigurationVersionRestoreRequest request,
            IConfigurationVersionService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await service.RestoreAsync(id, request, Actor(context), EndpointHelpers.RemoteIp(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.configuration-version.restore", "success", context, $"已恢复配置版本 {snapshot.Name}。", new { snapshot.Id, snapshot.Sha256, request.Restart });
            return Ok(snapshot, context);
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/configuration-versions/{id}", async (
            string id,
            IConfigurationVersionService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.configuration-version.delete", "success", context, $"已删除配置版本 {id}。", new { id });
            return Results.NoContent();
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();
    }

    private static void MapPlaybooks(RouteGroupBuilder group)
    {
        group.MapGet("/playbooks", async (IOperationsPlaybookService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.GetDashboardAsync(cancellationToken), context));

        group.MapPost("/playbooks", async (
            OperationsPlaybookWriteRequest request,
            IOperationsPlaybookService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var playbook = await service.UpsertAsync(null, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.playbook.create", "success", context, $"已创建运维剧本 {playbook.Name}。", new { playbook.Id, playbook.Enabled, StepCount = playbook.Steps.Count });
            return Results.Created($"/api/v1/advanced-operations/playbooks/{playbook.Id}", new ApiResponse<OperationsPlaybook>(playbook, context.TraceIdentifier, []));
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/playbooks/{id}", async (
            string id,
            OperationsPlaybookWriteRequest request,
            IOperationsPlaybookService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var playbook = await service.UpsertAsync(id, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.playbook.update", "success", context, $"已更新运维剧本 {playbook.Name}。", new { playbook.Id, playbook.Enabled, StepCount = playbook.Steps.Count });
            return Ok(playbook, context);
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/playbooks/{id}/run", async (
            string id,
            OperationsPlaybookRunRequest request,
            IOperationsPlaybookService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var run = await service.RunAsync(id, request, Actor(context), EndpointHelpers.RemoteIp(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.playbook.run", run.Status, context, $"运维剧本 {run.PlaybookName} 执行结果：{run.Status}。", new { run.Id, run.PlaybookId, run.Status, StepCount = run.Steps.Count });
            return Ok(run, context);
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/playbooks/{id}", async (
            string id,
            IOperationsPlaybookService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.playbook.delete", "success", context, $"已删除运维剧本 {id}。", new { id });
            return Results.NoContent();
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();
    }

    private static void MapSecurity(RouteGroupBuilder group)
    {
        group.MapGet("/security", async (ISecurityCenterService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.GetDashboardAsync(cancellationToken), context))
            .RequireAuthorization("OwnerOnly");

        group.MapPut("/security/policy", async (
            SecurityPolicyWriteRequest request,
            ISecurityCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var policy = await service.UpdatePolicyAsync(request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.security.policy", "success", context, "已更新安全中心策略。", policy);
            return Ok(policy, context);
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/security/tokens", async (
            ApiTokenCreateRequest request,
            ISecurityCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var created = await service.CreateTokenAsync(request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.security.token.create", "success", context, $"已创建 API Token {created.Token.Prefix}。", new { created.Token.Id, created.Token.Prefix, created.Token.Scopes, created.Token.ExpiresAt });
            return Results.Created($"/api/v1/advanced-operations/security/tokens/{created.Token.Id}", new ApiResponse<ApiTokenCreationResult>(created, context.TraceIdentifier, []));
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/security/tokens/{id}/revoke", async (
            string id,
            ApiTokenRevokeRequest request,
            ISecurityCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var token = await service.RevokeTokenAsync(id, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.security.token.revoke", "success", context, $"已吊销 API Token {token.Prefix}。", new { token.Id, token.Prefix, token.RevokedAt });
            return Ok(token, context);
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();
    }

    private static void MapIntegrations(RouteGroupBuilder group)
    {
        group.MapGet("/integrations", async (IIntegrationCenterService service, HttpContext context, CancellationToken cancellationToken) =>
            Ok(await service.GetDashboardAsync(cancellationToken), context))
            .RequireAuthorization("Administrator");

        group.MapPost("/integrations/subscriptions", async (
            IntegrationSubscriptionWriteRequest request,
            IIntegrationCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var subscription = await service.UpsertSubscriptionAsync(null, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.integration.subscription.create", "success", context, $"已创建集成订阅 {subscription.Name}。", new { subscription.Id, subscription.EventTypes, subscription.Destination });
            return Results.Created($"/api/v1/advanced-operations/integrations/subscriptions/{subscription.Id}", new ApiResponse<IntegrationSubscription>(subscription, context.TraceIdentifier, []));
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/integrations/subscriptions/{id}", async (
            string id,
            IntegrationSubscriptionWriteRequest request,
            IIntegrationCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var subscription = await service.UpsertSubscriptionAsync(id, request, Actor(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.integration.subscription.update", "success", context, $"已更新集成订阅 {subscription.Name}。", new { subscription.Id, subscription.Enabled, subscription.EventTypes });
            return Ok(subscription, context);
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/integrations/subscriptions/{id}", async (
            string id,
            IIntegrationCenterService service,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteSubscriptionAsync(id, cancellationToken);
            await AuditAsync(audit, loggerFactory, "advanced.integration.subscription.delete", "success", context, $"已删除集成订阅 {id}。", new { id });
            return Results.NoContent();
        }).RequireAuthorization("OwnerOnly").AddEndpointFilter<CsrfValidationFilter>();
    }

    private static void MapExternalIntegrations(IEndpointRouteBuilder endpoints)
    {
        var integration = endpoints.MapGroup("/api/v1/integrations")
            .WithTags("Integrations")
            .RequireRateLimiting("integration");

        integration.MapGet("/status", async (
            ISystemHealthService health,
            IApplicationVersionProvider versionProvider,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await health.RefreshAsync(cancellationToken);
            var data = new
            {
                status = health.Components.Any(static item => !item.Status.Equals("healthy", StringComparison.OrdinalIgnoreCase)) ? "degraded" : "ok",
                version = versionProvider.Get().CurrentVersion,
                components = health.Components,
                timestamp = DateTimeOffset.UtcNow
            };
            return Ok(data, context);
        });

        integration.MapGet("/incidents", async (
            IIncidentCenterService incidents,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Ok(await incidents.GetDashboardAsync(cancellationToken), context));

        integration.MapGet("/diagnostics", async (
            IDiagnosticCenterService diagnostics,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Ok(await diagnostics.RunAsync(cancellationToken), context));

        integration.MapPost("/events", async (
            IntegrationEventRequest request,
            IIntegrationCenterService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var validation = context.Items[IntegrationApiTokenMiddleware.ValidationItemKey] as ApiTokenValidationResult
                             ?? throw new UnauthorizedAccessException();
            var record = await service.IngestEventAsync(request, validation.Prefix, cancellationToken);
            return Results.Accepted($"/api/v1/advanced-operations/integrations", new ApiResponse<IntegrationEventRecord>(record, context.TraceIdentifier, []));
        });
    }

    private static IResult Ok<T>(T data, HttpContext context) =>
        Results.Ok(new ApiResponse<T>(data, context.TraceIdentifier, []));

    private static string Actor(HttpContext context) =>
        string.IsNullOrWhiteSpace(context.User.Identity?.Name) ? "unknown" : context.User.Identity.Name;

    private static Task AuditAsync(
        IAuditLogService audit,
        ILoggerFactory loggerFactory,
        string eventType,
        string outcome,
        HttpContext context,
        string summary,
        object? data) => audit.WriteBestEffortAsync(
            loggerFactory.CreateLogger("PalOps.Audit"),
            eventType,
            outcome,
            EndpointHelpers.RemoteIp(context),
            summary,
            data);
}
