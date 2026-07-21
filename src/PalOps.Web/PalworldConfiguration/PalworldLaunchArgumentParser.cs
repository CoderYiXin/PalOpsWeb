using System.Text;

namespace PalOps.Web.PalworldConfiguration;

public sealed record PalworldLaunchArgumentParseResult(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlySet<string> Flags,
    IReadOnlyList<PalworldConfigurationDiagnostic> Diagnostics);

public static class PalworldLaunchArgumentParser
{
    private static readonly HashSet<string> KnownValueArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "port", "players", "publicip", "publicport", "logformat", "numberofworkerthreadsserver"
    };
    private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "useperfthreads", "noasyncloadingthread", "usemultithreadfords", "publiclobby"
    };

    public static PalworldLaunchArgumentParseResult Parse(string? arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<PalworldConfigurationDiagnostic>();
        var tokens = Tokenize(arguments ?? string.Empty, out var unclosedQuote);
        if (unclosedQuote)
            diagnostics.Add(new("ARGUMENT_QUOTE_UNCLOSED", PalworldConfigurationSeverity.Error, "launchArguments", "启动参数包含未闭合的双引号。"));
        foreach (var token in tokens)
        {
            if (!token.StartsWith('-')) continue;
            var normalized = token.TrimStart('-');
            if (string.IsNullOrWhiteSpace(normalized)) continue;
            var separator = normalized.IndexOf('=');
            var key = (separator < 0 ? normalized : normalized[..separator]).Trim();
            var value = separator < 0 ? string.Empty : normalized[(separator + 1)..].Trim().Trim('"');
            if (KnownValueArguments.Contains(key))
            {
                if (separator < 0 || string.IsNullOrWhiteSpace(value))
                {
                    diagnostics.Add(new("ARGUMENT_VALUE_REQUIRED", PalworldConfigurationSeverity.Error, $"launchArguments.{key}", $"启动参数 -{key} 需要值。"));
                    continue;
                }
                if (!values.TryAdd(key, value))
                    diagnostics.Add(new("DUPLICATE_ARGUMENT", PalworldConfigurationSeverity.Error, $"launchArguments.{key}", $"启动参数 -{key} 重复。"));
            }
            else if (KnownFlags.Contains(key))
            {
                if (!flags.Add(key))
                    diagnostics.Add(new("DUPLICATE_ARGUMENT", PalworldConfigurationSeverity.Warning, $"launchArguments.{key}", $"启动开关 -{key} 重复。"));
            }
        }
        return new(values, flags, diagnostics);
    }

    private static IReadOnlyList<string> Tokenize(string value, out bool unclosedQuote)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '"')
            {
                quoted = !quoted;
                current.Append(character);
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(character);
        }
        if (current.Length > 0) result.Add(current.ToString());
        unclosedQuote = quoted;
        return result;
    }
}
