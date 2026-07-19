using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.Events;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Notifications;

public interface IWebhookChannelStore
{
    Task<IReadOnlyList<WebhookChannel>> ListInternalAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookChannelV1>> ListAsync(CancellationToken cancellationToken = default);
    Task<WebhookChannelV1> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<WebhookChannel> GetInternalAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetSecretsAsync(WebhookChannel channel, CancellationToken cancellationToken = default);
    Task<WebhookChannelV1> CreateAsync(WebhookChannelWriteRequest request, string userName, CancellationToken cancellationToken = default);
    Task<WebhookChannelV1> UpdateAsync(string id, WebhookChannelWriteRequest request, string userName, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class WebhookChannelStore : IWebhookChannelStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string RedactedValue = "[REDACTED]";
    private static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
    { "Host", "Content-Length", "Connection", "Transfer-Encoding", "Cookie", "Proxy-Authorization" };
    private static readonly string[] SensitiveHeaderFragments = ["authorization", "token", "secret", "signature", "sign", "api-key", "apikey", "cookie"];

    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly IWebhookProviderRegistry _providers;
    private readonly IWebhookDestinationValidator _destinationValidator;
    private readonly IWebhookTemplateRenderer _renderer;
    private readonly ILogger<WebhookChannelStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WebhookChannelStore(
        IHostEnvironment environment,
        IOptions<AppRuntimeOptions> options,
        IDataProtectionProvider protectionProvider,
        IWebhookProviderRegistry providers,
        IWebhookDestinationValidator destinationValidator,
        IWebhookTemplateRenderer renderer,
        ILogger<WebhookChannelStore> logger)
    {
        var data = Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(data);
        _path = Path.Combine(data, "webhook-channels.json");
        _protector = protectionProvider.CreateProtector("PalOps.Web.WebhookChannels.v1");
        _providers = providers;
        _destinationValidator = destinationValidator;
        _renderer = renderer;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebhookChannel>> ListInternalAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await LoadUnsafeAsync(cancellationToken)).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<WebhookChannelV1>> ListAsync(CancellationToken cancellationToken = default) =>
        (await ListInternalAsync(cancellationToken)).Select(ToContract).ToArray();

    public async Task<WebhookChannelV1> GetAsync(string id, CancellationToken cancellationToken = default) =>
        ToContract(await GetInternalAsync(id, cancellationToken));

    public async Task<WebhookChannel> GetInternalAsync(string id, CancellationToken cancellationToken = default)
    {
        var channels = await ListInternalAsync(cancellationToken);
        return channels.FirstOrDefault(channel => channel.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("未找到指定的 Webhook 渠道。");
    }

    public Task<IReadOnlyDictionary<string, string>> GetSecretsAsync(
        WebhookChannel channel,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyDictionary<string, string>>(Unprotect(channel.ProtectedSecretJson));
    }

    public async Task<WebhookChannelV1> CreateAsync(
        WebhookChannelWriteRequest request,
        string userName,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var secrets = NormalizeSecrets(request.Secrets);
        var protectedSecrets = Protect(secrets);
        var channel = await BuildAndValidateAsync(
            Guid.NewGuid().ToString("N"), request, protectedSecrets, now, now, cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channels = await LoadUnsafeAsync(cancellationToken);
            channels.Add(channel);
            await SaveUnsafeAsync(channels, cancellationToken);
        }
        finally { _gate.Release(); }
        return ToContract(channel);
    }

    public async Task<WebhookChannelV1> UpdateAsync(
        string id,
        WebhookChannelWriteRequest request,
        string userName,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channels = await LoadUnsafeAsync(cancellationToken);
            var index = channels.FindIndex(channel => channel.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new KeyNotFoundException("未找到指定的 Webhook 渠道。");
            var existing = channels[index];
            var effectiveRequest = request with
            {
                Url = RestoreRedactedUrl(request.Url, existing.Url),
                Headers = RestoreRedactedHeaders(request.Headers, existing.Headers)
            };
            var suppliedSecrets = NormalizeSecrets(effectiveRequest.Secrets);
            var protectedSecrets = effectiveRequest.ClearSecrets
                ? Protect(new Dictionary<string, string>())
                : suppliedSecrets.Count > 0
                    ? Protect(suppliedSecrets)
                    : existing.ProtectedSecretJson;
            var channel = await BuildAndValidateAsync(
                existing.Id, effectiveRequest, protectedSecrets, existing.CreatedAt, DateTimeOffset.UtcNow, cancellationToken);
            channels[index] = channel;
            await SaveUnsafeAsync(channels, cancellationToken);
            return ToContract(channel);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channels = await LoadUnsafeAsync(cancellationToken);
            if (channels.RemoveAll(channel => channel.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0)
                throw new KeyNotFoundException("未找到指定的 Webhook 渠道。");
            await SaveUnsafeAsync(channels, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<WebhookChannel> BuildAndValidateAsync(
        string id,
        WebhookChannelWriteRequest request,
        string protectedSecrets,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100)
            throw new PalOpsApiException(422, "WEBHOOK_CHANNEL_INVALID", "渠道名称长度必须为 1 到 100 个字符。");
        var provider = _providers.Get(request.ProviderType);
        var method = string.IsNullOrWhiteSpace(request.HttpMethod) ? "POST" : request.HttpMethod.Trim().ToUpperInvariant();
        if (provider.Type != WebhookProviderTypes.GenericJson) method = "POST";
        if (method is not ("POST" or "PUT" or "PATCH"))
            throw new PalOpsApiException(422, "WEBHOOK_CHANNEL_INVALID", "通用 Webhook 方法只能为 POST、PUT 或 PATCH。");
        if (request.TimeoutSeconds is < 3 or > 30)
            throw new PalOpsApiException(422, "WEBHOOK_CHANNEL_INVALID", "超时时间必须为 3 到 30 秒。");
        if (request.MaximumRetries is < 0 or > 3)
            throw new PalOpsApiException(422, "WEBHOOK_CHANNEL_INVALID", "重试次数必须为 0 到 3。");

        var headers = NormalizeHeaders(request.Headers);
        var eventTypes = (request.EventTypes ?? [])
            .Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (eventTypes.Length > 100)
            throw new PalOpsApiException(422, "WEBHOOK_CHANNEL_INVALID", "单个渠道最多订阅 100 个事件类型。");

        var url = request.Url?.Trim() ?? string.Empty;
        if (url.Contains(RedactedValue, StringComparison.OrdinalIgnoreCase)
            || url.Contains(Uri.EscapeDataString(RedactedValue), StringComparison.OrdinalIgnoreCase))
            throw new PalOpsApiException(422, "WEBHOOK_URL_REDACTED", "Webhook URL 中仍包含脱敏占位符，请输入完整地址后再保存。");
        if (provider.Type != WebhookProviderTypes.Telegram)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new PalOpsApiException(422, "WEBHOOK_URL_INVALID", "Webhook URL 无效。");
            await _destinationValidator.ValidateAsync(uri, request.AllowPrivateNetwork, cancellationToken);
        }

        var channel = new WebhookChannel(
            id,
            name,
            provider.Type,
            request.Enabled,
            url,
            method,
            headers,
            protectedSecrets,
            request.AllowPrivateNetwork,
            request.TimeoutSeconds,
            request.MaximumRetries,
            eventTypes,
            string.IsNullOrWhiteSpace(request.TitleTemplate) ? provider.Definition.DefaultTitleTemplate : request.TitleTemplate,
            string.IsNullOrWhiteSpace(request.BodyTemplate) ? provider.Definition.DefaultBodyTemplate : request.BodyTemplate,
            string.IsNullOrWhiteSpace(request.PayloadTemplate) ? provider.Definition.DefaultPayloadTemplate : request.PayloadTemplate,
            createdAt,
            updatedAt);
        var secrets = Unprotect(protectedSecrets);
        var rendered = _renderer.Render(channel, SampleEvent(), secrets);
        var requestDefinition = provider.CreateRequest(channel, rendered, secrets);
        await _destinationValidator.ValidateAsync(requestDefinition.Uri, request.AllowPrivateNetwork, cancellationToken);
        return channel;
    }

    private async Task<List<WebhookChannel>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            var envelope = await JsonSerializer.DeserializeAsync<ChannelFile>(stream, JsonOptions, cancellationToken);
            return envelope?.SchemaVersion == 1 ? envelope.Channels ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Unable to load webhook channel configuration.");
            return [];
        }
    }

    private async Task SaveUnsafeAsync(IReadOnlyList<WebhookChannel> channels, CancellationToken cancellationToken)
    {
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, new ChannelFile(1, DateTimeOffset.UtcNow, channels.ToList()), JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
            File.Move(temporary, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private string Protect(IReadOnlyDictionary<string, string> secrets) =>
        _protector.Protect(JsonSerializer.Serialize(secrets, JsonOptions));

    private Dictionary<string, string> Unprotect(string protectedJson)
    {
        if (string.IsNullOrWhiteSpace(protectedJson)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = _protector.Unprotect(protectedJson);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to unprotect webhook secrets.");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private WebhookChannelV1 ToContract(WebhookChannel channel) => new(
        channel.Id, channel.Name, channel.ProviderType, channel.Enabled, RedactUrl(channel.Url), channel.HttpMethod,
        RedactHeaders(channel.Headers), Unprotect(channel.ProtectedSecretJson).Count > 0, channel.AllowPrivateNetwork,
        channel.TimeoutSeconds, channel.MaximumRetries, channel.EventTypes, channel.TitleTemplate,
        channel.BodyTemplate, channel.PayloadTemplate, channel.CreatedAt, channel.UpdatedAt);

    private static string RedactUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Query))
            return url;
        var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var separator = part.IndexOf('=');
                return separator < 0 ? Uri.EscapeDataString(Uri.UnescapeDataString(part)) : part[..separator] + "=" + Uri.EscapeDataString(RedactedValue);
            });
        var builder = new UriBuilder(uri) { Query = string.Join("&", pairs) };
        return builder.Uri.AbsoluteUri;
    }

    private static string RestoreRedactedUrl(string requested, string existing)
    {
        var normalized = requested?.Trim() ?? string.Empty;
        return normalized.Equals(RedactUrl(existing), StringComparison.Ordinal) ? existing : normalized;
    }

    private static IReadOnlyDictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string> headers) =>
        headers.ToDictionary(
            pair => pair.Key,
            pair => IsSensitiveHeader(pair.Key) ? RedactedValue : pair.Value,
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> RestoreRedactedHeaders(
        IReadOnlyDictionary<string, string>? requested,
        IReadOnlyDictionary<string, string> existing)
    {
        if (requested is null) return new Dictionary<string, string>();
        var restored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in requested)
        {
            restored[pair.Key] = pair.Value == RedactedValue && existing.TryGetValue(pair.Key, out var current)
                ? current
                : pair.Value;
        }
        return restored;
    }

    private static bool IsSensitiveHeader(string name) =>
        SensitiveHeaderFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> NormalizeSecrets(IReadOnlyDictionary<string, string>? source) =>
        source?.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase)
        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> NormalizeHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null) return new Dictionary<string, string>();
        if (headers.Count > 30) throw new PalOpsApiException(422, "WEBHOOK_CHANNEL_INVALID", "自定义请求头最多 30 项。");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers)
        {
            var key = pair.Key?.Trim() ?? string.Empty;
            var value = pair.Value?.Trim() ?? string.Empty;
            if (key.Length is < 1 or > 100 || value.Length > 1000 || ForbiddenHeaders.Contains(key))
                throw new PalOpsApiException(422, "WEBHOOK_CHANNEL_INVALID", $"不允许的请求头：{key}");
            result[key] = value;
        }
        return result;
    }

    private static PalOpsEvent SampleEvent() => PalOpsEvent.Create(
        "notification.test",
        server: new Dictionary<string, object?> { ["name"] = "PalServer", ["state"] = "Running", ["processId"] = 1234, ["executablePath"] = "PalServer.exe", ["operationId"] = "test" },
        player: new Dictionary<string, object?> { ["name"] = "Player", ["uid"] = "uid", ["userId"] = "user", ["guildName"] = "Guild" },
        backup: new Dictionary<string, object?> { ["fileName"] = "backup.zip", ["size"] = 1024, ["worldId"] = "world", ["note"] = "test" },
        system: new Dictionary<string, object?> { ["cpuPercent"] = 10, ["memoryPercent"] = 20, ["diskFreeBytes"] = 1000000 },
        metadata: new Dictionary<string, object?> { ["message"] = "测试通知", ["errorCode"] = "", ["currentVersion"] = "1.0.0", ["latestVersion"] = "1.0.1", ["releaseUrl"] = "https://example.invalid" });

    private sealed record ChannelFile(int SchemaVersion, DateTimeOffset UpdatedAt, List<WebhookChannel> Channels);
}
