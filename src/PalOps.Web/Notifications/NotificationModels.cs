using PalOps.Web.Events;

namespace PalOps.Web.Notifications;

public static class WebhookProviderTypes
{
    public const string GenericJson = "generic-json";
    public const string WeCom = "wecom";
    public const string DingTalk = "dingtalk";
    public const string Feishu = "feishu";
    public const string Discord = "discord";
    public const string Slack = "slack";
    public const string Telegram = "telegram";
}

public sealed record WebhookProviderDefinition(
    string Type,
    string DisplayName,
    bool SupportsTitle,
    bool SupportsPayloadTemplate,
    IReadOnlyList<string> SecretFields,
    string DefaultTitleTemplate,
    string DefaultBodyTemplate,
    string DefaultPayloadTemplate);

public sealed record WebhookChannel(
    string Id,
    string Name,
    string ProviderType,
    bool Enabled,
    string Url,
    string HttpMethod,
    IReadOnlyDictionary<string, string> Headers,
    string ProtectedSecretJson,
    bool AllowPrivateNetwork,
    int TimeoutSeconds,
    int MaximumRetries,
    IReadOnlyList<string> EventTypes,
    string TitleTemplate,
    string BodyTemplate,
    string PayloadTemplate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WebhookChannelV1(
    string Id,
    string Name,
    string ProviderType,
    bool Enabled,
    string Url,
    string HttpMethod,
    IReadOnlyDictionary<string, string> Headers,
    bool SecretConfigured,
    bool AllowPrivateNetwork,
    int TimeoutSeconds,
    int MaximumRetries,
    IReadOnlyList<string> EventTypes,
    string TitleTemplate,
    string BodyTemplate,
    string PayloadTemplate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WebhookChannelWriteRequest(
    string Name,
    string ProviderType,
    bool Enabled,
    string Url,
    string? HttpMethod,
    IReadOnlyDictionary<string, string>? Headers,
    IReadOnlyDictionary<string, string>? Secrets,
    bool ClearSecrets,
    bool AllowPrivateNetwork,
    int TimeoutSeconds,
    int MaximumRetries,
    IReadOnlyList<string> EventTypes,
    string? TitleTemplate,
    string? BodyTemplate,
    string? PayloadTemplate);

public sealed record WebhookDeliveryRecord(
    string Id,
    string EventId,
    string EventType,
    string ChannelId,
    string ChannelName,
    string ProviderType,
    string Status,
    int Attempt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    int? HttpStatusCode,
    long DurationMs,
    string RequestSummary,
    string ResponseSummary,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? NextAttemptAt);

public sealed record WebhookHistoryQueryResult(
    IReadOnlyList<WebhookDeliveryRecord> Items,
    bool HasMore);

public sealed record RenderedWebhookMessage(
    string Title,
    string Body,
    string PayloadJson,
    IReadOnlyList<string> UsedVariables);

public sealed record WebhookRequestDefinition(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string ContentType,
    string Body);

public sealed record WebhookDeliveryWorkItem(
    PalOpsEvent Event,
    string ChannelId,
    int Attempt,
    DateTimeOffset DueAt);

public sealed record WebhookTemplatePreviewRequest(
    string ProviderType,
    string EventType,
    string? TitleTemplate,
    string? BodyTemplate,
    string? PayloadTemplate,
    IReadOnlyDictionary<string, string>? Secrets);

public sealed record WebhookTestResult(
    bool Success,
    string Status,
    int? HttpStatusCode,
    long DurationMs,
    string ResponseSummary,
    string? ErrorCode,
    string? ErrorMessage);
