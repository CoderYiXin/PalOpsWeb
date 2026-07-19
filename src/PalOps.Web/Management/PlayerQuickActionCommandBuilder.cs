using System.Globalization;
using System.Text.RegularExpressions;

namespace PalOps.Web.Management;

public sealed record PlayerQuickActionParameters(
    string? TargetPlayerIdentifier,
    double? X,
    double? Y,
    double? Z,
    long? Amount,
    string? TechId);

/// <summary>
/// Builds the exact PalDefender RCON command for a validated player quick action.
/// The browser never concatenates raw commands; all command construction remains server-side.
/// </summary>
public static class PlayerQuickActionCommandBuilder
{
    private static readonly Regex SafeTokenPattern = new(
        "^[A-Za-z0-9_.:-]{1,128}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Build(string action, string playerIdentifier, PlayerQuickActionParameters parameters)
    {
        var actionName = action?.Trim().ToLowerInvariant() ?? string.Empty;
        var player = ValidateToken(playerIdentifier, "玩家标识");

        return actionName switch
        {
            "get-position" => $"/getpos {player}",
            "teleport-player" => $"/tp {player} {ValidateToken(parameters.TargetPlayerIdentifier, "目标玩家标识")}",
            "teleport-coordinates" => $"/tp {player} {FormatCoordinate(parameters.X, "X")} {FormatCoordinate(parameters.Y, "Y")} {FormatCoordinate(parameters.Z, "Z")}",
            "give-experience" => $"/give_exp {player} {RequireAmount(parameters.Amount)}",
            "give-stat-points" => $"/givestats {player} {RequireAmount(parameters.Amount)}",
            "give-tech-points" => $"/givetechpoints {player} {RequireAmount(parameters.Amount)}",
            "give-ancient-tech-points" => $"/givebosstechpoints {player} {RequireAmount(parameters.Amount)}",
            "learn-tech" => $"/learntech {player} {ValidateToken(parameters.TechId, "科技 ID")}",
            _ => throw new ArgumentException("不支持的玩家快捷操作。", nameof(action))
        };
    }

    private static string ValidateToken(string? value, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!SafeTokenPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"{label}只能包含字母、数字、下划线、连字符、冒号和点。");
        }

        return normalized;
    }

    private static long RequireAmount(long? amount)
        => amount is > 0 ? amount.Value : throw new ArgumentException("数量必须大于 0。");

    private static string FormatCoordinate(double? value, string axis)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || Math.Abs(value.Value) > 10_000_000)
        {
            throw new ArgumentException($"{axis} 坐标必须是绝对值不超过 10000000 的有效数字。");
        }

        return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
