using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;
using PalOps.Web.Events;
using PalOps.Web.Infrastructure;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Platform.Workers;

namespace PalOps.Web.Notifications;

public interface IWebhookDeliveryService
{
    Task<WebhookTestResult> DeliverTestAsync(
        WebhookChannel channel,
        PalOpsEvent palOpsEvent,
        CancellationToken cancellationToken = default);
}

public sealed class WebhookDispatcherService : BackgroundService, IWebhookDeliveryService
{
    // The named client is registered with HttpClientHandler.AllowAutoRedirect = false;
    // every redirect is followed manually and revalidated below.
    private const int QueueCapacity = 5000;
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10)
    ];

    private readonly IPalOpsEventBus _bus;
    private readonly IPalOpsEventPublisher _publisher;
    private readonly IWebhookChannelStore _channels;
    private readonly IWebhookTemplateRenderer _renderer;
    private readonly IWebhookProviderRegistry _providers;
    private readonly IWebhookDestinationValidator _destinationValidator;
    private readonly IWebhookHistoryStore _history;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDispatcherService> _logger;
    private readonly IBackgroundWorkerSupervisor _workerSupervisor;
    private readonly IOperationalReadinessGate _readinessGate;
    private readonly Channel<WebhookDeliveryWorkItem> _queue;
    private IPalOpsEventSubscription? _subscription;

    public WebhookDispatcherService(
        IPalOpsEventBus bus,
        IPalOpsEventPublisher publisher,
        IWebhookChannelStore channels,
        IWebhookTemplateRenderer renderer,
        IWebhookProviderRegistry providers,
        IWebhookDestinationValidator destinationValidator,
        IWebhookHistoryStore history,
        IHttpClientFactory httpClientFactory,
        IBackgroundWorkerSupervisor workerSupervisor,
        IOperationalReadinessGate readinessGate,
        ILogger<WebhookDispatcherService> logger)
    {
        _bus = bus;
        _publisher = publisher;
        _channels = channels;
        _renderer = renderer;
        _providers = providers;
        _destinationValidator = destinationValidator;
        _history = history;
        _httpClientFactory = httpClientFactory;
        _workerSupervisor = workerSupervisor;
        _readinessGate = readinessGate;
        _logger = logger;
        _queue = Channel.CreateBounded<WebhookDeliveryWorkItem>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _workerSupervisor.RunAsync("webhook-dispatcher", RunLoopAsync, stoppingToken);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        await _readinessGate.WaitUntilReadyAsync(
            "webhook-dispatcher",
            anyOf: OperationalCapability.Core,
            cancellationToken: stoppingToken).ConfigureAwait(false);

        if (_subscription is not null) await _subscription.DisposeAsync();
        _subscription = _bus.Subscribe("webhook-dispatcher", QueueCapacity);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var routingTask = RouteEventsAsync(_subscription, linked.Token);
        var deliveryTask = ProcessQueueAsync(linked.Token);
        var heartbeatTask = HeartbeatAsync(linked.Token);
        var completed = await Task.WhenAny(routingTask, deliveryTask).ConfigureAwait(false);
        linked.Cancel();
        try { await completed.ConfigureAwait(false); }
        finally
        {
            try { await Task.WhenAll(routingTask, deliveryTask, heartbeatTask).ConfigureAwait(false); }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        }
    }

    private async Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!cancellationToken.IsCancellationRequested)
        {
            _workerSupervisor.Heartbeat("webhook-dispatcher");
            if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) break;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        if (_subscription is not null) await _subscription.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }

    public async Task<WebhookTestResult> DeliverTestAsync(
        WebhookChannel channel,
        PalOpsEvent palOpsEvent,
        CancellationToken cancellationToken = default)
    {
        var result = await DeliverOnceAsync(channel, palOpsEvent, cancellationToken);
        var status = result.Success ? "succeeded" : "failed";
        var finalized = result with { Status = status };
        await AppendHistoryAsync(channel, palOpsEvent, finalized, 1, cancellationToken);
        return new(
            finalized.Success,
            finalized.Status,
            finalized.HttpStatusCode,
            finalized.DurationMs,
            finalized.ResponseSummary,
            finalized.ErrorCode,
            finalized.ErrorMessage);
    }

    private async Task RouteEventsAsync(IPalOpsEventSubscription subscription, CancellationToken cancellationToken)
    {
        await foreach (var palOpsEvent in subscription.ReadAllAsync(cancellationToken))
        {
            IReadOnlyList<WebhookChannel> channels;
            try { channels = await _channels.ListInternalAsync(cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to load webhook channels for event {EventType}.", palOpsEvent.EventType);
                continue;
            }

            foreach (var channel in channels.Where(channel => channel.Enabled && IsSubscribed(channel, palOpsEvent)))
            {
                if (!_queue.Writer.TryWrite(new WebhookDeliveryWorkItem(palOpsEvent, channel.Id, 1, DateTimeOffset.UtcNow)))
                {
                    _logger.LogWarning("Webhook work queue rejected event {EventType} for channel {ChannelId}.", palOpsEvent.EventType, channel.Id);
                    await PublishBestEffortAsync(PalOpsEvent.Create(
                        "webhook.queue-overflow",
                        "warning",
                        metadata: new Dictionary<string, object?>
                        {
                            ["message"] = "Webhook 派发队列已满，最旧任务可能被丢弃。",
                            ["channelId"] = channel.Id,
                            ["eventType"] = palOpsEvent.EventType
                        }), cancellationToken);
                }
            }
        }
    }

    private static bool IsSubscribed(WebhookChannel channel, PalOpsEvent palOpsEvent)
    {
        var exact = channel.EventTypes.Any(value => value.Equals(palOpsEvent.EventType, StringComparison.OrdinalIgnoreCase));
        if (palOpsEvent.EventType.StartsWith("webhook.delivery.", StringComparison.OrdinalIgnoreCase))
        {
            if (!exact) return false;
            if (palOpsEvent.Metadata.TryGetValue("channelId", out var source)
                && string.Equals(Convert.ToString(source, System.Globalization.CultureInfo.InvariantCulture), channel.Id, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
        var prefixWildcard = channel.EventTypes.Any(value =>
            value.EndsWith(".*", StringComparison.Ordinal)
            && palOpsEvent.EventType.StartsWith(value[..^1], StringComparison.OrdinalIgnoreCase));
        return exact || prefixWildcard || channel.EventTypes.Contains("*", StringComparer.OrdinalIgnoreCase);
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (var workItem in _queue.Reader.ReadAllAsync(cancellationToken))
        {
            if (workItem.DueAt > DateTimeOffset.UtcNow)
                await Task.Delay(workItem.DueAt - DateTimeOffset.UtcNow, cancellationToken);

            WebhookChannel channel;
            try { channel = await _channels.GetInternalAsync(workItem.ChannelId, cancellationToken); }
            catch (KeyNotFoundException)
            {
                _logger.LogInformation("Webhook channel {ChannelId} was removed before delivery.", workItem.ChannelId);
                continue;
            }
            if (!channel.Enabled) continue;

            var result = await DeliverOnceAsync(channel, workItem.Event, cancellationToken);
            if (result.Success)
            {
                var succeeded = result with { Status = "succeeded" };
                await AppendHistoryAsync(channel, workItem.Event, succeeded, workItem.Attempt, cancellationToken);
                await PublishDeliveryEventAsync("webhook.delivery.succeeded", channel, workItem.Event, succeeded, workItem.Attempt, null, cancellationToken);
                continue;
            }

            var retryIndex = workItem.Attempt - 1;
            var maximumRetries = Math.Min(channel.MaximumRetries, RetryDelays.Length);
            if (result.Retryable && retryIndex < maximumRetries)
            {
                var nextAttempt = workItem.Attempt + 1;
                var dueAt = DateTimeOffset.UtcNow.Add(RetryDelays[retryIndex]);
                var retrying = result with { Status = "retrying", NextAttemptAt = dueAt };
                await AppendHistoryAsync(channel, workItem.Event, retrying, workItem.Attempt, cancellationToken);
                await PublishDeliveryEventAsync("webhook.delivery.retrying", channel, workItem.Event, retrying, workItem.Attempt, dueAt, cancellationToken);
                _ = ScheduleRetryAsync(new WebhookDeliveryWorkItem(workItem.Event, channel.Id, nextAttempt, dueAt), cancellationToken);
                continue;
            }

            var failed = result with { Status = "failed" };
            await AppendHistoryAsync(channel, workItem.Event, failed, workItem.Attempt, cancellationToken);
            await PublishDeliveryEventAsync("webhook.delivery.failed", channel, workItem.Event, failed, workItem.Attempt, null, cancellationToken);
        }
    }

    private async Task ScheduleRetryAsync(WebhookDeliveryWorkItem workItem, CancellationToken cancellationToken)
    {
        try
        {
            var delay = workItem.DueAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            if (!_queue.Writer.TryWrite(workItem))
                _logger.LogWarning("Webhook retry queue rejected event {EventType} for channel {ChannelId}.", workItem.Event.EventType, workItem.ChannelId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to schedule webhook retry for event {EventType} and channel {ChannelId}.", workItem.Event.EventType, workItem.ChannelId);
        }
    }

    private async Task<DeliveryAttemptResult> DeliverOnceAsync(
        WebhookChannel channel,
        PalOpsEvent palOpsEvent,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        WebhookRequestDefinition? request = null;
        try
        {
            var secrets = await _channels.GetSecretsAsync(channel, cancellationToken);
            var rendered = _renderer.Render(channel, palOpsEvent, secrets);
            var provider = _providers.Get(channel.ProviderType);
            request = provider.CreateRequest(channel, rendered, secrets);
            var result = await SendAsync(channel, request, cancellationToken);
            stopwatch.Stop();

            var attemptResult = new DeliveryAttemptResult(
                result.Success,
                result.Success ? "succeeded" : "failed",
                result.Retryable,
                result.StatusCode,
                stopwatch.ElapsedMilliseconds,
                result.ResponseSummary,
                result.ErrorCode,
                result.ErrorMessage,
                null,
                request);

            return attemptResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var code = ex is PalOpsApiException api ? api.Code : "WEBHOOK_DELIVERY_ERROR";
            var message = ex is PalOpsApiException apiException ? apiException.Message : "Webhook 发送失败。";
            var result = new DeliveryAttemptResult(false, "failed", IsRetryableException(ex), null, stopwatch.ElapsedMilliseconds, string.Empty, code, message, null, request);
            _logger.LogWarning(ex, "Webhook delivery failed for channel {ChannelId} and event {EventType}.", channel.Id, palOpsEvent.EventType);
            return result;
        }
    }

    private async Task<SendResult> SendAsync(
        WebhookChannel channel,
        WebhookRequestDefinition definition,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("webhooks");
        var current = definition;
        for (var redirect = 0; redirect <= 3; redirect++)
        {
            await _destinationValidator.ResolveAndValidateAsync(current.Uri, channel.AllowPrivateNetwork, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(channel.TimeoutSeconds));
            using var request = CreateHttpRequest(current);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
            {
                if (redirect == 3)
                    return new(false, false, (int)response.StatusCode, string.Empty, "WEBHOOK_REDIRECT_LIMIT", "Webhook 重定向次数超过 3 次。");
                var target = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(current.Uri, response.Headers.Location);
                await _destinationValidator.ResolveAndValidateAsync(target, channel.AllowPrivateNetwork, timeout.Token);
                current = current with { Uri = target };
                continue;
            }

            var summary = await ReadBoundedResponseAsync(response, timeout.Token);
            var status = (int)response.StatusCode;
            if (status is >= 200 and <= 299)
                return new(true, false, status, summary, null, null);
            var retryable = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || status >= 500;
            return new(false, retryable, status, summary, "WEBHOOK_HTTP_ERROR", $"Webhook 返回 HTTP {status}。");
        }
        return new(false, false, null, string.Empty, "WEBHOOK_REDIRECT_LIMIT", "Webhook 重定向次数超过限制。");
    }

    private static HttpRequestMessage CreateHttpRequest(WebhookRequestDefinition definition)
    {
        var request = new HttpRequestMessage(definition.Method, definition.Uri)
        {
            Content = new StringContent(definition.Body, Encoding.UTF8, definition.ContentType)
        };
        foreach (var header in definition.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static async Task<string> ReadBoundedResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null) return string.Empty;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (buffer.Length < MaximumResponseBytes)
        {
            var count = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, MaximumResponseBytes - (int)buffer.Length)), cancellationToken);
            if (count == 0) break;
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private async Task AppendHistoryAsync(
        WebhookChannel channel,
        PalOpsEvent palOpsEvent,
        DeliveryAttemptResult result,
        int attempt,
        CancellationToken cancellationToken)
    {
        var requestSummary = result.Request is null
            ? "request-not-created"
            : WebhookHistoryStore.BuildRequestSummary(
                result.Request.Method,
                result.Request.Uri,
                Encoding.UTF8.GetByteCount(result.Request.Body),
                channel.ProviderType);
        await _history.AppendAsync(new WebhookDeliveryRecord(
            Guid.NewGuid().ToString("N"),
            palOpsEvent.EventId,
            palOpsEvent.EventType,
            channel.Id,
            channel.Name,
            channel.ProviderType,
            result.Status,
            attempt,
            DateTimeOffset.UtcNow,
            result.Success ? DateTimeOffset.UtcNow : null,
            result.HttpStatusCode,
            result.DurationMs,
            requestSummary,
            result.ResponseSummary,
            result.ErrorCode,
            result.ErrorMessage,
            result.NextAttemptAt), cancellationToken);
    }

    private async Task PublishDeliveryEventAsync(
        string eventType,
        WebhookChannel channel,
        PalOpsEvent source,
        DeliveryAttemptResult result,
        int attempt,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        await PublishBestEffortAsync(PalOpsEvent.Create(
            eventType,
            result.Success ? "information" : eventType.EndsWith("retrying", StringComparison.OrdinalIgnoreCase) ? "warning" : "error",
            metadata: new Dictionary<string, object?>
            {
                ["message"] = result.Success ? "Webhook 推送成功。" : result.ErrorMessage ?? "Webhook 推送失败。",
                ["channelId"] = channel.Id,
                ["channelName"] = channel.Name,
                ["providerType"] = channel.ProviderType,
                ["sourceEventId"] = source.EventId,
                ["sourceEventType"] = source.EventType,
                ["attempt"] = attempt,
                ["httpStatusCode"] = result.HttpStatusCode,
                ["nextAttemptAt"] = nextAttemptAt
            }), cancellationToken);
    }

    private async ValueTask PublishBestEffortAsync(PalOpsEvent palOpsEvent, CancellationToken cancellationToken)
    {
        try { await _publisher.PublishAsync(palOpsEvent, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish notification event {EventType}.", palOpsEvent.EventType);
        }
    }

    private static bool IsRetryableException(Exception exception) => exception is
        HttpRequestException or IOException or TaskCanceledException;

    private sealed record SendResult(
        bool Success,
        bool Retryable,
        int? StatusCode,
        string ResponseSummary,
        string? ErrorCode,
        string? ErrorMessage);

    private sealed record DeliveryAttemptResult(
        bool Success,
        string Status,
        bool Retryable,
        int? HttpStatusCode,
        long DurationMs,
        string ResponseSummary,
        string? ErrorCode,
        string? ErrorMessage,
        DateTimeOffset? NextAttemptAt,
        WebhookRequestDefinition? Request);
}
