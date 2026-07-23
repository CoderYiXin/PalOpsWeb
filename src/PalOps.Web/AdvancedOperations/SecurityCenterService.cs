using System.Collections.Concurrent;

namespace PalOps.Web.AdvancedOperations;

public sealed record ApiTokenValidationResult(
    bool Valid,
    string TokenId,
    string TokenName,
    string Prefix,
    string RequiredScope,
    string FailureReason);

public interface ISecurityCenterService
{
    Task<SecurityCenterDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<SecurityPolicy> UpdatePolicyAsync(SecurityPolicyWriteRequest request, string actor, CancellationToken cancellationToken = default);
    Task<ApiTokenCreationResult> CreateTokenAsync(ApiTokenCreateRequest request, string actor, CancellationToken cancellationToken = default);
    Task<ApiTokenView> RevokeTokenAsync(string id, ApiTokenRevokeRequest request, string actor, CancellationToken cancellationToken = default);
    Task<ApiTokenValidationResult> ValidateTokenAsync(string? plainText, string requiredScope, string remoteIp, CancellationToken cancellationToken = default);
    Task RecordObservationAsync(string observationType, string actor, string remoteIp, string outcome, string message, CancellationToken cancellationToken = default);
}

public sealed class SecurityCenterService(
    IAdvancedOperationsRepository repository,
    AdvancedOperationsValidator validator) : ISecurityCenterService
{
    private static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ObservationWriteInterval = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _observationWrites = new(StringComparer.OrdinalIgnoreCase);
    public async Task<SecurityCenterDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var state = await repository.ReadAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var views = state.ApiTokens
            .OrderByDescending(static token => token.CreatedAt)
            .Select(ToView)
            .ToArray();
        return new(
            NormalizePolicy(state.SecurityPolicy),
            state.ApiTokens.Count(token => !token.RevokedAt.HasValue && token.ExpiresAt > now),
            state.ApiTokens.Count(token => !token.RevokedAt.HasValue && token.ExpiresAt > now && token.ExpiresAt <= now.AddDays(7)),
            state.ApiTokens.Count(static token => token.RevokedAt.HasValue),
            views,
            state.SecurityObservations.OrderByDescending(static item => item.OccurredAt).Take(200).ToArray(),
            now);
    }

    public Task<SecurityPolicy> UpdatePolicyAsync(SecurityPolicyWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        if (request.MaximumTokenDays is < 1 or > 365)
            throw new ArgumentException("Maximum token lifetime must be between 1 and 365 days.", nameof(request.MaximumTokenDays));
        if (request.MaximumActiveTokens is < 1 or > 100)
            throw new ArgumentException("Maximum active token count must be between 1 and 100.", nameof(request.MaximumActiveTokens));
        if (!request.RequireHighRiskConfirmation)
            throw new ArgumentException("High-risk exact confirmation cannot be disabled.", nameof(request.RequireHighRiskConfirmation));
        var policy = new SecurityPolicy(
            request.ApiTokensEnabled,
            request.MaximumTokenDays,
            true,
            request.AuditTokenUse,
            request.MaximumActiveTokens,
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim());
        return repository.MutateAsync(state => state.SecurityPolicy = policy, cancellationToken);
    }

    public Task<ApiTokenCreationResult> CreateTokenAsync(ApiTokenCreateRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var name = validator.ValidateName(request.Name, nameof(request.Name), 120);
        var scopes = validator.ValidateScopes(request.Scopes);
        var normalizedActor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
        return repository.MutateAsync(state =>
        {
            var now = DateTimeOffset.UtcNow;
            var policy = NormalizePolicy(state.SecurityPolicy);
            if (!policy.ApiTokensEnabled) throw new InvalidOperationException("API token creation is disabled by security policy.");
            var active = state.ApiTokens.Count(token => !token.RevokedAt.HasValue && token.ExpiresAt > now);
            if (active >= policy.MaximumActiveTokens)
                throw new InvalidOperationException($"The active API token limit ({policy.MaximumActiveTokens}) has been reached.");
            if (request.ExpiresInDays is < 1 || request.ExpiresInDays > policy.MaximumTokenDays)
                throw new ArgumentException($"Token lifetime must be between 1 and {policy.MaximumTokenDays} days.", nameof(request.ExpiresInDays));

            var secret = ApiTokenSecret.Create(scopes[0], now.AddDays(request.ExpiresInDays), name, normalizedActor);
            var record = secret.Record with { Scopes = scopes, CreatedAt = now };
            state.ApiTokens.Add(record);
            state.SecurityObservations.Add(Observation("api-token.created", normalizedActor, string.Empty, "success", $"Created API token {record.Prefix}.", now));
            return new ApiTokenCreationResult(ToView(record), secret.PlainText);
        }, cancellationToken);
    }

    public Task<ApiTokenView> RevokeTokenAsync(string id, ApiTokenRevokeRequest request, string actor, CancellationToken cancellationToken = default)
    {
        AdvancedOperationsValidator.RequireConfirmation(request.Confirmation, "REVOKE TOKEN");
        var normalizedActor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
        return repository.MutateAsync(state =>
        {
            var index = state.ApiTokens.FindIndex(token => token.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new KeyNotFoundException("API token not found.");
            var current = state.ApiTokens[index];
            if (current.RevokedAt.HasValue) return ToView(current);
            var now = DateTimeOffset.UtcNow;
            var revoked = current with { RevokedAt = now, RevokedBy = normalizedActor };
            state.ApiTokens[index] = revoked;
            state.SecurityObservations.Add(Observation("api-token.revoked", normalizedActor, string.Empty, "success", $"Revoked API token {revoked.Prefix}.", now));
            return ToView(revoked);
        }, cancellationToken);
    }

    public async Task<ApiTokenValidationResult> ValidateTokenAsync(string? plainText, string requiredScope, string remoteIp, CancellationToken cancellationToken = default)
    {
        var scope = validator.ValidateScopes([requiredScope])[0];
        var tokenText = plainText?.Trim() ?? string.Empty;
        var prefix = ExtractPrefix(tokenText);
        var state = await repository.ReadAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var policy = NormalizePolicy(state.SecurityPolicy);
        ApiTokenRecord? record = null;
        ApiTokenValidationResult result;

        if (!policy.ApiTokensEnabled)
        {
            result = new(false, string.Empty, string.Empty, prefix, scope, "API tokens are disabled.");
        }
        else if (string.IsNullOrWhiteSpace(prefix))
        {
            result = new(false, string.Empty, string.Empty, string.Empty, scope, "Bearer token format is invalid.");
        }
        else
        {
            record = state.ApiTokens.FirstOrDefault(token => token.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                result = new(false, string.Empty, string.Empty, prefix, scope, "Bearer token was not recognized.");
            }
            else if (!record.Verify(tokenText, now, scope))
            {
                var reason = record.RevokedAt.HasValue ? "Bearer token has been revoked."
                    : record.ExpiresAt <= now ? "Bearer token has expired."
                    : !record.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase) ? "Bearer token scope is insufficient."
                    : "Bearer token is invalid.";
                result = new(false, record.Id, record.Name, record.Prefix, scope, reason);
            }
            else
            {
                result = new(true, record.Id, record.Name, record.Prefix, scope, string.Empty);
            }
        }

        var shouldUpdateLastUsed = result.Valid && (!record!.LastUsedAt.HasValue || now - record.LastUsedAt.Value >= LastUsedWriteInterval);
        var observationKey = result.Valid
            ? $"token:{result.TokenId}|{scope}"
            : $"ip:{remoteIp}|{scope}|rejected";
        var shouldAudit = policy.AuditTokenUse && ShouldWriteObservation(observationKey, now);
        if (shouldUpdateLastUsed || shouldAudit)
        {
            await repository.MutateAsync(document =>
            {
                if (shouldUpdateLastUsed)
                {
                    var index = document.ApiTokens.FindIndex(token => token.Id.Equals(result.TokenId, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0 && !document.ApiTokens[index].RevokedAt.HasValue)
                        document.ApiTokens[index] = document.ApiTokens[index] with { LastUsedAt = now };
                }
                if (shouldAudit)
                {
                    document.SecurityObservations.Add(Observation(
                        "api-token.used",
                        result.TokenName.Length > 0 ? result.TokenName : "unknown-token",
                        remoteIp,
                        result.Valid ? "success" : "rejected",
                        result.Valid ? $"Token {result.Prefix} authorized scope {scope}." : result.FailureReason,
                        now));
                }
                return true;
            }, cancellationToken);
        }

        return result;
    }

    private bool ShouldWriteObservation(string key, DateTimeOffset now)
    {
        if (_observationWrites.Count > 10_000)
        {
            var cutoff = now - TimeSpan.FromDays(1);
            foreach (var entry in _observationWrites)
            {
                if (entry.Value < cutoff) _observationWrites.TryRemove(entry.Key, out _);
            }
        }

        while (true)
        {
            if (!_observationWrites.TryGetValue(key, out var last))
                return _observationWrites.TryAdd(key, now);
            if (now - last < ObservationWriteInterval) return false;
            if (_observationWrites.TryUpdate(key, now, last)) return true;
        }
    }

    public async Task RecordObservationAsync(string observationType, string actor, string remoteIp, string outcome, string message, CancellationToken cancellationToken = default)
    {
        var type = validator.ValidateName(observationType, nameof(observationType), 100).ToLowerInvariant();
        var normalizedActor = validator.LimitText(actor, 120);
        var normalizedIp = validator.LimitText(remoteIp, 80);
        var normalizedOutcome = validator.LimitText(outcome, 40);
        var normalizedMessage = validator.LimitText(message, 1000);
        _ = await repository.MutateAsync(state =>
        {
            state.SecurityObservations.Add(Observation(type, normalizedActor, normalizedIp, normalizedOutcome, normalizedMessage, DateTimeOffset.UtcNow));
            return true;
        }, cancellationToken);
    }

    private static SecurityPolicy NormalizePolicy(SecurityPolicy? policy)
    {
        policy ??= SecurityPolicy.Default();
        return policy with
        {
            MaximumTokenDays = Math.Clamp(policy.MaximumTokenDays, 1, 365),
            MaximumActiveTokens = Math.Clamp(policy.MaximumActiveTokens, 1, 100),
            RequireHighRiskConfirmation = true
        };
    }

    private static string ExtractPrefix(string token)
    {
        if (!token.StartsWith("palops_", StringComparison.Ordinal) || token.Length > 512) return string.Empty;
        var secondSeparator = token.IndexOf('_', "palops_".Length);
        if (secondSeparator <= "palops_".Length) return string.Empty;
        var prefix = token["palops_".Length..secondSeparator];
        return prefix.Length is >= 6 and <= 32 && prefix.All(static character => char.IsAsciiHexDigit(character)) ? prefix : string.Empty;
    }

    private static ApiTokenView ToView(ApiTokenRecord record) =>
        new(record.Id, record.Name, record.Prefix, record.Scopes, record.CreatedAt, record.ExpiresAt, record.CreatedBy, record.LastUsedAt, record.RevokedAt, record.RevokedBy);

    private static SecurityObservation Observation(string type, string actor, string remoteIp, string outcome, string message, DateTimeOffset now) =>
        new(Guid.NewGuid().ToString("N"), type, string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(), remoteIp.Trim(), outcome.Trim(), message.Trim(), now);
}
