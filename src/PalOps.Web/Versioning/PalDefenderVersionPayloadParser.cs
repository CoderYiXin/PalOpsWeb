using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PalOps.Web.Versioning;

internal static partial class PalDefenderVersionPayloadParser
{
    private static readonly HashSet<string> DirectVersionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "paldefenderversion",
        "pdversion",
        "paldefender"
    };

    private static readonly HashSet<string> ContainerKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "data",
        "result",
        "versions",
        "components",
        "version",
        "versioninfo",
        "server"
    };

    public static string Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim().Trim('\uFEFF');
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return TryExtract(document.RootElement, 0, false, out var version) ? version : string.Empty;
        }
        catch (JsonException)
        {
            return TryNormalizeVersion(trimmed.Trim('"'), out var version) ? version : string.Empty;
        }
    }

    private static bool TryExtract(JsonElement element, int depth, bool palDefenderContext, out string version)
    {
        version = string.Empty;
        if (depth > 8) return false;

        if (element.ValueKind == JsonValueKind.String)
            return palDefenderContext && TryNormalizeVersion(element.GetString(), out version);

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray().Take(50))
            {
                if (TryExtract(item, depth + 1, palDefenderContext, out version)) return true;
            }
            return false;
        }

        if (element.ValueKind != JsonValueKind.Object) return false;
        if (TryComposeVersionObject(element, out version)) return palDefenderContext;

        foreach (var property in element.EnumerateObject())
        {
            var key = NormalizeKey(property.Name);
            if (!DirectVersionKeys.Contains(key)) continue;
            if (TryExtractVersionValue(property.Value, depth + 1, true, out version)) return true;
        }

        // Some releases return a minimal { "version": "x.y.z" } payload.
        // Accept only a dotted semantic version so API schema values such as integer 1 are never mistaken for PalDefender.
        foreach (var property in element.EnumerateObject())
        {
            var key = NormalizeKey(property.Name);
            if (key is not ("version" or "currentversion" or "tag")) continue;
            if (TryExtractVersionValue(property.Value, depth + 1, true, out version))
            {
                return true;
            }
        }

        var componentIsPalDefender = ObjectIdentifiesPalDefender(element);
        if (componentIsPalDefender)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = NormalizeKey(property.Name);
                if (key is "version" or "currentversion" or "tag" or "value"
                    && TryExtractVersionValue(property.Value, depth + 1, true, out version))
                {
                    return true;
                }
            }
            if (TryComposeVersionObject(element, out version)) return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            var key = NormalizeKey(property.Name);
            if (ContainerKeys.Contains(key)
                && TryExtract(property.Value, depth + 1, palDefenderContext, out version))
            {
                return true;
            }
        }

        if (palDefenderContext)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = NormalizeKey(property.Name);
                if (key is "version" or "currentversion" or "tag" or "value"
                    && TryExtractVersionValue(property.Value, depth + 1, true, out version))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryExtractVersionValue(
        JsonElement value,
        int depth,
        bool palDefenderContext,
        out string version)
    {
        version = string.Empty;
        if (value.ValueKind == JsonValueKind.String)
            return TryNormalizeVersion(value.GetString(), out version);
        if (value.ValueKind == JsonValueKind.Object && TryComposeVersionObject(value, out version))
            return true;
        return TryExtract(value, depth, palDefenderContext, out version);
    }

    private static bool ObjectIdentifiesPalDefender(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = NormalizeKey(property.Name);
            if (key is not ("name" or "component" or "product" or "module" or "id")) continue;
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var value = NormalizeKey(property.Value.GetString() ?? string.Empty);
            if (value.Contains("paldefender", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool TryComposeVersionObject(JsonElement element, out string version)
    {
        version = string.Empty;
        if (element.ValueKind != JsonValueKind.Object) return false;
        int? major = null;
        int? minor = null;
        int? patch = null;
        int? revision = null;
        foreach (var property in element.EnumerateObject())
        {
            var key = NormalizeKey(property.Name);
            var number = ReadNonNegativeInteger(property.Value);
            if (!number.HasValue) continue;
            switch (key)
            {
                case "major": major = number; break;
                case "minor": minor = number; break;
                case "patch": patch = number; break;
                case "revision":
                case "build": revision = number; break;
            }
        }
        if (!major.HasValue || !minor.HasValue) return false;
        version = revision.HasValue
            ? $"{major.Value}.{minor.Value}.{patch.GetValueOrDefault()}.{revision.Value}"
            : $"{major.Value}.{minor.Value}.{patch.GetValueOrDefault()}";
        return true;
    }

    private static int? ReadNonNegativeInteger(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0)
            return number;
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number)
            && number >= 0)
        {
            return number;
        }
        return null;
    }

    private static bool TryNormalizeVersion(string? value, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = SemanticVersionPattern().Match(value.Trim());
        if (!match.Success) return false;
        version = match.Value.TrimStart('v', 'V');
        return true;
    }

    private static string NormalizeKey(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    [GeneratedRegex(@"[vV]?\d+\.\d+(?:\.\d+){0,2}(?:[-+][0-9A-Za-z.-]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();
}
