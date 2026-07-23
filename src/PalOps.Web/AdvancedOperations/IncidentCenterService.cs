using PalOps.Web.Health;

namespace PalOps.Web.AdvancedOperations;

public interface IIncidentCenterService
{
    Task<IncidentCenterDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IncidentRecord> CreateAsync(IncidentCreateRequest request, string actor, CancellationToken cancellationToken = default);
    Task<IncidentRecord> ApplyActionAsync(string id, IncidentActionRequest request, string actor, CancellationToken cancellationToken = default);
    Task<IncidentRule> UpsertRuleAsync(string? id, IncidentRuleWriteRequest request, string actor, CancellationToken cancellationToken = default);
    Task DeleteRuleAsync(string id, CancellationToken cancellationToken = default);
    Task<int> EvaluateHealthAsync(CancellationToken cancellationToken = default);
}

public sealed class IncidentCenterService(
    IAdvancedOperationsRepository repository,
    ISystemHealthService health,
    AdvancedOperationsValidator validator) : IIncidentCenterService
{
    public async Task<IncidentCenterDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var state = await EnsureDefaultRulesAsync(cancellationToken);
        var incidents = state.Incidents.OrderByDescending(static item => item.UpdatedAt).ToArray();
        return new(
            incidents.Count(static item => item.Status == IncidentStatus.Open),
            incidents.Count(static item => item.Status == IncidentStatus.Acknowledged),
            incidents.Count(static item => item.Status == IncidentStatus.Resolved),
            incidents.Count(static item => item.Status != IncidentStatus.Resolved && item.Severity == IncidentSeverity.Critical),
            incidents,
            state.IncidentRules.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            DateTimeOffset.UtcNow);
    }

    public Task<IncidentRecord> CreateAsync(IncidentCreateRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var title = validator.ValidateName(request.Title, nameof(request.Title), 160);
        var severity = validator.NormalizeSeverity(request.Severity);
        var source = validator.LimitText(request.Source, 80);
        var fingerprint = validator.LimitText(request.Fingerprint, 160);
        if (string.IsNullOrWhiteSpace(fingerprint)) fingerprint = "manual:" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        return repository.MutateAsync(state =>
        {
            var duplicate = state.Incidents.FirstOrDefault(item => item.Status != IncidentStatus.Resolved && item.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
            {
                var updated = IncidentTransitions.ObserveAgain(duplicate, "Duplicate observation aggregated.", now);
                Replace(state.Incidents, updated);
                return updated;
            }
            var incident = IncidentTransitions.Create(title, severity, string.IsNullOrWhiteSpace(source) ? "manual" : source, fingerprint, now,
                validator.LimitText(request.Description, 2000), validator.LimitText(request.Assignee, 120));
            incident = IncidentTransitions.AddComment(incident, actor, "Created manually.", now);
            state.Incidents.Add(incident);
            return incident;
        }, cancellationToken);
    }

    public Task<IncidentRecord> ApplyActionAsync(string id, IncidentActionRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var normalizedId = validator.ValidateName(id, nameof(id), 80);
        var action = validator.ValidateName(request.Action, nameof(request.Action), 40).ToLowerInvariant();
        var message = validator.LimitText(request.Message, 1000);
        var assignee = validator.LimitText(request.Assignee, 120);
        return repository.MutateAsync(state =>
        {
            var incident = state.Incidents.FirstOrDefault(item => item.Id.Equals(normalizedId, StringComparison.OrdinalIgnoreCase))
                           ?? throw new KeyNotFoundException("Incident not found.");
            var now = DateTimeOffset.UtcNow;
            var updated = action switch
            {
                "acknowledge" => IncidentTransitions.Acknowledge(incident, actor, Default(message, "Incident acknowledged."), now),
                "resolve" => IncidentTransitions.Resolve(incident, actor, Default(message, "Incident resolved."), now),
                "reopen" => IncidentTransitions.Reopen(incident, actor, Default(message, "Incident reopened."), now),
                "assign" => IncidentTransitions.Assign(incident, actor, validator.ValidateName(assignee, nameof(request.Assignee), 120), now),
                "comment" => IncidentTransitions.AddComment(incident, actor, validator.ValidateName(message, nameof(request.Message), 1000), now),
                _ => throw new ArgumentException("Unsupported incident action.", nameof(request.Action))
            };
            Replace(state.Incidents, updated);
            return updated;
        }, cancellationToken);
    }

    public Task<IncidentRule> UpsertRuleAsync(string? id, IncidentRuleWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var name = validator.ValidateName(request.Name, nameof(request.Name), 120);
        var sourceCode = validator.ValidateName(request.SourceCode, nameof(request.SourceCode), 160);
        var severity = validator.NormalizeSeverity(request.Severity);
        var statuses = (request.TriggerStatuses ?? []).Select(static item => item?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(static item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (statuses.Length == 0) throw new ArgumentException("At least one trigger status is required.", nameof(request.TriggerStatuses));
        var now = DateTimeOffset.UtcNow;
        return repository.MutateAsync(state =>
        {
            var existing = string.IsNullOrWhiteSpace(id)
                ? null
                : state.IncidentRules.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            var rule = new IncidentRule(
                existing?.Id ?? Guid.NewGuid().ToString("N"), name, request.Enabled, sourceCode, statuses, severity,
                request.AutoResolve, existing?.CreatedAt ?? now, now, actor);
            if (existing is null) state.IncidentRules.Add(rule); else Replace(state.IncidentRules, rule);
            return rule;
        }, cancellationToken);
    }

    public async Task DeleteRuleAsync(string id, CancellationToken cancellationToken = default)
    {
        _ = await repository.MutateAsync(state =>
        {
            var removed = state.IncidentRules.RemoveAll(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) throw new KeyNotFoundException("Incident rule not found.");
            return true;
        }, cancellationToken);
    }

    public async Task<int> EvaluateHealthAsync(CancellationToken cancellationToken = default)
    {
        await health.RefreshAsync(cancellationToken);
        var rulesState = await EnsureDefaultRulesAsync(cancellationToken);
        var enabledRules = rulesState.IncidentRules.Where(static item => item.Enabled).ToArray();
        var components = health.Components;
        return await repository.MutateAsync(state =>
        {
            var changed = 0;
            var now = DateTimeOffset.UtcNow;
            foreach (var rule in enabledRules)
            {
                var componentCode = rule.SourceCode.StartsWith("component.", StringComparison.OrdinalIgnoreCase)
                    ? rule.SourceCode["component.".Length..]
                    : rule.SourceCode;
                var component = components.FirstOrDefault(item => item.Name.Equals(componentCode, StringComparison.OrdinalIgnoreCase));
                if (component is null) continue;
                var triggered = rule.TriggerStatuses.Contains(component.Status, StringComparer.OrdinalIgnoreCase);
                var fingerprint = "health:" + rule.SourceCode.ToLowerInvariant();
                var active = state.Incidents.FirstOrDefault(item => item.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase) && item.Status != IncidentStatus.Resolved);
                if (triggered)
                {
                    if (active is null)
                    {
                        var incident = IncidentTransitions.Create(rule.Name, rule.Severity, "health-rule", fingerprint, now,
                            component.Message ?? $"Component status is {component.Status}.");
                        state.Incidents.Add(incident);
                    }
                    else
                    {
                        Replace(state.Incidents, IncidentTransitions.ObserveAgain(active, component.Message ?? component.Status, now));
                    }
                    changed++;
                }
                else if (active is not null && rule.AutoResolve)
                {
                    Replace(state.Incidents, IncidentTransitions.Resolve(active, "system", "Health component recovered automatically.", now));
                    changed++;
                }
            }
            return changed;
        }, cancellationToken);
    }

    private Task<AdvancedOperationsStateDocument> EnsureDefaultRulesAsync(CancellationToken cancellationToken) =>
        repository.MutateAsync(state =>
        {
            if (state.IncidentRules.Count == 0)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var code in new[] { "palworldRest", "palDefenderRest", "rcon", "saveIndex", "backups", "automation" })
                {
                    state.IncidentRules.Add(new IncidentRule(
                        Guid.NewGuid().ToString("N"),
                        $"{code} unavailable",
                        true,
                        "component." + code,
                        ["unavailable", "degraded"],
                        code is "palworldRest" or "rcon" ? IncidentSeverity.High : IncidentSeverity.Medium,
                        true,
                        now,
                        now,
                        "system"));
                }
            }
            return state;
        }, cancellationToken);

    private static string Default(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static void Replace(List<IncidentRecord> items, IncidentRecord updated)
    {
        var index = items.FindIndex(item => item.Id.Equals(updated.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) items.Add(updated); else items[index] = updated;
    }

    private static void Replace(List<IncidentRule> items, IncidentRule updated)
    {
        var index = items.FindIndex(item => item.Id.Equals(updated.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) items.Add(updated); else items[index] = updated;
    }
}
