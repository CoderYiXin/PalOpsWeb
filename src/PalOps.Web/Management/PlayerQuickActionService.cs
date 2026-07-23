using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.Contracts;
using PalOps.Web.External;
using PalOps.Web.Players;
using PalOps.Web.Rcon;
using PalOps.Web.Settings;

namespace PalOps.Web.Management;

public interface IPlayerQuickActionService
{
    Task<PlayerQuickActionResponse> ExecuteAsync(PlayerQuickActionRequest request, CancellationToken cancellationToken = default);
}

public sealed class PlayerQuickActionService(
    IPlayerAggregationService playerService,
    IPalDefenderApiClient palDefenderApi,
    IServerSettingsStore settingsStore,
    IRconClient rcon,
    IOptions<AppRuntimeOptions> options) : IPlayerQuickActionService
{
    private readonly AppRuntimeOptions _options = options.Value;

    public async Task<PlayerQuickActionResponse> ExecuteAsync(PlayerQuickActionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PlayerIdentifiers is null) throw new ArgumentException("玩家列表不能为空。");

        var players = request.PlayerIdentifiers
            .Select(static value => value?.Trim() ?? string.Empty)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (players.Length is < 1 || players.Length > Math.Clamp(_options.MaxPlayersPerGrant, 1, 12))
        {
            throw new ArgumentException($"每次必须选择 1 到 {Math.Clamp(_options.MaxPlayersPerGrant, 1, 12)} 名玩家。");
        }

        var action = NormalizeAction(request.Action);
        ValidateActionValues(action, request);

        var onlinePlayers = await playerService.GetOnlinePlayersAsync(cancellationToken);
        var onlineByIdentifier = BuildOnlinePlayerMap(onlinePlayers);
        foreach (var player in players)
        {
            if (!onlineByIdentifier.ContainsKey(player)) throw new ArgumentException($"玩家 {player} 当前不在线。");
        }

        var target = request.TargetPlayerIdentifier?.Trim();
        if (action == "teleport-player" && (string.IsNullOrWhiteSpace(target) || !onlineByIdentifier.ContainsKey(target)))
        {
            throw new ArgumentException("传送目标玩家当前不在线。");
        }

        var parameters = new PlayerQuickActionParameters(target, request.X, request.Y, request.Z, request.Amount, request.TechId);
        var results = new PlayerQuickActionResult[players.Length];
        RconConnection? rconConnection = null;
        if (RequiresRcon(action))
        {
            rconConnection = (await settingsStore.GetAsync(cancellationToken)).Rcon;
        }

        await Parallel.ForEachAsync(
            Enumerable.Range(0, players.Length),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(_options.GrantParallelism, 1, 4),
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                var playerIdentifier = players[index];
                try
                {
                    results[index] = action switch
                    {
                        "get-position" => BuildPositionResult(playerIdentifier, onlineByIdentifier[playerIdentifier]),
                        "give-experience" => await ExecuteProgressionAsync(playerIdentifier, request.Amount, null, null, token),
                        "give-tech-points" => await ExecuteProgressionAsync(
                            playerIdentifier,
                            null,
                            request.Amount.HasValue ? checked((int)request.Amount.Value) : null,
                            null,
                            token),
                        "give-ancient-tech-points" => await ExecuteProgressionAsync(
                            playerIdentifier,
                            null,
                            null,
                            request.Amount.HasValue ? checked((int)request.Amount.Value) : null,
                            token),
                        "learn-tech" => await ExecuteLearnTechnologyAsync(playerIdentifier, request.TechId!, token),
                        _ => await ExecuteRconActionAsync(
                            playerIdentifier,
                            PlayerQuickActionCommandBuilder.Build(action, playerIdentifier, parameters),
                            rconConnection!,
                            token)
                    };
                }
                catch (ExternalApiException ex)
                {
                    results[index] = new PlayerQuickActionResult(playerIdentifier, false, ex.Code, ex.Message, string.Empty, 0);
                }
                catch (RconException ex)
                {
                    results[index] = new PlayerQuickActionResult(playerIdentifier, false, ex.Code, ex.Message, string.Empty, 0);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    results[index] = new PlayerQuickActionResult(playerIdentifier, false, "PLAYER_ACTION_FAILED", ex.Message, string.Empty, 0);
                }
            });

        var succeeded = results.Count(static result => result.Success);
        return new PlayerQuickActionResponse(action, players.Length, succeeded, players.Length - succeeded, results);
    }

    private async Task<PlayerQuickActionResult> ExecuteProgressionAsync(
        string playerIdentifier,
        long? experience,
        int? technologyPoints,
        int? ancientTechnologyPoints,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await palDefenderApi.GiveProgressionAsync(
            playerIdentifier,
            new ExternalProgressionGrant(experience, technologyPoints, ancientTechnologyPoints, null),
            cancellationToken);
        stopwatch.Stop();
        return new PlayerQuickActionResult(
            playerIdentifier,
            true,
            "OK",
            "PalDefender REST API 已接受操作。",
            "REST API：执行成功",
            stopwatch.ElapsedMilliseconds);
    }

    private async Task<PlayerQuickActionResult> ExecuteLearnTechnologyAsync(
        string playerIdentifier,
        string technologyId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await palDefenderApi.LearnTechnologyAsync(playerIdentifier, technologyId, cancellationToken);
        stopwatch.Stop();
        var normalized = technologyId.Equals("all", StringComparison.OrdinalIgnoreCase) ? "All" : technologyId;
        return new PlayerQuickActionResult(
            playerIdentifier,
            true,
            "OK",
            normalized == "All" ? "已通过 PalDefender REST API 解锁全部科技。" : $"已通过 PalDefender REST API 解锁科技 {normalized}。",
            $"REST API：Technology={normalized}",
            stopwatch.ElapsedMilliseconds);
    }

    private async Task<PlayerQuickActionResult> ExecuteRconActionAsync(
        string playerIdentifier,
        string command,
        RconConnection connection,
        CancellationToken cancellationToken)
    {
        var first = await rcon.ExecuteAsync(connection, command, cancellationToken);
        var firstInterpretation = RconActionResponseInterpreter.Interpret(first.Response);
        if (firstInterpretation.Success)
        {
            return new PlayerQuickActionResult(
                playerIdentifier,
                true,
                firstInterpretation.Code,
                firstInterpretation.Message,
                first.Response,
                first.ElapsedMilliseconds);
        }

        // PalDefender releases have used both chat-style commands with a leading
        // slash and raw RCON command names. Retry only the precise compatibility
        // case; do not duplicate operations after any other error response.
        if (firstInterpretation.Code == "RCON_UNKNOWN_COMMAND" && command.StartsWith('/'))
        {
            var fallback = await rcon.ExecuteAsync(connection, command[1..], cancellationToken);
            var fallbackInterpretation = RconActionResponseInterpreter.Interpret(fallback.Response);
            return new PlayerQuickActionResult(
                playerIdentifier,
                fallbackInterpretation.Success,
                fallbackInterpretation.Code,
                fallbackInterpretation.Success
                    ? "兼容模式执行成功。"
                    : fallbackInterpretation.Message,
                fallback.Response,
                first.ElapsedMilliseconds + fallback.ElapsedMilliseconds);
        }

        return new PlayerQuickActionResult(
            playerIdentifier,
            false,
            firstInterpretation.Code,
            firstInterpretation.Message,
            first.Response,
            first.ElapsedMilliseconds);
    }

    private static PlayerQuickActionResult BuildPositionResult(string playerIdentifier, PlayerResponse player)
    {
        if (!player.LocationX.HasValue || !player.LocationY.HasValue)
        {
            return new PlayerQuickActionResult(
                playerIdentifier,
                false,
                "POSITION_UNAVAILABLE",
                "玩家在线，但当前 API 数据没有可用坐标。",
                $"数据来源：{player.Source}",
                0);
        }

        var z = player.LocationZ.HasValue
            ? player.LocationZ.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : "未知";
        var response = FormattableString.Invariant(
            $"X={player.LocationX.Value:0.###}, Y={player.LocationY.Value:0.###}, Z={z}；来源={player.Source}");
        return new PlayerQuickActionResult(playerIdentifier, true, "OK", "已读取玩家坐标。", response, 0);
    }

    private static Dictionary<string, PlayerResponse> BuildOnlinePlayerMap(IReadOnlyList<PlayerResponse> onlinePlayers)
    {
        var result = new Dictionary<string, PlayerResponse>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in onlinePlayers)
        {
            if (!string.IsNullOrWhiteSpace(player.UserId)) result[player.UserId] = player;
            if (!string.IsNullOrWhiteSpace(player.PlayerUid)) result[player.PlayerUid] = player;
        }
        return result;
    }

    private static bool RequiresRcon(string action)
        => action is "teleport-player" or "teleport-coordinates" or "give-stat-points";

    private static string NormalizeAction(string? action)
    {
        var normalized = action?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "get-position" or
            "teleport-player" or
            "teleport-coordinates" or
            "give-experience" or
            "give-stat-points" or
            "give-tech-points" or
            "give-ancient-tech-points" or
            "learn-tech" => normalized,
            _ => throw new ArgumentException("不支持的玩家快捷操作。")
        };
    }

    private static void ValidateActionValues(string action, PlayerQuickActionRequest request)
    {
        if (action is "give-experience" && request.Amount is not (> 0 and <= 100_000_000))
            throw new ArgumentException("经验值必须在 1 到 100000000 之间。");

        if ((action is "give-stat-points" or "give-tech-points" or "give-ancient-tech-points")
            && request.Amount is not (> 0 and <= 100_000))
            throw new ArgumentException("点数必须在 1 到 100000 之间。");

        if (action == "teleport-coordinates")
            ValidateTeleportCoordinates(request.X, request.Y, request.Z);

        if (action == "learn-tech" && string.IsNullOrWhiteSpace(request.TechId))
            throw new ArgumentException("请输入科技 ID；输入 all 可解锁全部科技。");
    }

    private static void ValidateTeleportCoordinates(double? x, double? y, double? z)
    {
        // PalDefender /tp expects in-game map coordinates (the values displayed on the M map),
        // not Unreal world coordinates. The current supported maps stay well inside +/- 5000.
        if (!x.HasValue || !double.IsFinite(x.Value) || Math.Abs(x.Value) > 5_000)
            throw new ArgumentException("X 必须是绝对值不超过 5000 的有效地图坐标。");
        if (!y.HasValue || !double.IsFinite(y.Value) || Math.Abs(y.Value) > 5_000)
            throw new ArgumentException("Y 必须是绝对值不超过 5000 的有效地图坐标。");
        if (z.HasValue && (!double.IsFinite(z.Value) || Math.Abs(z.Value) > 100_000))
            throw new ArgumentException("手动 Z 必须是绝对值不超过 100000 的有效高度坐标。");
    }

}
