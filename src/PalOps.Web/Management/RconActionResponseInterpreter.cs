namespace PalOps.Web.Management;

public sealed record RconActionInterpretation(bool Success, string Code, string Message);

/// <summary>
/// Converts textual PalDefender RCON output into a business result. A network
/// round trip alone is not proof that the command was accepted.
/// </summary>
public static class RconActionResponseInterpreter
{
    private static readonly (string Fragment, string Code, string Message)[] FailurePatterns =
    [
        ("unknown command", "RCON_UNKNOWN_COMMAND", "服务器不支持该 RCON 指令。"),
        ("command not found", "RCON_UNKNOWN_COMMAND", "服务器不支持该 RCON 指令。"),
        ("invalid command", "RCON_UNKNOWN_COMMAND", "服务器不支持该 RCON 指令。"),
        ("player not found", "PLAYER_NOT_FOUND", "服务器未找到目标玩家。"),
        ("user not found", "PLAYER_NOT_FOUND", "服务器未找到目标玩家。"),
        ("invalid argument", "RCON_INVALID_ARGUMENT", "服务器拒绝了指令参数。"),
        ("invalid parameter", "RCON_INVALID_ARGUMENT", "服务器拒绝了指令参数。"),
        ("permission denied", "RCON_PERMISSION_DENIED", "PalDefender 拒绝执行该指令。"),
        ("not permitted", "RCON_PERMISSION_DENIED", "PalDefender 拒绝执行该指令。"),
        ("failed", "RCON_COMMAND_FAILED", "RCON 指令执行失败。"),
        ("error", "RCON_COMMAND_FAILED", "RCON 指令返回错误。")
    ];

    public static RconActionInterpretation Interpret(string? response)
    {
        var normalized = response?.Trim() ?? string.Empty;
        foreach (var pattern in FailurePatterns)
        {
            if (normalized.Contains(pattern.Fragment, StringComparison.OrdinalIgnoreCase))
            {
                return new RconActionInterpretation(false, pattern.Code, pattern.Message);
            }
        }

        return string.IsNullOrWhiteSpace(normalized)
            ? new RconActionInterpretation(false, "RCON_NO_RESPONSE", "服务器未返回执行结果，无法确认操作是否生效。")
            : new RconActionInterpretation(true, "OK", "服务器已接受指令。");
    }

    public static bool IsUnknownCommand(string? response)
        => Interpret(response).Code == "RCON_UNKNOWN_COMMAND";
}
