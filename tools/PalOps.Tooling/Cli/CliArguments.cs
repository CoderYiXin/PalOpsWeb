using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Cli;

public sealed class CliArguments
{
    private readonly Dictionary<string, string?> _options;

    private CliArguments(string commandPath, Dictionary<string, string?> options)
    {
        CommandPath = commandPath;
        _options = options;
    }

    public string CommandPath { get; }

    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            throw ToolExitException.Usage(
                "缺少命令。用法：PalOps.Tooling <map|catalog|docs|release> <command> [--option value]。");

        if (args[0].StartsWith("-", StringComparison.Ordinal) ||
            args[1].StartsWith("-", StringComparison.Ordinal))
            throw ToolExitException.Usage("命令必须位于选项之前。");

        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 2; index < args.Count; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
                throw ToolExitException.Usage($"无法识别参数“{token}”。选项必须使用 --name [value] 格式。");

            var name = token[2..];
            if (!options.TryAdd(name, null))
                throw ToolExitException.Usage($"选项 --{name} 不能重复。");

            if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                options[name] = args[++index];
        }

        return new CliArguments($"{args[0].ToLowerInvariant()} {args[1].ToLowerInvariant()}", options);
    }

    public bool HasFlag(string name)
    {
        if (!_options.TryGetValue(name, out var value))
            return false;
        if (value is not null)
            throw ToolExitException.Usage($"选项 --{name} 不接受值。");
        return true;
    }

    public string? GetOptional(string name)
    {
        if (!_options.TryGetValue(name, out var value))
            return null;
        if (string.IsNullOrWhiteSpace(value))
            throw ToolExitException.Usage($"选项 --{name} 需要一个值。");
        return value;
    }

    public string? GetOptionalPath(string name)
    {
        var value = GetOptional(name);
        return value is null ? null : Path.GetFullPath(value);
    }

    public int GetInt(string name, int defaultValue, int minimum, int maximum)
    {
        var value = GetOptional(name);
        if (value is null)
            return defaultValue;
        if (!int.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ||
            parsed < minimum ||
            parsed > maximum)
            throw ToolExitException.Usage($"选项 --{name} 必须是 {minimum} 到 {maximum} 之间的整数。");
        return parsed;
    }

    public void EnsureOnly(params string[] allowed)
    {
        var valid = allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = _options.Keys.FirstOrDefault(key => !valid.Contains(key));
        if (unknown is not null)
            throw ToolExitException.Usage($"当前命令不支持选项 --{unknown}。");
    }
}
