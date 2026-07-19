using System.Globalization;
using PalOps.Web.Events;
using PalOps.Web.SaveGames.Binary;
using PalOps.Web.SaveGames.Index;
using PalOps.Web.SaveGames.Projection;
using PalOps.Web.Settings;

namespace PalOps.Web.SaveGames;

public sealed record SaveIndexTriggerResult(bool Started, string Code, string Message, string? JobId);

public interface ISaveIndexingService
{
    SaveIndexStatus Status { get; }
    Task<SaveIndexTriggerResult> TriggerAsync(string reason, CancellationToken cancellationToken = default);
    Task<bool> CancelAsync(CancellationToken cancellationToken = default);
}

public sealed class SaveIndexingService : ISaveIndexingService, IDisposable
{
    private readonly IServerSettingsStore _settingsStore;
    private readonly ISaveSourceResolver _sourceResolver;
    private readonly IStableSaveSnapshotService _snapshotService;
    private readonly IPalworldSavDecompressor _decompressor;
    private readonly IGvasParser _gvasParser;
    private readonly IPlayerSaveProjector _playerProjector;
    private readonly IWorldSaveProjector _worldProjector;
    private readonly ISaveIndexRepository _repository;
    private readonly IPalOpsEventPublisher _eventPublisher;
    private readonly ILogger<SaveIndexingService> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SaveIndexProgressTracker _progress = new();
    private CancellationTokenSource? _currentCancellation;
    private Task? _currentTask;

    public SaveIndexingService(
        IServerSettingsStore settingsStore,
        ISaveSourceResolver sourceResolver,
        IStableSaveSnapshotService snapshotService,
        IPalworldSavDecompressor decompressor,
        IGvasParser gvasParser,
        IPlayerSaveProjector playerProjector,
        IWorldSaveProjector worldProjector,
        ISaveIndexRepository repository,
        IPalOpsEventPublisher eventPublisher,
        ILogger<SaveIndexingService> logger)
    {
        _settingsStore = settingsStore;
        _sourceResolver = sourceResolver;
        _snapshotService = snapshotService;
        _decompressor = decompressor;
        _gvasParser = gvasParser;
        _playerProjector = playerProjector;
        _worldProjector = worldProjector;
        _repository = repository;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public SaveIndexStatus Status => _progress.Current;

    public async Task<SaveIndexTriggerResult> TriggerAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_currentTask is { IsCompleted: false })
                return new SaveIndexTriggerResult(false, "SAVE_PARSE_ALREADY_RUNNING", "已有存档解析任务正在运行。", Status.CurrentSnapshotId);

            var jobId = $"parse-{SaveClock.BeijingNow():yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            _currentCancellation?.Dispose();
            _currentCancellation = new CancellationTokenSource();
            _progress.Begin(jobId, SaveClock.BeijingNow());
            _currentTask = Task.Run(
                () => ExecuteAsync(jobId, reason, _currentCancellation.Token),
                CancellationToken.None);
            return new SaveIndexTriggerResult(true, "SAVE_PARSE_STARTED", "存档解析任务已启动。", jobId);
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task<bool> CancelAsync(CancellationToken cancellationToken = default)
    {
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_currentTask is not { IsCompleted: false } || _currentCancellation is null) return false;
            _currentCancellation.Cancel();
            return true;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task ExecuteAsync(string jobId, string reason, CancellationToken cancellationToken)
    {
        SaveSnapshotManifest? manifest = null;
        string worldId = string.Empty;
        var startedAt = SaveClock.BeijingNow();
        try
        {
            var settings = await _settingsStore.GetAsync(cancellationToken);
            var source = _sourceResolver.ResolveConfigured(settings.SaveGame);
            worldId = source.WorldId;

            _progress.Report(SaveIndexState.WaitingForStableFile, "waitingForStableFile", 5);
            manifest = await _snapshotService.CreateAsync(source, settings.SaveGame, cancellationToken);
            _progress.Report(SaveIndexState.CopyingSnapshot, "copyingSnapshot", 15);

            var levelFile = manifest.Files.First(file => file.RelativePath.Equals("Level.sav", StringComparison.OrdinalIgnoreCase));
            _progress.Report(SaveIndexState.Decompressing, "decompressingLevel", 22);
            var levelDocument = await ReadDocumentAsync(levelFile.FullPath, cancellationToken);
            _progress.Report(SaveIndexState.ReadingGvas, "readingWorldGvas", 32);
            var world = _worldProjector.Project(levelDocument, levelFile.ModifiedAt);

            var playerFiles = manifest.Files
                .Where(file => file.RelativePath.StartsWith("Players/", StringComparison.OrdinalIgnoreCase)
                               && file.RelativePath.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var playerSeeds = new List<PlayerSaveProjection>(playerFiles.Length);
            var diagnostics = new Dictionary<string, string>(world.Diagnostics, StringComparer.OrdinalIgnoreCase)
            {
                ["triggerReason"] = reason,
                ["playerSaveFiles"] = playerFiles.Length.ToString(CultureInfo.InvariantCulture)
            };

            for (var index = 0; index < playerFiles.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = playerFiles[index];
                var progress = 35 + (int)Math.Floor((index + 1d) / Math.Max(1, playerFiles.Length) * 42d);
                _progress.Report(SaveIndexState.Players, $"players:{index + 1}/{playerFiles.Length}", progress);
                try
                {
                    var document = await ReadDocumentAsync(file.FullPath, cancellationToken);
                    var projected = _playerProjector.Project(document, file.RelativePath);
                    playerSeeds.Add(projected);
                    foreach (var pair in projected.Diagnostics)
                        diagnostics[$"player:{projected.Player.PlayerUid}:{pair.Key}"] = pair.Value;
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or EndOfStreamException)
                {
                    diagnostics[$"playerError:{file.RelativePath}"] = ex.Message;
                    _logger.LogWarning(ex, "Player save {PlayerSave} could not be projected; world indexing continues.", file.RelativePath);
                }
            }

            _progress.Report(SaveIndexState.Inventories, "inventories", 80);
            var merged = MergePlayerDomains(playerSeeds, world, diagnostics, cancellationToken);
            _progress.Report(SaveIndexState.Pals, "pals", 84);
            _progress.Report(SaveIndexState.Guilds, "guilds", 87);
            _progress.Report(SaveIndexState.Bases, "bases", 90);

            var playerMarkers = merged.Players
                .Where(player => player.X.HasValue && player.Y.HasValue)
                .Select(player => new IndexedMapMarker(
                    "player:" + player.PlayerUid,
                    "offlinePlayer",
                    player.Name,
                    player.X!.Value,
                    player.Y!.Value,
                    player.Z ?? 0,
                    "save",
                    manifest.CreatedAt,
                    new Dictionary<string, string> { ["playerUid"] = player.PlayerUid }))
                .ToArray();

            var snapshot = new SaveIndexSnapshot(
                manifest.SnapshotId,
                manifest.WorldId,
                manifest.CreatedAt,
                SaveClock.BeijingNow(),
                manifest.LevelSha256,
                manifest.SourceWorldDirectory,
                merged.Players,
                merged.Items,
                merged.Pals,
                world.Guilds,
                world.Bases,
                world.Markers.Concat(playerMarkers).ToArray(),
                diagnostics);

            _progress.Report(SaveIndexState.WritingIndex, "writingIndex", 95);
            await _repository.PublishAsync(snapshot, cancellationToken);
            _progress.Complete(snapshot.SnapshotId, SaveClock.BeijingNow());
            _logger.LogInformation(
                "Save index {SnapshotId} completed with {Players} players, {Items} items and {Pals} pals.",
                snapshot.SnapshotId,
                snapshot.Players.Count,
                snapshot.Items.Count,
                snapshot.Pals.Count);
            await PublishBestEffortAsync(PalOpsEvent.Create(
                "save-index.completed",
                metadata: new Dictionary<string, object?>
                {
                    ["message"] = "存档解析已完成。",
                    ["snapshotId"] = snapshot.SnapshotId,
                    ["worldId"] = snapshot.WorldId,
                    ["players"] = snapshot.Players.Count,
                    ["items"] = snapshot.Items.Count,
                    ["pals"] = snapshot.Pals.Count,
                    ["guilds"] = snapshot.Guilds.Count,
                    ["bases"] = snapshot.Bases.Count,
                    ["durationMs"] = (SaveClock.BeijingNow() - startedAt).TotalMilliseconds
                }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _progress.Cancel(SaveClock.BeijingNow());
            _logger.LogInformation("Save indexing job {JobId} was cancelled.", jobId);
        }
        catch (Exception ex)
        {
            var completedAt = SaveClock.BeijingNow();
            _progress.Fail(ex.Message, completedAt);
            await _repository.RecordFailureAsync(
                new SaveIndexFailure(manifest?.SnapshotId ?? jobId, worldId, completedAt, ex.Message),
                CancellationToken.None);
            await PublishBestEffortAsync(PalOpsEvent.Create(
                "save-index.failed",
                "error",
                metadata: new Dictionary<string, object?>
                {
                    ["message"] = LimitEventText(ex.Message, 500),
                    ["errorCode"] = ex.GetType().Name,
                    ["snapshotId"] = manifest?.SnapshotId ?? jobId,
                    ["worldId"] = worldId,
                    ["durationMs"] = (completedAt - startedAt).TotalMilliseconds
                }));
            _logger.LogError(ex, "Save indexing job {JobId} failed; previous successful snapshot remains active.", jobId);
        }
        finally
        {
            if (manifest is not null) TryDeleteSnapshot(manifest.SnapshotDirectory);
        }
    }

    private async Task PublishBestEffortAsync(PalOpsEvent palOpsEvent)
    {
        try { await _eventPublisher.PublishAsync(palOpsEvent, CancellationToken.None); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Save index event {EventType} could not be published.", palOpsEvent.EventType);
        }
    }

    private static string LimitEventText(string value, int maximum)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    /// <summary>
    /// Joins each Players/&lt;uid&gt;.sav seed to the authoritative Level.sav domains.
    /// The join keys are player UID, item-container ID, Pal-container ID and Pal
    /// instance ID. No inventory or Pal data is inferred by scanning a player save.
    /// </summary>
    private static PlayerDomainMerge MergePlayerDomains(
        IReadOnlyList<PlayerSaveProjection> seeds,
        WorldSaveProjection world,
        IDictionary<string, string> diagnostics,
        CancellationToken cancellationToken)
    {
        var guildById = world.Guilds.ToDictionary(guild => guild.GuildId, StringComparer.OrdinalIgnoreCase);
        var palsByContainer = world.PalProfiles.Values
            .Where(pal => !string.IsNullOrWhiteSpace(pal.ContainerId))
            .GroupBy(pal => pal.ContainerId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var palsByOwner = world.PalProfiles.Values
            .Where(pal => !string.IsNullOrWhiteSpace(pal.OwnerPlayerUid))
            .GroupBy(pal => pal.OwnerPlayerUid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var characterContainerByInstance = world.CharacterContainers.Values
            .SelectMany(slots => slots)
            .Where(slot => !string.IsNullOrWhiteSpace(slot.InstanceId))
            .GroupBy(slot => slot.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var players = new List<IndexedPlayer>(seeds.Count);
        var items = new List<IndexedItem>();
        var pals = new List<IndexedPal>();
        var matchedProfiles = 0;
        var missingProfiles = 0;
        var matchedItemContainers = 0;
        var missingItemContainers = 0;
        var matchedPalContainers = 0;
        var missingPalContainers = 0;
        var ownerFallbackPals = 0;
        var characterContainerFallbackPals = 0;
        var characterContainerSlotOverrides = 0;

        foreach (var seed in seeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uid = seed.Player.PlayerUid;
            var hasProfile = world.PlayerProfiles.TryGetValue(uid, out var profile);
            if (hasProfile) matchedProfiles++; else missingProfiles++;

            var guildId = profile?.GuildId ?? seed.Player.GuildId;
            var guildName = !string.IsNullOrWhiteSpace(guildId) && guildById.TryGetValue(guildId, out var guild)
                ? guild.Name
                : null;
            var hp = profile?.Hp ?? seed.Player.Hp;
            var shield = profile?.ShieldHp ?? seed.Player.ShieldHp;
            players.Add(seed.Player with
            {
                Name = FirstNonEmpty(profile?.Name, seed.Player.Name, uid),
                Level = profile?.Level ?? seed.Player.Level,
                GuildId = guildId,
                GuildName = guildName,
                Experience = profile?.Experience ?? seed.Player.Experience,
                Hp = hp,
                MaxHp = profile?.MaxHp ?? (hp > 0 ? hp : seed.Player.MaxHp),
                ShieldHp = shield,
                ShieldMaxHp = profile?.ShieldMaxHp ?? (shield > 0 ? shield : seed.Player.ShieldMaxHp),
                FullStomach = profile?.FullStomach ?? seed.Player.FullStomach,
                StatusPoints = profile?.StatusPoints ?? seed.Player.StatusPoints
            });

            foreach (var pair in seed.ItemContainerIds)
            {
                if (!world.ItemContainers.TryGetValue(pair.Value, out var slots))
                {
                    missingItemContainers++;
                    diagnostics[$"missingItemContainer:{uid}:{pair.Key}"] = pair.Value;
                    continue;
                }

                matchedItemContainers++;
                items.AddRange(slots.Select(slot => new IndexedItem(
                    uid,
                    pair.Key,
                    slot.SlotIndex,
                    slot.ItemId,
                    slot.Quantity,
                    slot.Quality,
                    slot.Durability,
                    slot.DynamicItemId)));
            }

            var containerTypeById = seed.PalContainerIds
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.OrdinalIgnoreCase);
            var playerPals = new Dictionary<string, IndexedPal>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in containerTypeById)
            {
                var hasDirectProfiles = palsByContainer.TryGetValue(pair.Key, out var containerPals);
                var hasCharacterContainer = world.CharacterContainers.TryGetValue(pair.Key, out var containerSlots);
                if (!hasDirectProfiles && !hasCharacterContainer)
                {
                    missingPalContainers++;
                    diagnostics[$"missingPalContainer:{uid}:{pair.Value}"] = pair.Key;
                    continue;
                }

                // CharacterContainerSaveData records even an empty party/palbox, so a
                // present container is a successful join even when it has zero Pals.
                matchedPalContainers++;

                if (hasDirectProfiles)
                {
                    foreach (var pal in containerPals!)
                        playerPals[pal.InstanceId] = ToIndexedPal(pal, uid, pair.Value);
                }

                // SlotId is omitted from selected CharacterSaveParameterMap records in
                // Palworld 1.0. CharacterContainerSaveData is the authoritative reverse
                // index from container/slot to character instance, so use it to recover
                // both membership and the real slot index.
                if (hasCharacterContainer)
                {
                    foreach (var slot in containerSlots!)
                    {
                        if (string.IsNullOrWhiteSpace(slot.InstanceId)
                            || !world.PalProfiles.TryGetValue(slot.InstanceId, out var pal))
                            continue;

                        var existed = playerPals.ContainsKey(pal.InstanceId);
                        var needsSlotOverride = pal.SlotIndex != slot.SlotIndex
                                                || !string.Equals(
                                                    pal.ContainerId,
                                                    slot.ContainerId,
                                                    StringComparison.OrdinalIgnoreCase);
                        playerPals[pal.InstanceId] = ToIndexedPal(
                            pal,
                            uid,
                            pair.Value,
                            slot.SlotIndex);

                        if (!existed) characterContainerFallbackPals++;
                        else if (needsSlotOverride) characterContainerSlotOverrides++;
                    }
                }
            }

            // OwnerPlayerUId is only a fallback discriminator. Do not assign every
            // owner-linked character to the player's palbox: workers assigned to bases
            // and other world containers can share the same owner. Include a fallback
            // Pal only when its profile or reverse container slot resolves to one of
            // this player's explicit party/palbox container IDs.
            if (palsByOwner.TryGetValue(uid, out var ownedPals))
            {
                foreach (var pal in ownedPals)
                {
                    if (playerPals.ContainsKey(pal.InstanceId)) continue;

                    string? containerType = null;
                    int? slotIndex = null;
                    if (pal.ContainerId is not null
                        && containerTypeById.TryGetValue(pal.ContainerId, out var explicitType))
                    {
                        containerType = explicitType;
                    }
                    else if (characterContainerByInstance.TryGetValue(pal.InstanceId, out var slot)
                             && containerTypeById.TryGetValue(slot.ContainerId, out var slotType))
                    {
                        containerType = slotType;
                        slotIndex = slot.SlotIndex;
                    }

                    if (containerType is null) continue;
                    playerPals[pal.InstanceId] = ToIndexedPal(pal, uid, containerType, slotIndex);
                    ownerFallbackPals++;
                }
            }

            pals.AddRange(playerPals.Values);
        }

        var uniquePlayers = players
            .GroupBy(player => player.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(player => player.LastSeenAt).First())
            .OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var validPlayerIds = uniquePlayers
            .Select(player => player.PlayerUid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uniqueItems = items
            .Where(item => validPlayerIds.Contains(item.PlayerUid))
            .GroupBy(
                item => $"{item.PlayerUid}|{item.ContainerType}|{item.SlotIndex}|{item.ItemId}|{item.DynamicItemId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ContainerType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SlotIndex)
            .ToArray();
        var uniquePals = pals
            .Where(pal => validPlayerIds.Contains(pal.PlayerUid))
            .GroupBy(pal => pal.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(pal => pal.PlayerUid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pal => pal.ContainerType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pal => pal.SlotIndex)
            .ToArray();

        var expectedItemContainerReferences = seeds.Sum(seed => seed.ItemContainerIds.Values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
        var expectedPalContainerReferences = seeds.Sum(seed => seed.PalContainerIds.Values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());

        // A syntactically valid GVAS document is not enough. Refuse to replace the
        // previous snapshot when authoritative Level.sav domains are populated but
        // every player join misses; that state indicates a schema/projection mismatch.
        if (seeds.Count > 0 && world.PlayerProfiles.Count > 0 && matchedProfiles == 0)
            throw new InvalidDataException(
                "Level.sav 包含玩家档案，但 Players/*.sav 与所有玩家档案均无法按 UID 联结；拒绝发布空玩家详情索引。");

        if (expectedItemContainerReferences > 0
            && world.ItemContainers.Count > 0
            && matchedItemContainers == 0)
            throw new InvalidDataException(
                "玩家存档包含物品容器引用，但与 Level.sav 的所有物品容器均无法联结；拒绝发布空背包索引。");

        if (expectedPalContainerReferences > 0
            && (world.CharacterContainers.Count > 0 || palsByContainer.Count > 0)
            && matchedPalContainers == 0)
            throw new InvalidDataException(
                "玩家存档包含帕鲁容器引用，但与 Level.sav 的所有角色容器均无法联结；拒绝发布空帕鲁索引。");

        diagnostics["expectedItemContainerReferences"] = expectedItemContainerReferences.ToString(CultureInfo.InvariantCulture);
        diagnostics["expectedPalContainerReferences"] = expectedPalContainerReferences.ToString(CultureInfo.InvariantCulture);
        diagnostics["matchedWorldPlayerProfiles"] = matchedProfiles.ToString(CultureInfo.InvariantCulture);
        diagnostics["missingWorldPlayerProfiles"] = missingProfiles.ToString(CultureInfo.InvariantCulture);
        diagnostics["matchedItemContainers"] = matchedItemContainers.ToString(CultureInfo.InvariantCulture);
        diagnostics["missingItemContainers"] = missingItemContainers.ToString(CultureInfo.InvariantCulture);
        diagnostics["matchedPalContainers"] = matchedPalContainers.ToString(CultureInfo.InvariantCulture);
        diagnostics["missingPalContainers"] = missingPalContainers.ToString(CultureInfo.InvariantCulture);
        diagnostics["ownerFallbackPals"] = ownerFallbackPals.ToString(CultureInfo.InvariantCulture);
        diagnostics["characterContainerFallbackPals"] = characterContainerFallbackPals.ToString(CultureInfo.InvariantCulture);
        diagnostics["characterContainerSlotOverrides"] = characterContainerSlotOverrides.ToString(CultureInfo.InvariantCulture);
        diagnostics["indexedPlayers"] = uniquePlayers.Length.ToString(CultureInfo.InvariantCulture);
        diagnostics["indexedItems"] = uniqueItems.Length.ToString(CultureInfo.InvariantCulture);
        diagnostics["indexedPals"] = uniquePals.Length.ToString(CultureInfo.InvariantCulture);

        return new PlayerDomainMerge(uniquePlayers, uniqueItems, uniquePals);
    }

    private static IndexedPal ToIndexedPal(
        WorldPalProfile pal,
        string playerUid,
        string containerType,
        int? slotIndex = null)
        => new(
            pal.InstanceId,
            playerUid,
            pal.PalId,
            pal.Nickname,
            pal.Level,
            pal.Experience,
            pal.Gender,
            pal.IsLucky,
            pal.IsBoss,
            pal.IsTower,
            containerType,
            slotIndex ?? pal.SlotIndex,
            pal.Hp,
            pal.MaxHp,
            pal.HpTalent,
            pal.AttackTalent,
            pal.DefenseTalent,
            pal.WorkSpeed,
            pal.Rank,
            pal.Passives,
            pal.Skills);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private async Task<GvasDocument> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        var payload = await _decompressor.DecompressAsync(stream, cancellationToken);
        return _gvasParser.Parse(payload.Data);
    }

    private void TryDeleteSnapshot(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Temporary save snapshot {SnapshotDirectory} could not be deleted.", path);
        }
    }

    public void Dispose()
    {
        _currentCancellation?.Cancel();
        _currentCancellation?.Dispose();
        _startGate.Dispose();
    }

    private sealed record PlayerDomainMerge(
        IReadOnlyList<IndexedPlayer> Players,
        IReadOnlyList<IndexedItem> Items,
        IReadOnlyList<IndexedPal> Pals);
}
