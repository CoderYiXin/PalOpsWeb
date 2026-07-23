using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.Events;
using PalOps.Web.Notifications;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class NotificationEndpoints
{
    private static readonly NotificationEventDefinition[] EventDefinitions =
    [
        new("player.online", "玩家上线", "玩家事件"),
        new("player.offline", "玩家下线", "玩家事件"),
        new("player.kicked", "玩家踢出", "玩家事件"),
        new("server.started", "启动成功", "服务器事件"),
        new("server.stopped", "正常停止", "服务器事件"),
        new("server.restarted", "重启成功", "服务器事件"),
        new("server.stop-timeout", "停止超时", "服务器事件"),
        new("server.exited-unexpectedly", "异常退出", "服务器事件"),
        new("server.force-stopped", "强制停止", "服务器事件"),
        new("server.operation.failed", "操作失败", "服务器事件"),
        new("backup.completed", "备份完成", "存档事件"),
        new("backup.failed", "备份失败", "存档事件"),
        new("backup.verified", "备份校验", "存档事件"),
        new("backup.restored", "备份恢复", "存档事件"),
        new("backup.deleted", "备份删除", "存档事件"),
        new("save-index.completed", "解析完成", "存档事件"),
        new("save-index.failed", "解析失败", "存档事件"),
        new("grant.completed", "发放完成", "管理事件"),
        new("grant.failed", "发放失败", "管理事件"),
        new("paldefender.update-available", "防护更新", "管理事件"),
        new("palops.update-available", "平台更新", "管理事件"),
        new("maintenance.completed", "维护完成", "维护事件"),
        new("maintenance.failed", "维护失败", "维护事件"),
        new("maintenance.cancelled", "维护取消", "维护事件"),
        new("maintenance.interrupted", "维护中断", "维护事件"),
        new("maintenance.crash-guard.circuit-opened", "崩溃守护熔断", "维护事件"),
        new("maintenance.crash-guard.restarting", "崩溃守护正在重启", "维护事件"),
        new("maintenance.crash-guard.recovered", "崩溃守护恢复成功", "维护事件"),
        new("maintenance.crash-guard.failed", "崩溃守护恢复失败", "维护事件"),
        new("webhook.delivery.failed", "推送失败", "管理事件"),
        new("system.cpu.high", "CPU过高", "系统告警"),
        new("system.cpu.recovered", "CPU恢复", "系统告警"),
        new("system.memory.high", "内存过高", "系统告警"),
        new("system.memory.recovered", "内存恢复", "系统告警"),
        new("system.disk.low", "磁盘不足", "系统告警"),
        new("system.disk.recovered", "磁盘恢复", "系统告警"),
        new("component.unhealthy", "组件异常", "系统告警"),
        new("component.recovered", "组件恢复", "系统告警")
    ];

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications")
            .RequireAuthorization("Auditor")
            .WithTags("Notifications");

        group.MapGet("/providers", (
            IWebhookProviderRegistry providers,
            HttpContext context) =>
            Results.Ok(Response(providers.GetDefinitions(), context)));

        group.MapGet("/events", (HttpContext context) =>
            Results.Ok(Response<IReadOnlyList<NotificationEventDefinition>>(EventDefinitions, context)));

        group.MapGet("/channels", async (
            IWebhookChannelStore store,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await store.ListAsync(cancellationToken), context)));

        group.MapGet("/channels/{id}", async (
            string id,
            IWebhookChannelStore store,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Response(await store.GetAsync(id, cancellationToken), context)));

        group.MapPost("/channels", async (
            WebhookChannelWriteRequest request,
            IWebhookChannelStore store,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var saved = await store.CreateAsync(request, User(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, context, "notification.channel.create", "已创建消息通知渠道。", saved);
            return Results.Created($"/api/v1/notifications/channels/{saved.Id}", Response(saved, context));
        })
            .RequireAuthorization("OwnerOnly")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/channels/{id}", async (
            string id,
            WebhookChannelWriteRequest request,
            IWebhookChannelStore store,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var saved = await store.UpdateAsync(id, request, User(context), cancellationToken);
            await AuditAsync(audit, loggerFactory, context, "notification.channel.update", "已更新消息通知渠道。", saved);
            return Results.Ok(Response(saved, context));
        })
            .RequireAuthorization("OwnerOnly")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/channels/{id}", async (
            string id,
            IWebhookChannelStore store,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var existing = await store.GetAsync(id, cancellationToken);
            await store.DeleteAsync(id, cancellationToken);
            await AuditAsync(audit, loggerFactory, context, "notification.channel.delete", "已删除消息通知渠道。", existing);
            return Results.NoContent();
        })
            .RequireAuthorization("OwnerOnly")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/channels/{id}/test", async (
            string id,
            IWebhookChannelStore store,
            IWebhookDeliveryService delivery,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var channel = await store.GetInternalAsync(id, cancellationToken);
            var palOpsEvent = CreateSampleEvent("notification.test", true);
            var result = await delivery.DeliverTestAsync(channel, palOpsEvent, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "notification.channel.test",
                result.Success ? "success" : "failed",
                EndpointHelpers.RemoteIp(context),
                result.Success ? "消息通知测试发送成功。" : "消息通知测试发送失败。",
                new { channel.Id, channel.Name, channel.ProviderType, channel.Enabled, result.Status, result.HttpStatusCode, result.ErrorCode });
            return Results.Ok(Response(result, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/templates/preview", (
            WebhookTemplatePreviewRequest request,
            IWebhookProviderRegistry providers,
            IWebhookTemplateRenderer renderer,
            HttpContext context) =>
        {
            var provider = providers.Get(request.ProviderType);
            var definition = provider.Definition;
            var now = DateTimeOffset.UtcNow;
            var channel = new WebhookChannel(
                "preview",
                "模板预览",
                definition.Type,
                true,
                "https://example.invalid/webhook",
                "POST",
                new Dictionary<string, string>(),
                string.Empty,
                false,
                10,
                0,
                [request.EventType],
                request.TitleTemplate ?? definition.DefaultTitleTemplate,
                request.BodyTemplate ?? definition.DefaultBodyTemplate,
                request.PayloadTemplate ?? definition.DefaultPayloadTemplate,
                now,
                now);
            var rendered = renderer.Render(
                channel,
                CreateSampleEvent(request.EventType, false),
                request.Secrets ?? new Dictionary<string, string>());
            return Results.Ok(Response(rendered, context));
        })
            .RequireAuthorization("Administrator")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/history", async (
            int? page,
            int? pageSize,
            string? status,
            string? channelId,
            string? eventType,
            IWebhookHistoryStore history,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var normalizedPage = Math.Max(1, page ?? 1);
            var normalizedSize = Math.Clamp(pageSize ?? 50, 1, 200);
            var queryResult = await history.ListAsync(normalizedPage, normalizedSize, status, channelId, eventType, cancellationToken);
            return Results.Ok(Response(new NotificationHistoryPage(queryResult.Items, normalizedPage, normalizedSize, queryResult.HasMore), context));
        });

        return endpoints;
    }

    private static PalOpsEvent CreateSampleEvent(string eventType, bool isTest) => PalOpsEvent.Create(
        string.IsNullOrWhiteSpace(eventType) ? "notification.test" : eventType,
        server: new Dictionary<string, object?>
        {
            ["name"] = "Palworld Server",
            ["state"] = "running",
            ["processId"] = 12884,
            ["operationId"] = "sample-operation"
        },
        player: new Dictionary<string, object?>
        {
            ["name"] = "SamplePlayer",
            ["uid"] = "00000000000000000000000000000000",
            ["userId"] = "steam-sample",
            ["guildName"] = "Sample Guild"
        },
        backup: new Dictionary<string, object?>
        {
            ["fileName"] = "sample-world.zip",
            ["size"] = 1048576,
            ["worldId"] = "SampleWorld",
            ["note"] = "测试消息"
        },
        system: new Dictionary<string, object?>
        {
            ["cpuPercent"] = 42.5,
            ["memoryPercent"] = 56.3,
            ["diskFreeBytes"] = 107374182400L
        },
        metadata: new Dictionary<string, object?>
        {
            ["message"] = isTest ? "这是一条 PalOps 测试通知。" : "模板预览消息。",
            ["isTest"] = isTest,
            ["currentVersion"] = "1.0.0",
            ["latestVersion"] = "1.3.1",
            ["releaseUrl"] = "https://github.com/Ultimeit/PalDefender/releases"
        });

    private static async Task AuditAsync(
        IAuditLogService audit,
        ILoggerFactory loggerFactory,
        HttpContext context,
        string eventType,
        string summary,
        WebhookChannelV1 channel) =>
        await audit.WriteBestEffortAsync(
            loggerFactory.CreateLogger("PalOps.Audit"),
            eventType,
            "success",
            EndpointHelpers.RemoteIp(context),
            summary,
            new { channel.Id, channel.Name, channel.ProviderType, channel.Enabled });

    private static ApiResponse<T> Response<T>(T data, HttpContext context) =>
        new(data, context.TraceIdentifier, []);

    private static string User(HttpContext context) => context.User.Identity?.Name ?? "unknown";

    public sealed record NotificationEventDefinition(string EventType, string DisplayName, string Group);
    public sealed record NotificationHistoryPage(
        IReadOnlyList<WebhookDeliveryRecord> Items,
        int Page,
        int PageSize,
        bool HasMore);
}
