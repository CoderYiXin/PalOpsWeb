using System.Text;

namespace PalOps.Web.PalworldConfiguration;

public sealed class PalworldSettingsDocument
{
    private readonly List<KeyValuePair<string, string>> _entries;

    internal PalworldSettingsDocument(IEnumerable<KeyValuePair<string, string>> entries)
    {
        _entries = entries.ToList();
    }

    public IReadOnlyList<KeyValuePair<string, string>> Entries => _entries;

    public string? GetRawValue(string key)
    {
        for (var index = _entries.Count - 1; index >= 0; index--)
        {
            if (_entries[index].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return _entries[index].Value;
        }
        return null;
    }

    public void SetRawValue(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        for (var index = 0; index < _entries.Count; index++)
        {
            if (!_entries[index].Key.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            _entries[index] = new KeyValuePair<string, string>(_entries[index].Key, value.Trim());
            return;
        }
        _entries.Add(new KeyValuePair<string, string>(key.Trim(), value.Trim()));
    }

    public IReadOnlyDictionary<string, string> ToDictionary()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries) values[entry.Key] = entry.Value;
        return values;
    }
}

public sealed class PalworldSettingsIniCodec
{
    public const string SectionName = "/Script/Pal.PalGameWorldSettings";
    private const string OptionPrefix = "OptionSettings=";

    public PalworldSettingsDocument Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidDataException("PalWorldSettings.ini 内容为空。");
        if (content.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("PalWorldSettings.ini 超过 2 MiB 限制。");

        var normalized = NormalizeNewLines(content).TrimStart('\uFEFF');
        var section = $"[{SectionName}]";
        var sectionIndex = normalized.IndexOf(section, StringComparison.OrdinalIgnoreCase);
        if (sectionIndex < 0)
            throw new InvalidDataException($"缺少配置节 {section}。");

        var optionIndex = normalized.IndexOf(OptionPrefix, sectionIndex + section.Length, StringComparison.OrdinalIgnoreCase);
        if (optionIndex < 0)
            throw new InvalidDataException("缺少 OptionSettings 配置。");
        var openIndex = normalized.IndexOf('(', optionIndex + OptionPrefix.Length);
        if (openIndex < 0)
            throw new InvalidDataException("OptionSettings 缺少左括号。");
        var closeIndex = FindMatchingParenthesis(normalized, openIndex);
        if (closeIndex < 0)
            throw new InvalidDataException("OptionSettings 括号未闭合。");

        var trailing = normalized[(closeIndex + 1)..].Trim();
        if (trailing.Length > 0 && !trailing.StartsWith(';') && !trailing.StartsWith('#'))
            throw new InvalidDataException("OptionSettings 后存在无法识别的内容。");

        var body = normalized[(openIndex + 1)..closeIndex];
        var entries = SplitTopLevel(body)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Select(ParseEntry)
            .ToArray();
        if (entries.Length == 0)
            throw new InvalidDataException("OptionSettings 未包含任何参数。");
        return new PalworldSettingsDocument(entries);
    }

    public string Serialize(PalworldSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        builder.Append('[').Append(SectionName).AppendLine("]");
        builder.Append(OptionPrefix).Append('(');
        for (var index = 0; index < document.Entries.Count; index++)
        {
            if (index > 0) builder.Append(',');
            builder.Append(document.Entries[index].Key).Append('=').Append(document.Entries[index].Value);
        }
        builder.Append(')').AppendLine();
        return builder.ToString();
    }

    public static string Quote(string value) =>
        '"' + (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    public static string Unquote(string raw)
    {
        var value = raw.Trim();
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return value;
        var builder = new StringBuilder(value.Length - 2);
        var escaped = false;
        for (var index = 1; index < value.Length - 1; index++)
        {
            var character = value[index];
            if (escaped)
            {
                builder.Append(character);
                escaped = false;
            }
            else if (character == '\\') escaped = true;
            else builder.Append(character);
        }
        if (escaped) builder.Append('\\');
        return builder.ToString();
    }

    private static int FindMatchingParenthesis(string value, int openIndex)
    {
        var depth = 0;
        var quoted = false;
        var escaped = false;
        for (var index = openIndex; index < value.Length; index++)
        {
            var character = value[index];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') quoted = false;
                continue;
            }
            if (character == '"') quoted = true;
            else if (character == '(') depth++;
            else if (character == ')' && --depth == 0) return index;
        }
        return -1;
    }

    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var start = 0;
        var depth = 0;
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') quoted = false;
                continue;
            }
            if (character == '"') quoted = true;
            else if (character == '(') depth++;
            else if (character == ')')
            {
                depth--;
                if (depth < 0) throw new InvalidDataException("OptionSettings 包含多余的右括号。");
            }
            else if (character == ',' && depth == 0)
            {
                yield return body[start..index].Trim();
                start = index + 1;
            }
        }
        if (quoted) throw new InvalidDataException("OptionSettings 字符串引号未闭合。");
        if (depth != 0) throw new InvalidDataException("OptionSettings 嵌套括号未闭合。");
        yield return body[start..].Trim();
    }

    private static KeyValuePair<string, string> ParseEntry(string token)
    {
        var separator = FindTopLevelEquals(token);
        if (separator <= 0 || separator == token.Length - 1)
            throw new InvalidDataException($"无法解析参数：{token}");
        var key = token[..separator].Trim();
        var value = token[(separator + 1)..].Trim();
        if (key.Any(static character => !(char.IsLetterOrDigit(character) || character == '_')))
            throw new InvalidDataException($"参数名称无效：{key}");
        return new KeyValuePair<string, string>(key, value);
    }

    private static int FindTopLevelEquals(string token)
    {
        var depth = 0;
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < token.Length; index++)
        {
            var character = token[index];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') quoted = false;
                continue;
            }
            if (character == '"') quoted = true;
            else if (character == '(') depth++;
            else if (character == ')') depth--;
            else if (character == '=' && depth == 0) return index;
        }
        return -1;
    }

    private static string NormalizeNewLines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
