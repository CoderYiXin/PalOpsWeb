using System.Text.Json;

namespace PalOps.Web.Settings;

internal static class CredentialInputNormalizer
{
    public static string NormalizePalDefenderToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..].Trim();
        }

        if (normalized.Length >= 2 &&
            ((normalized[0] == '"' && normalized[^1] == '"') ||
             (normalized[0] == '\'' && normalized[^1] == '\'')))
        {
            normalized = normalized[1..^1].Trim();
        }

        if (normalized.StartsWith('{') && normalized.EndsWith('}'))
        {
            try
            {
                using var document = JsonDocument.Parse(normalized);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!property.Name.Equals("Token", StringComparison.OrdinalIgnoreCase) ||
                        property.Value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    return property.Value.GetString()?.Trim() ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // Keep the original text so PalDefender can return a precise authentication error.
            }
        }

        return normalized;
    }
}
