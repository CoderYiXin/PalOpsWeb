namespace PalOps.Web.AdvancedOperations;

public sealed class AdvancedOperationsValidator
{
    private static readonly HashSet<string> AllowedScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "status.read", "events.write", "incidents.read", "diagnostics.read"
    };

    public string NormalizeSeverity(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "critical" => IncidentSeverity.Critical,
        "high" => IncidentSeverity.High,
        "medium" or "warning" => IncidentSeverity.Medium,
        "low" => IncidentSeverity.Low,
        "information" or "info" or "" => IncidentSeverity.Information,
        _ => throw new ArgumentException("Unsupported incident severity.", nameof(value))
    };

    public string NormalizeTargetType(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "local" => DisasterRecoveryTargetType.Local,
        "unc" or "network" or "network-share" => DisasterRecoveryTargetType.Unc,
        "webdav" or "web-dav" => DisasterRecoveryTargetType.WebDav,
        "s3" or "s3-compatible" or "object-storage" => DisasterRecoveryTargetType.S3Compatible,
        _ => throw new ArgumentException("Unsupported disaster recovery target type.", nameof(value))
    };

    public string ValidateName(string? value, string parameterName, int maximumLength = 120)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        var limit = Math.Clamp(maximumLength, 1, 4000);
        if (normalized.Length < 1 || normalized.Length > limit)
            throw new ArgumentException($"{parameterName} must contain 1 to {limit} characters.", parameterName);
        return normalized;
    }

    public string LimitText(string? value, int maximumLength)
    {
        var normalized = (value ?? string.Empty).Replace("\0", string.Empty).Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    public IReadOnlyList<string> ValidateScopes(IReadOnlyList<string>? scopes)
    {
        var normalized = (scopes ?? []).Select(static scope => scope.Trim().ToLowerInvariant())
            .Where(static scope => scope.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0) throw new ArgumentException("At least one token scope is required.", nameof(scopes));
        var invalid = normalized.Where(scope => !AllowedScopes.Contains(scope)).ToArray();
        if (invalid.Length > 0) throw new ArgumentException($"Unsupported token scope: {string.Join(", ", invalid)}", nameof(scopes));
        return normalized;
    }

    public IReadOnlyList<OperationsPlaybookStep> ValidatePlaybookSteps(IReadOnlyList<OperationsPlaybookStep>? steps)
    {
        if (steps is null || steps.Count == 0) throw new ArgumentException("A playbook requires at least one step.", nameof(steps));
        if (steps.Count > 25) throw new ArgumentException("A playbook cannot exceed 25 steps.", nameof(steps));
        var normalized = steps.OrderBy(static step => step.Order).Select((step, index) =>
        {
            if (!OperationsPlaybookCatalog.IsAllowed(step.Action))
                throw new ArgumentException($"Unsupported playbook action: {step.Action}", nameof(steps));
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in step.Parameters ?? new Dictionary<string, string>())
            {
                var key = pair.Key.Trim();
                if (key.Length == 0) continue;
                parameters[key] = LimitStatic(pair.Value, 1000);
                if (parameters.Count >= 20) break;
            }
            return new OperationsPlaybookStep(index + 1, step.Action.Trim().ToLowerInvariant(), parameters, step.ContinueOnError);
        }).ToArray();
        return normalized;
    }

    public string ValidateEndpoint(string targetType, string? endpoint)
    {
        var normalized = (endpoint ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > 2048) throw new ArgumentException("Target endpoint is required and must be at most 2048 characters.", nameof(endpoint));
        if (targetType == DisasterRecoveryTargetType.Local && !Path.IsPathRooted(normalized))
            throw new ArgumentException("Local target must be an absolute path.", nameof(endpoint));
        if (targetType == DisasterRecoveryTargetType.Unc && !normalized.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("UNC target must start with \\\\.", nameof(endpoint));
        if (targetType is DisasterRecoveryTargetType.WebDav or DisasterRecoveryTargetType.S3Compatible)
        {
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("Remote target must use an absolute HTTPS URL.", nameof(endpoint));
        }
        return normalized;
    }

    public static void RequireConfirmation(string? provided, string expected)
    {
        if (!string.Equals(provided?.Trim(), expected, StringComparison.Ordinal))
            throw new ArgumentException($"Confirmation must exactly match: {expected}", nameof(provided));
    }

    private static string LimitStatic(string? value, int maximumLength)
    {
        var normalized = (value ?? string.Empty).Replace("\0", string.Empty).Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
