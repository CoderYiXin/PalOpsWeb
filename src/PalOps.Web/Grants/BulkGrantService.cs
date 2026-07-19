using Microsoft.Extensions.Options;
using PalOps.Web.Catalog;
using PalOps.Web.Configuration;
using PalOps.Web.Contracts;
using PalOps.Web.External;
using PalOps.Web.Events;
using PalOps.Web.Players;

namespace PalOps.Web.Grants;

public interface IBulkGrantService
{
    Task<BulkGrantResponse> ExecuteAsync(BulkGrantRequest request, CancellationToken cancellationToken = default);
}

public sealed class BulkGrantService(
    IGrantValidator validator,
    IPlayerAggregationService playerService,
    ICatalogService catalogService,
    IPalDefenderApiClient palDefenderApi,
    IOptions<AppRuntimeOptions> options,
    IPalOpsEventPublisher eventPublisher,
    ILogger<BulkGrantService> logger) : IBulkGrantService
{
    private readonly AppRuntimeOptions _options = options.Value;

    public async Task<BulkGrantResponse> ExecuteAsync(BulkGrantRequest request, CancellationToken cancellationToken = default)
    {
        validator.ValidateShape(request);
        var players = request.PlayerIdentifiers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var online = await playerService.GetOnlinePlayersAsync(cancellationToken);
        var onlineIdentifiers = online.SelectMany(static player => new[] { player.UserId, player.PlayerUid })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var player in players)
        {
            if (!onlineIdentifiers.Contains(player))
                throw new GrantValidationException("PLAYER_NOT_ONLINE", $"玩家 {player} 当前不在线。");
        }

        var externalItems = new List<ExternalItemGrant>();
        foreach (var item in request.Items)
        {
            if (!item.Custom && await catalogService.FindAsync("item", item.ItemId, cancellationToken) is null)
                throw new GrantValidationException("ITEM_NOT_IN_CATALOG", $"物品 {item.ItemId} 不在目录中；如确认 ID 正确，请使用“自定义 ID”方式加入。");
            externalItems.Add(new ExternalItemGrant(item.ItemId, item.Count));
        }

        var externalPals = new List<ExternalPalGrant>();
        foreach (var pal in request.Pals)
        {
            if (!pal.Custom && await catalogService.FindAsync("pal", pal.PalId, cancellationToken) is null)
                throw new GrantValidationException("PAL_NOT_IN_CATALOG", $"帕鲁 {pal.PalId} 不在目录中；如确认 ID 正确，请使用“自定义 ID”方式加入。");
            for (var index = 0; index < pal.Count; index++) externalPals.Add(new ExternalPalGrant(pal.PalId, pal.Level));
        }

        var progression = request.Progression is null ? null : new ExternalProgressionGrant(
            request.Progression.Experience,
            request.Progression.TechnologyPoints,
            request.Progression.AncientTechnologyPoints,
            request.Progression.Relics);

        var results = new PlayerGrantResult[players.Length];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, players.Length),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(_options.GrantParallelism, 1, 4), CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var player = players[index];
                try
                {
                    if (externalItems.Count > 0) await palDefenderApi.GiveItemsAsync(player, externalItems, token);
                    if (externalPals.Count > 0) await palDefenderApi.GivePalsAsync(player, externalPals, token);
                    if (progression is not null && HasProgression(progression)) await palDefenderApi.GiveProgressionAsync(player, progression, token);
                    results[index] = new PlayerGrantResult(player, true, "OK", "发放成功。");
                }
                catch (ExternalApiException ex)
                {
                    results[index] = new PlayerGrantResult(player, false, ex.Code, ex.Message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    results[index] = new PlayerGrantResult(player, false, "GRANT_FAILED", ex.Message);
                }
            });

        if (results.Any(static result => result.Success))
        {
            var catalogItemIds = request.Items.Where(static item => !item.Custom).Select(static item => item.ItemId).ToArray();
            var catalogPalIds = request.Pals.Where(static pal => !pal.Custom).Select(static pal => pal.PalId).ToArray();
            await RecordCatalogUsageBestEffortAsync("item", catalogItemIds);
            await RecordCatalogUsageBestEffortAsync("pal", catalogPalIds);
        }

        var succeeded = results.Count(static result => result.Success);
        var response = new BulkGrantResponse(players.Length, succeeded, players.Length - succeeded, results);
        await PublishBestEffortAsync(PalOpsEvent.Create(
            succeeded == players.Length ? "grant.completed" : "grant.failed",
            succeeded == players.Length ? "information" : "warning",
            player: new Dictionary<string, object?>
            {
                ["identifiers"] = players,
                ["targetCount"] = players.Length
            },
            metadata: new Dictionary<string, object?>
            {
                ["message"] = succeeded == players.Length ? "物资发放已完成。" : "部分或全部物资发放失败。",
                ["succeeded"] = succeeded,
                ["failed"] = players.Length - succeeded,
                ["itemKinds"] = request.Items.Count,
                ["palKinds"] = request.Pals.Count
            }));
        return response;
    }

    private async Task PublishBestEffortAsync(PalOpsEvent palOpsEvent)
    {
        try { await eventPublisher.PublishAsync(palOpsEvent, CancellationToken.None); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Grant event {EventType} could not be published.", palOpsEvent.EventType);
        }
    }

    private async Task RecordCatalogUsageBestEffortAsync(string type, IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0) return;

        try
        {
            // The grant has already reached the game at this point. Catalog
            // usage metadata is secondary and must never turn a successful
            // grant into HTTP 500, even if the browser disconnected or the
            // local overrides file is temporarily unavailable.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await catalogService.RecordUsageAsync(type, ids, timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Catalog usage update failed after grant completion. Type={CatalogType}, Count={Count}",
                type,
                ids.Count);
        }
    }

    private static bool HasProgression(ExternalProgressionGrant progression)
        => progression.Experience is > 0 || progression.TechnologyPoints is > 0 || progression.AncientTechnologyPoints is > 0 || progression.Relics?.Count > 0;
}
