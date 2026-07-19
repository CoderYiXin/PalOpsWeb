using System.Text;

namespace PalOps.Web.Rcon;

public static class RconBase64Codec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string EncodeCommand(string command)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(command));

    public static string DecodeResponse(string response)
    {
        var normalized = response.Trim();
        if (normalized.Length == 0) return string.Empty;
        var bytes = Convert.FromBase64String(normalized);
        return StrictUtf8.GetString(bytes);
    }

    /// <summary>
    /// PalDefender 的 Base64 RCON 模式只保证命令能够以 Base64 形式传入。
    /// 部分扩展命令（例如 version、getrconcmds、settime）会直接返回 JSON 或普通文本，
    /// 而原生 Palworld RCON 命令通常返回 Base64。这里先进行严格 Base64 + UTF-8 解码，
    /// 解码失败时仅接受可显示的普通文本，避免把二进制或损坏数据伪装成有效响应。
    /// </summary>
    public static string DecodeResponseOrPlainText(string response)
    {
        var normalized = response.Trim();
        if (normalized.Length == 0) return string.Empty;

        try
        {
            var decoded = DecodeResponse(normalized);
            if (IsDisplayableText(decoded)) return decoded;
        }
        catch (FormatException)
        {
            // 继续检查 PalDefender 是否返回了未编码的 JSON/普通文本。
        }
        catch (DecoderFallbackException)
        {
            // Base64 字节不是有效 UTF-8，不作为有效的 Base64 RCON 响应。
        }

        if (IsDisplayableText(normalized)) return normalized;

        throw new FormatException("RCON 响应既不是有效的 UTF-8 Base64，也不是可显示的普通文本。");
    }

    private static bool IsDisplayableText(string value)
    {
        foreach (var character in value)
        {
            if (character == '\uFFFD' || character == '\0') return false;
            if (char.IsControl(character) && character is not ('\r' or '\n' or '\t')) return false;
        }

        return true;
    }
}
