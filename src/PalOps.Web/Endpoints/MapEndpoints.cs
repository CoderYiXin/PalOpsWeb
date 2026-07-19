using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.Map;
using PalOps.Web.Players;
using PalOps.Web.SaveGames.Index;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

/// <summary>
/// Exposes only server-runtime map entities and custom-marker management.
/// Fixed tiles, fixed POIs, map metadata, coordinate projection, package import,
/// and raster health checks are owned by the frontend bundle.
/// </summary>
public static class MapEndpoints
{
    public static IEndpointRouteBuilder MapMapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/map").RequireAuthorization().WithTags("Map");

        group.MapGet("/entities", async (
            string? types,
            string? query,
            bool? includeUnresolved,
            ISaveIndexRepository repository,
            IPlayerAggregationService livePlayers,
            ICustomMapMarkerRepository customRepository,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var data = await BuildMapEntitiesAsync(
                types,
                query,
                includeUnresolved ?? true,
                repository,
                livePlayers,
                customRepository,
                cancellationToken);
            return Results.Ok(new ApiResponse<MapEntityPageV1>(data, context.TraceIdentifier, []));
        });

        group.MapGet("/custom-markers", async (
            ICustomMapMarkerRepository repository,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<IReadOnlyList<CustomMapMarkerV1>>(
                await repository.ListAsync(cancellationToken),
                context.TraceIdentifier,
                [])));

        group.MapPost("/custom-markers", async (
            CustomMapMarkerWriteRequest request,
            ICustomMapMarkerRepository repository,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var marker = await repository.CreateAsync(request, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "map.marker-create",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已创建地图标记 {marker.Label}。",
                new
                {
                    marker.Id,
                    marker.X,
                    marker.Y,
                    marker.Z,
                    marker.Category,
                    marker.MapLayer,
                    marker.CoordinateSpace
                });
            return Results.Created(
                $"/api/v1/map/custom-markers/{marker.Id}",
                new ApiResponse<CustomMapMarkerV1>(marker, context.TraceIdentifier, []));
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/custom-markers/{id}", async (
            string id,
            CustomMapMarkerWriteRequest request,
            ICustomMapMarkerRepository repository,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var marker = await repository.UpdateAsync(id, request, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "map.marker-update",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已更新地图标记 {marker.Label}。",
                new
                {
                    marker.Id,
                    marker.X,
                    marker.Y,
                    marker.Z,
                    marker.Category,
                    marker.MapLayer,
                    marker.CoordinateSpace
                });
            return Results.Ok(new ApiResponse<CustomMapMarkerV1>(
                marker,
                context.TraceIdentifier,
                []));
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/custom-markers/{id}", async (
            string id,
            ICustomMapMarkerRepository repository,
            IAuditLogService audit,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await repository.DeleteAsync(id, cancellationToken);
            await audit.WriteBestEffortAsync(
                loggerFactory.CreateLogger("PalOps.Audit"),
                "map.marker-delete",
                "success",
                EndpointHelpers.RemoteIp(context),
                $"已删除地图标记 {id}。",
                new { id });
            return Results.NoContent();
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();

        return endpoints;
    }

    private static async Task<MapEntityPageV1> BuildMapEntitiesAsync(
        string? types,
        string? query,
        bool includeUnresolved,
        ISaveIndexRepository repository,
        IPlayerAggregationService livePlayers,
        ICustomMapMarkerRepository customRepository,
        CancellationToken cancellationToken)
    {
        var requestedTypes = ParseEntityTypes(types);
        var includeAll = requestedTypes.Count == 0;
        var includePlayers = includeAll || requestedTypes.Contains("player");
        var includeBases = includeAll || requestedTypes.Contains("guildbase");
        var includeCustom = includeAll || requestedTypes.Contains("custom");
        var includeSnapshot = includePlayers || includeBases;

        var snapshotTask = includeSnapshot
            ? repository.GetCurrentAsync(cancellationToken)
            : Task.FromResult<SaveIndexSnapshot?>(null);
        var livePlayersTask = includePlayers
            ? GetOnlinePlayersSafeAsync(livePlayers, cancellationToken)
            : Task.FromResult<IReadOnlyList<PlayerResponse>>([]);
        var customMarkersTask = includeCustom
            ? customRepository.ListAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<CustomMapMarkerV1>>([]);

        await Task.WhenAll(snapshotTask, livePlayersTask, customMarkersTask);
        var snapshot = await snapshotTask;
        var onlinePlayers = await livePlayersTask;
        var customMarkers = await customMarkersTask;
        var entities = new Dictionary<string, MapEntityV1>(StringComparer.OrdinalIgnoreCase);

        if (snapshot is not null)
        {
            foreach (var marker in snapshot.MapMarkers)
            {
                var normalizedType = NormalizeEntityType(marker.Type);
                var includeMarker = normalizedType switch
                {
                    "player" => includePlayers,
                    "guildbase" => includeBases,
                    _ => false
                };
                if (!includeMarker)
                    continue;

                entities[marker.Id] = CreateRuntimeEntity(
                    marker.Id,
                    marker.Type,
                    marker.Label,
                    marker.X,
                    marker.Y,
                    marker.Z,
                    marker.Source,
                    marker.SourceAt,
                    DateTimeOffset.UtcNow - marker.SourceAt > TimeSpan.FromHours(2),
                    marker.Metadata,
                    GetMetadata(marker.Metadata, "coordinateSpace"),
                    GetMetadata(marker.Metadata, "mapLayer"));
            }

            if (includeBases)
            {
                var guildNames = snapshot.Guilds.ToDictionary(
                    guild => guild.GuildId,
                    guild => guild.Name,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var baseCamp in snapshot.Bases)
                {
                    var id = "base:" + baseCamp.BaseId;
                    guildNames.TryGetValue(baseCamp.GuildId, out var guildName);
                    var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["baseId"] = baseCamp.BaseId,
                        ["guildId"] = baseCamp.GuildId,
                        ["guildName"] = guildName ?? string.Empty,
                        ["associationType"] = baseCamp.AssociationType,
                        ["positionSource"] = baseCamp.PositionSource,
                        ["workerCount"] = baseCamp.WorkerCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["mapObjectCount"] = baseCamp.MapObjectCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    };
                    entities[id] = baseCamp.PositionResolved && baseCamp.X.HasValue && baseCamp.Y.HasValue && baseCamp.Z.HasValue
                        ? CreateRuntimeEntity(
                            id,
                            "guildBase",
                            string.IsNullOrWhiteSpace(guildName) ? baseCamp.BaseId : guildName,
                            baseCamp.X.Value,
                            baseCamp.Y.Value,
                            baseCamp.Z.Value,
                            "save",
                            snapshot.ParsedAt,
                            DateTimeOffset.UtcNow - snapshot.ParsedAt > TimeSpan.FromHours(2),
                            metadata,
                            "world",
                            null)
                        : CreateInvalidEntity(
                            id,
                            "guildBase",
                            string.IsNullOrWhiteSpace(guildName) ? baseCamp.BaseId : guildName,
                            baseCamp.X ?? 0,
                            baseCamp.Y ?? 0,
                            baseCamp.Z ?? 0,
                            "save",
                            snapshot.ParsedAt,
                            metadata,
                            baseCamp.AssociationReason ?? "base-coordinate-unavailable");
                }
            }
        }

        if (includePlayers)
        {
            var observedAt = DateTimeOffset.UtcNow;
            foreach (var player in onlinePlayers)
            {
                if (!player.LocationX.HasValue || !player.LocationY.HasValue)
                    continue;

                var id = "player:" + player.PlayerUid;
                entities[id] = CreateRuntimeEntity(
                    id,
                    "onlinePlayer",
                    player.Name,
                    player.LocationX.Value,
                    player.LocationY.Value,
                    player.LocationZ ?? 0,
                    "officialRest",
                    observedAt,
                    false,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["playerUid"] = player.PlayerUid,
                        ["userId"] = player.UserId,
                        ["guildName"] = player.GuildName,
                        ["online"] = "true"
                    },
                    "world",
                    null);
            }
        }

        if (includeCustom)
        {
            foreach (var marker in customMarkers)
            {
                entities["custom:" + marker.Id] = CreateRuntimeEntity(
                    "custom:" + marker.Id,
                    "custom",
                    marker.Label,
                    marker.X,
                    marker.Y,
                    marker.Z,
                    "custom",
                    marker.UpdatedAt,
                    false,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["customId"] = marker.Id,
                        ["category"] = marker.Category,
                        ["description"] = marker.Description,
                        ["coordinateWarning"] = marker.CoordinateWarning ?? string.Empty
                    },
                    marker.CoordinateSpace,
                    marker.MapLayer);
            }
        }

        var needle = query?.Trim();
        var filtered = entities.Values
            .Where(entity => requestedTypes.Count == 0 || requestedTypes.Contains(NormalizeEntityType(entity.Type)))
            .Where(entity => string.IsNullOrWhiteSpace(needle)
                || entity.Label.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || entity.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || entity.Metadata.Any(pair => pair.Value.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entity => entity.Type)
            .ThenBy(entity => entity.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resolved = filtered.Count(entity => entity.PlacementStatus == "resolved");
        var unresolved = filtered.Length - resolved;
        var items = includeUnresolved
            ? filtered
            : filtered.Where(entity => entity.PlacementStatus == "resolved").ToArray();
        return new MapEntityPageV1(items, filtered.Length, resolved, unresolved, DateTimeOffset.UtcNow);
    }

    private static MapEntityV1 CreateRuntimeEntity(
        string id,
        string type,
        string label,
        double x,
        double y,
        double z,
        string source,
        DateTimeOffset sourceAt,
        bool stale,
        IReadOnlyDictionary<string, string>? metadata,
        string? coordinateSpace,
        string? mapLayer)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
        {
            return CreateInvalidEntity(
                id,
                type,
                label,
                x,
                y,
                z,
                source,
                sourceAt,
                metadata,
                "non-finite-coordinate");
        }

        var normalizedSpace = coordinateSpace?.Trim().ToLowerInvariant() switch
        {
            "game-map" => "game-map",
            "legacy-inferred" => InferLegacyCoordinateSpace(x, y),
            _ => "world"
        };
        var normalizedLayer = string.IsNullOrWhiteSpace(mapLayer) ? null : mapLayer.Trim();
        var isDirectMapCoordinate = normalizedSpace == "game-map" && normalizedLayer is not null;
        return new MapEntityV1(
            id,
            type,
            label,
            x,
            y,
            z,
            isDirectMapCoordinate ? x : null,
            isDirectMapCoordinate ? y : null,
            normalizedLayer,
            normalizedSpace,
            "resolved",
            isDirectMapCoordinate ? "high" : "medium",
            isDirectMapCoordinate ? "frontend-direct-map-coordinate" : "frontend-local-projection",
            source,
            sourceAt,
            stale,
            metadata ?? new Dictionary<string, string>());
    }

    private static MapEntityV1 CreateInvalidEntity(
        string id,
        string type,
        string label,
        double x,
        double y,
        double z,
        string source,
        DateTimeOffset sourceAt,
        IReadOnlyDictionary<string, string>? metadata,
        string reason) =>
        new(
            id,
            type,
            label,
            x,
            y,
            z,
            null,
            null,
            null,
            "world",
            "invalid-coordinate",
            "none",
            reason,
            source,
            sourceAt,
            false,
            metadata ?? new Dictionary<string, string>());

    private static async Task<IReadOnlyList<PlayerResponse>> GetOnlinePlayersSafeAsync(
        IPlayerAggregationService livePlayers,
        CancellationToken cancellationToken)
    {
        try
        {
            return await livePlayers.GetOnlinePlayersAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static string InferLegacyCoordinateSpace(double x, double y) =>
        Math.Abs(x) > 10_000 || Math.Abs(y) > 10_000 ? "world" : "game-map";

    private static HashSet<string> ParseEntityTypes(string? types)
    {
        if (string.IsNullOrWhiteSpace(types))
            return new(StringComparer.OrdinalIgnoreCase);
        return types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeEntityType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeEntityType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "player" or "players" or "onlineplayer" or "offlineplayer" => "player",
        "guildbase" or "base" or "bases" => "guildbase",
        "custom" or "marker" or "markers" => "custom",
        var normalized => normalized
    };

    private static string? GetMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        string key)
    {
        if (metadata is null)
            return null;
        return metadata.FirstOrDefault(pair =>
            pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    }
}
