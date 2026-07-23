using PalOps.Web.Events;
using PalOps.Web.Notifications;

namespace PalOps.Web.AdvancedOperations;

public interface IIntegrationCenterService
{
    Task<IntegrationCenterDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IntegrationSubscription> UpsertSubscriptionAsync(string? id, IntegrationSubscriptionWriteRequest request, string actor, CancellationToken cancellationToken = default);
    Task DeleteSubscriptionAsync(string id, CancellationToken cancellationToken = default);
    Task<IntegrationEventRecord> IngestEventAsync(IntegrationEventRequest request, string tokenPrefix, CancellationToken cancellationToken = default);
}

public sealed class IntegrationCenterService(
    IAdvancedOperationsRepository repository,
    AdvancedOperationsValidator validator,
    IWebhookDestinationValidator destinationValidator,
    IWebhookChannelStore webhookChannels,
    IWebhookHistoryStore webhookHistory,
    IPalOpsEventPublisher eventPublisher) : IIntegrationCenterService
{
    public async Task<IntegrationCenterDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var stateTask = repository.ReadAsync(cancellationToken);
        var historyTask = webhookHistory.ListAsync(1, 200, null, null, null, cancellationToken);
        var state = await stateTask;
        var history = await historyTask;
        var latestByChannel = history.Items
            .GroupBy(static item => item.ChannelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(item => item.CreatedAt).First(), StringComparer.OrdinalIgnoreCase);
        var subscriptions = state.IntegrationSubscriptions.Select(item =>
        {
            if (string.IsNullOrWhiteSpace(item.DeliveryChannelId) || !latestByChannel.TryGetValue(item.DeliveryChannelId, out var delivery)) return item;
            return item with
            {
                LastStatus = delivery.Status,
                LastMessage = DeliveryMessage(delivery),
                LastDeliveredAt = delivery.SentAt ?? delivery.CreatedAt
            };
        }).OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(
            subscriptions.Count(static item => item.Enabled),
            subscriptions.Length,
            state.IntegrationEvents.Count(static item => item.Outcome == "accepted"),
            state.IntegrationEvents.Count(static item => item.Outcome == "rejected"),
            subscriptions,
            state.IntegrationEvents.OrderByDescending(static item => item.ReceivedAt).Take(200).ToArray(),
            DateTimeOffset.UtcNow);
    }

    public async Task<IntegrationSubscription> UpsertSubscriptionAsync(string? id, IntegrationSubscriptionWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var name = validator.ValidateName(request.Name, nameof(request.Name), 100);
        var destination = ValidateDestination(request.Destination);
        await destinationValidator.ValidateAsync(new Uri(destination), false, cancellationToken);
        var eventTypes = ValidateEventTypes(request.EventTypes, allowWildcard: true);
        var secretReference = validator.LimitText(request.SigningSecretReference, 240);
        var normalizedActor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
        var state = await repository.ReadAsync(cancellationToken);
        var existing = string.IsNullOrWhiteSpace(id)
            ? null
            : state.IntegrationSubscriptions.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(id) && existing is null) throw new KeyNotFoundException("Integration subscription not found.");

        var channelRequest = BuildWebhookRequest(name, request.Enabled, destination, eventTypes);
        WebhookChannelV1 channel;
        var channelCreated = false;
        if (!string.IsNullOrWhiteSpace(existing?.DeliveryChannelId))
        {
            try
            {
                channel = await webhookChannels.UpdateAsync(existing.DeliveryChannelId, channelRequest, normalizedActor, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                channel = await webhookChannels.CreateAsync(channelRequest, normalizedActor, cancellationToken);
                channelCreated = true;
            }
        }
        else
        {
            channel = await webhookChannels.CreateAsync(channelRequest, normalizedActor, cancellationToken);
            channelCreated = true;
        }

        try
        {
            return await repository.MutateAsync(document =>
            {
                var now = DateTimeOffset.UtcNow;
                if (existing is null)
                {
                    var created = new IntegrationSubscription(
                        Guid.NewGuid().ToString("N"), name, request.Enabled, eventTypes, destination, secretReference,
                        now, now, normalizedActor, request.Enabled ? "active" : "disabled",
                        request.Enabled ? "Subscription is connected to the webhook delivery pipeline." : "Subscription is disabled.",
                        null, channel.Id);
                    document.IntegrationSubscriptions.Add(created);
                    return created;
                }

                var index = document.IntegrationSubscriptions.FindIndex(item => item.Id.Equals(existing.Id, StringComparison.OrdinalIgnoreCase));
                if (index < 0) throw new KeyNotFoundException("Integration subscription not found.");
                var updated = document.IntegrationSubscriptions[index] with
                {
                    Name = name,
                    Enabled = request.Enabled,
                    EventTypes = eventTypes,
                    Destination = destination,
                    SigningSecretReference = secretReference,
                    UpdatedAt = now,
                    UpdatedBy = normalizedActor,
                    LastStatus = request.Enabled ? "active" : "disabled",
                    LastMessage = request.Enabled ? "Subscription is connected to the webhook delivery pipeline." : "Subscription is disabled.",
                    DeliveryChannelId = channel.Id
                };
                document.IntegrationSubscriptions[index] = updated;
                return updated;
            }, cancellationToken);
        }
        catch
        {
            if (channelCreated)
            {
                try { await webhookChannels.DeleteAsync(channel.Id, CancellationToken.None); } catch { }
            }
            else if (existing is not null)
            {
                try
                {
                    await webhookChannels.UpdateAsync(
                        channel.Id,
                        BuildWebhookRequest(existing.Name, existing.Enabled, existing.Destination, existing.EventTypes),
                        normalizedActor,
                        CancellationToken.None);
                }
                catch { }
            }
            throw;
        }
    }

    public async Task DeleteSubscriptionAsync(string id, CancellationToken cancellationToken = default)
    {
        var state = await repository.ReadAsync(cancellationToken);
        var subscription = state.IntegrationSubscriptions.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                           ?? throw new KeyNotFoundException("Integration subscription not found.");
        if (!string.IsNullOrWhiteSpace(subscription.DeliveryChannelId))
        {
            try { await webhookChannels.DeleteAsync(subscription.DeliveryChannelId, cancellationToken); }
            catch (KeyNotFoundException) { }
        }
        _ = await repository.MutateAsync(document =>
        {
            var removed = document.IntegrationSubscriptions.RemoveAll(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) throw new KeyNotFoundException("Integration subscription not found.");
            return true;
        }, cancellationToken);
    }

    public async Task<IntegrationEventRecord> IngestEventAsync(IntegrationEventRequest request, string tokenPrefix, CancellationToken cancellationToken = default)
    {
        var eventType = ValidateEventTypes([request.EventType], allowWildcard: false)[0];
        if (!eventType.StartsWith("external.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Inbound integration event types must start with external.", nameof(request.EventType));
        var idempotencyKey = validator.ValidateName(request.IdempotencyKey, nameof(request.IdempotencyKey), 160);
        var source = validator.ValidateName(request.Source, nameof(request.Source), 120);
        var metadata = NormalizeMetadata(request.Metadata);
        var now = DateTimeOffset.UtcNow;

        var stored = await repository.MutateAsync(state =>
        {
            var duplicate = state.IntegrationEvents.FirstOrDefault(item =>
                item.Source.Equals(source, StringComparison.OrdinalIgnoreCase)
                && item.IdempotencyKey.Equals(idempotencyKey, StringComparison.Ordinal));
            if (duplicate is not null) return (Record: duplicate, Duplicate: true);
            var created = new IntegrationEventRecord(
                Guid.NewGuid().ToString("N"), eventType, idempotencyKey, source, "accepted",
                $"Accepted through API token {tokenPrefix}.", now);
            state.IntegrationEvents.Add(created);
            return (Record: created, Duplicate: false);
        }, cancellationToken);

        if (!stored.Duplicate)
        {
            var publishedMetadata = metadata.ToDictionary(static pair => pair.Key, static pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);
            publishedMetadata["source"] = source;
            publishedMetadata["idempotencyKey"] = idempotencyKey;
            publishedMetadata["tokenPrefix"] = tokenPrefix;
            await eventPublisher.PublishAsync(PalOpsEvent.Create(eventType, "information", metadata: publishedMetadata), cancellationToken);
        }
        return stored.Record;
    }

    private static WebhookChannelWriteRequest BuildWebhookRequest(
        string name,
        bool enabled,
        string destination,
        IReadOnlyList<string> eventTypes) => new(
            $"Integration: {name}",
            WebhookProviderTypes.GenericJson,
            enabled,
            destination,
            "POST",
            new Dictionary<string, string> { ["X-PalOps-Integration"] = "advanced-operations" },
            null,
            false,
            false,
            10,
            3,
            eventTypes,
            "{{event.name}}",
            "{{event.type}}",
            "{\"eventId\":\"{{event.id}}\",\"eventType\":\"{{event.type}}\",\"occurredAt\":\"{{event.time}}\",\"severity\":\"{{event.severity}}\"}");

    private static string DeliveryMessage(WebhookDeliveryRecord delivery)
    {
        if (!string.IsNullOrWhiteSpace(delivery.ErrorMessage)) return delivery.ErrorMessage;
        if (!string.IsNullOrWhiteSpace(delivery.ResponseSummary)) return delivery.ResponseSummary;
        return $"Delivery {delivery.Status} after {delivery.Attempt} attempt(s).";
    }

    private static IReadOnlyList<string> ValidateEventTypes(IReadOnlyList<string>? eventTypes, bool allowWildcard)
    {
        var values = (eventTypes ?? [])
            .Select(static value => value.Trim().ToLowerInvariant())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length == 0) throw new ArgumentException("At least one event type is required.", nameof(eventTypes));
        if (values.Length > 100) throw new ArgumentException("Event type count cannot exceed 100.", nameof(eventTypes));
        foreach (var value in values)
        {
            if (value.Length > 100) throw new ArgumentException("Event type cannot exceed 100 characters.", nameof(eventTypes));
            var wildcardValid = allowWildcard && value.EndsWith(".*", StringComparison.Ordinal) && value.Count(static character => character == '*') == 1;
            var normalValid = !value.Contains('*');
            if ((!wildcardValid && !normalValid) || value.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '*')))
                throw new ArgumentException($"Invalid event type: {value}", nameof(eventTypes));
        }
        return values;
    }

    private static string ValidateDestination(string? destination)
    {
        var value = (destination ?? string.Empty).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || !string.IsNullOrWhiteSpace(uri.Fragment))
            throw new ArgumentException("Integration destination must be an absolute HTTPS URL without credentials or fragments.", nameof(destination));
        return uri.AbsoluteUri;
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return new Dictionary<string, string>();
        if (metadata.Count > 50) throw new ArgumentException("Integration metadata cannot exceed 50 entries.", nameof(metadata));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in metadata)
        {
            var key = pair.Key.Trim();
            if (key.Length is < 1 or > 80 || key.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
                throw new ArgumentException("Integration metadata contains an invalid key.", nameof(metadata));
            var value = (pair.Value ?? string.Empty).Replace("\0", string.Empty).Trim();
            if (value.Length > 1000) throw new ArgumentException($"Integration metadata value '{key}' exceeds 1000 characters.", nameof(metadata));
            result[key] = value;
        }
        return result;
    }
}
