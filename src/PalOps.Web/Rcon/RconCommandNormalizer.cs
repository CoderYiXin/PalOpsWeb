namespace PalOps.Web.Rcon;

/// <summary>
/// 将用户输入和旧版快捷命令统一转换为 Palworld RCON 实际接受的无斜杠格式。
/// </summary>
public static class RconCommandNormalizer
{
    public static string Normalize(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalized = command.Trim();
        while (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized[1..].TrimStart();
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("RCON 指令不能为空。", nameof(command));
        }

        return normalized;
    }
}
