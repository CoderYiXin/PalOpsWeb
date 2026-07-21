using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using PalOps.Web.External;
using PalOps.Web.Management;
using PalOps.Web.Rcon;
using PalOps.Web.Settings;

namespace PalOps.Web.PlayerDiscipline;

public interface IPlayerDisciplineService
{
    Task<PlayerDisciplineDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task AddWhitelistAsync(WhitelistWriteRequest request, string actor, CancellationToken cancellationToken = default);
    Task RemoveWhitelistAsync(string userId, string actor, CancellationToken cancellationToken = default);
    Task BanAsync(BanWriteRequest request, string actor, CancellationToken cancellationToken = default);
    Task UnbanAsync(string identifier, string reason, string actor, bool automatic = false, CancellationToken cancellationToken = default);
    Task<DisciplineViolation> AddViolationAsync(ViolationWriteRequest request, string actor, CancellationToken cancellationToken = default);
    Task UpdateIdentityNotesAsync(string userId, string notes, string actor, CancellationToken cancellationToken = default);
    Task<DisciplineImportResult> ImportAsync(DisciplineImportRequest request, string actor, CancellationToken cancellationToken = default);
    Task<DisciplineExportFile> ExportAsync(string kind, string format, CancellationToken cancellationToken = default);
    Task<int> UnbanExpiredAsync(CancellationToken cancellationToken = default);
    Task SyncIdentitiesAsync(CancellationToken cancellationToken = default);
    Task RecordKickAsync(string userId, string? displayName, string? reason, string actor, string source = "palops", CancellationToken cancellationToken = default);
}

public sealed class PlayerDisciplineService(
    IPlayerDisciplineRepository repository,
    IPalDefenderAccessControlReader accessControlReader,
    IPalDefenderAccessControlWriter accessControlWriter,
    IPalDefenderApiClient palDefenderApi,
    IServerSettingsStore settingsStore,
    IRconClient rconClient,
    TimeProvider timeProvider,
    ILogger<PlayerDisciplineService> logger) : IPlayerDisciplineService
{
    private const int MaximumImportEntries = 5_000;
    private const int MaximumImportBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _commandGate = new(1, 1);

    public async Task<PlayerDisciplineDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var source = await accessControlReader.ReadAsync(cancellationToken);
        var state = await repository.ReadAsync(cancellationToken);
        var warnings = source.Warnings.ToList();
        IReadOnlyList<PalDefenderPlayer> knownPlayers;
        try { knownPlayers = await palDefenderApi.GetKnownPlayersAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            knownPlayers = [];
            warnings.Add("无法读取 PalDefender 在线玩家：" + ex.Message);
        }
        var online = knownPlayers.Where(static player => IsOnlineStatus(player.Status)).ToArray();

        var now = timeProvider.GetUtcNow();
        var onlineByUser = online
            .Where(static player => !string.IsNullOrWhiteSpace(player.UserId))
            .GroupBy(static player => player.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var whitelistSet = source.WhitelistIdentifiers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var banSet = source.BanRecords.Select(static item => item.Identifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var violationsByUser = state.Violations.GroupBy(static item => item.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var whitelist = source.WhitelistIdentifiers.Select(identifier =>
        {
            state.Whitelist.TryGetValue(identifier, out var metadata);
            onlineByUser.TryGetValue(identifier, out var player);
            return new DisciplineWhitelistEntry(
                identifier,
                FirstNonEmpty(metadata?.DisplayName, player?.Name),
                metadata?.AddedAt,
                metadata?.AddedBy ?? string.Empty,
                metadata?.Notes ?? string.Empty,
                player is not null,
                metadata is null);
        }).OrderByDescending(static entry => entry.Online).ThenBy(static entry => entry.UserId, StringComparer.OrdinalIgnoreCase).ToArray();

        var bans = source.BanRecords.Select(record =>
        {
            state.Bans.TryGetValue(record.Identifier, out var metadata);
            onlineByUser.TryGetValue(record.Identifier, out var player);
            var expiresAt = metadata?.ExpiresAt;
            return new DisciplineBanEntry(
                record.Identifier,
                metadata?.BanType ?? record.BanType,
                FirstNonEmpty(metadata?.DisplayName, player?.Name),
                FirstNonEmpty(metadata?.Reason, record.Reason),
                metadata?.Operator ?? string.Empty,
                metadata?.BannedAt ?? record.BannedAt,
                expiresAt,
                expiresAt.HasValue,
                expiresAt.HasValue && expiresAt.Value <= now,
                player is not null,
                metadata?.Status ?? "active",
                metadata?.Notes ?? string.Empty,
                metadata is null,
                metadata?.LastError);
        }).OrderByDescending(static entry => entry.BannedAt).ThenBy(static entry => entry.Identifier, StringComparer.OrdinalIgnoreCase).ToArray();

        var onlineRows = onlineByUser.Values.Select(player => new DisciplineOnlinePlayer(
            player.UserId,
            player.PlayerUid,
            player.Name,
            player.Ip,
            player.GuildName,
            player.Status,
            whitelistSet.Contains(player.UserId),
            banSet.Contains(player.UserId) || (!string.IsNullOrWhiteSpace(player.Ip) && banSet.Contains(player.Ip)),
            violationsByUser.GetValueOrDefault(player.UserId)))
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        var onlineIds = onlineRows.Select(static row => row.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownPlayersByUser = knownPlayers
            .Where(static player => !string.IsNullOrWhiteSpace(player.UserId))
            .GroupBy(static player => player.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var identityIds = state.Identities.Keys
            .Concat(knownPlayersByUser.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var identities = identityIds.Select(userId =>
        {
            state.Identities.TryGetValue(userId, out var identity);
            knownPlayersByUser.TryGetValue(userId, out var player);
            var status = player?.Status ?? (onlineIds.Contains(userId) ? "online" : "known");
            return new DisciplineIdentity(
                userId,
                MergeValues(identity?.Names, player?.Name),
                MergeValues(identity?.IpAddresses, player?.Ip),
                MergeValues(identity?.PlayerUids, player?.PlayerUid),
                MergeValues(identity?.GuildNames, player?.GuildName),
                identity?.FirstSeenAt ?? now,
                identity?.LastSeenAt ?? now,
                status,
                IsOnlineStatus(status),
                violationsByUser.GetValueOrDefault(userId),
                identity?.Notes ?? string.Empty);
        })
            .OrderByDescending(static item => item.Online)
            .ThenByDescending(static item => item.LastSeenAt)
            .ThenBy(static item => item.UserId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var violations = state.Violations.Select(static item => new DisciplineViolation(
            item.Id, item.UserId, item.DisplayName, item.Severity, item.Category,
            item.Summary, item.Details, item.Operator, item.CreatedAt)).ToArray();

        var kickEntries = state.Kicks.Select(static item => new DisciplineKickEntry(
            item.Id, item.UserId, item.DisplayName, item.Reason, item.Operator,
            item.KickedAt, item.Source)).ToArray();

        return new PlayerDisciplineDashboard(
            whitelist,
            bans,
            onlineRows,
            identities,
            violations,
            kickEntries,
            state.Operations.Take(200).ToArray(),
            warnings,
            now);
    }

    public async Task AddWhitelistAsync(WhitelistWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var userId = ValidatePalDefenderUserId(request.UserId);
        var displayName = NormalizeText(request.DisplayName, 120);
        var notes = NormalizeText(request.Notes, 1_000);
        await AddWhitelistAndVerifyAsync(userId, displayName, notes, actor, cancellationToken);
    }

    public async Task RemoveWhitelistAsync(string userId, string actor, CancellationToken cancellationToken = default)
    {
        userId = ValidatePalDefenderUserId(userId);
        await RemoveWhitelistAndVerifyAsync(userId, actor, cancellationToken);
    }

    public async Task BanAsync(BanWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var banType = NormalizeBanType(request.BanType, request.Identifier);
        var identifier = banType == "ip" ? ValidateIp(request.Identifier) : ValidateIdentifier(request.Identifier, "Identifier");
        var reason = RequireText(request.Reason, 500, "封禁原因不能为空。");
        var displayName = NormalizeText(request.DisplayName, 120);
        var notes = NormalizeText(request.Notes, 1_000);
        var now = timeProvider.GetUtcNow();
        if (request.ExpiresAt.HasValue)
        {
            if (request.ExpiresAt.Value <= now.AddMinutes(1)) throw new ArgumentException("临时封禁到期时间必须至少晚于当前时间 1 分钟。");
            if (request.ExpiresAt.Value > now.AddDays(3650)) throw new ArgumentException("临时封禁期限不能超过 10 年。");
        }

        var command = banType == "ip"
            ? $"banip {identifier}"
            : $"ban {identifier} {EscapeCommandArgument(reason)}";
        await ExecuteAndRecordAsync(
            "ban.add", identifier, actor, command,
            state => state.Bans[identifier] = new BanMetadata
            {
                Identifier = identifier,
                BanType = banType,
                DisplayName = displayName,
                Reason = reason,
                Operator = actor,
                BannedAt = now,
                ExpiresAt = request.ExpiresAt?.ToUniversalTime(),
                Notes = notes,
                Status = "active"
            },
            request.ExpiresAt.HasValue ? "已创建临时封禁。" : "已创建永久封禁。",
            cancellationToken);
    }

    public async Task UnbanAsync(string identifier, string reason, string actor, bool automatic = false, CancellationToken cancellationToken = default)
    {
        identifier = ValidateIdentifierOrIp(identifier);
        reason = RequireText(reason, 500, "解封原因不能为空。");
        var state = await repository.ReadAsync(cancellationToken);
        state.Bans.TryGetValue(identifier, out var metadata);
        var banType = metadata?.BanType ?? (IPAddress.TryParse(identifier, out _) ? "ip" : "account");
        var command = banType == "ip"
            ? $"unbanip {identifier}"
            : $"unban {identifier} {EscapeCommandArgument(reason)}";

        await ExecuteAndRecordAsync(
            automatic ? "ban.auto-unban" : "ban.unban", identifier, actor, command,
            current =>
            {
                if (!current.Bans.TryGetValue(identifier, out var item)) return;
                item.Status = "unbanned";
                item.UnbannedAt = timeProvider.GetUtcNow();
                item.UnbannedBy = actor;
                item.LastError = null;
            },
            automatic ? "临时封禁到期，已自动解除。" : "已解除封禁。",
            cancellationToken);
    }

    public async Task<DisciplineViolation> AddViolationAsync(ViolationWriteRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var userId = ValidateIdentifier(request.UserId, "UserId");
        var severity = NormalizeSeverity(request.Severity);
        var category = RequireText(request.Category, 80, "违规分类不能为空。");
        var summary = RequireText(request.Summary, 300, "违规摘要不能为空。");
        var details = NormalizeText(request.Details, 2_000);
        var displayName = NormalizeText(request.DisplayName, 120);
        var now = timeProvider.GetUtcNow();
        var item = new ViolationMetadata
        {
            Id = Guid.NewGuid().ToString("N"), UserId = userId, DisplayName = displayName,
            Severity = severity, Category = category, Summary = summary, Details = details,
            Operator = actor, CreatedAt = now
        };
        await repository.UpdateAsync(state =>
        {
            state.Violations.Insert(0, item);
            state.Operations.Insert(0, Operation("violation.add", userId, "success", actor, summary, null, now));
            return true;
        }, cancellationToken);
        return new DisciplineViolation(item.Id, item.UserId, item.DisplayName, item.Severity, item.Category, item.Summary, item.Details, item.Operator, item.CreatedAt);
    }

    public Task RecordKickAsync(
        string userId,
        string? displayName,
        string? reason,
        string actor,
        string source = "palops",
        CancellationToken cancellationToken = default)
    {
        userId = ValidateIdentifier(userId, "UserId");
        displayName = NormalizeText(displayName, 120);
        reason = NormalizeText(reason, 500);
        actor = NormalizeText(actor, 120);
        source = NormalizeText(source, 40);
        var now = timeProvider.GetUtcNow();
        var item = new KickMetadata
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            DisplayName = displayName,
            Reason = reason,
            Operator = actor,
            KickedAt = now,
            Source = string.IsNullOrWhiteSpace(source) ? "palops" : source
        };
        return repository.UpdateAsync(state =>
        {
            state.Kicks.Insert(0, item);
            state.Operations.Insert(0, Operation("player.kick", userId, "success", actor, "已记录玩家踢出操作。", null, now));
            return true;
        }, cancellationToken);
    }

    public Task UpdateIdentityNotesAsync(string userId, string notes, string actor, CancellationToken cancellationToken = default)
    {
        userId = ValidateIdentifier(userId, "UserId");
        notes = NormalizeText(notes, 2_000);
        var now = timeProvider.GetUtcNow();
        return repository.UpdateAsync(state =>
        {
            if (!state.Identities.TryGetValue(userId, out var identity))
            {
                identity = new IdentityMetadata { UserId = userId, FirstSeenAt = now, LastSeenAt = now };
                state.Identities[userId] = identity;
            }
            identity.Notes = notes;
            state.Operations.Insert(0, Operation("identity.notes", userId, "success", actor, "已更新身份关联备注。", null, now));
            return true;
        }, cancellationToken);
    }

    public async Task<DisciplineImportResult> ImportAsync(DisciplineImportRequest request, string actor, CancellationToken cancellationToken = default)
    {
        if (Encoding.UTF8.GetByteCount(request.Content ?? string.Empty) > MaximumImportBytes) throw new InvalidDataException("导入内容不能超过 2 MiB。");
        var kind = (request.Kind ?? string.Empty).Trim().ToLowerInvariant();
        var format = (request.Format ?? string.Empty).Trim().ToLowerInvariant();
        if (kind is not ("whitelist" or "bans" or "violations")) throw new ArgumentException("导入类型只能是 whitelist、bans 或 violations。");
        if (format is not ("json" or "csv")) throw new ArgumentException("导入格式只能是 json 或 csv。");

        var rows = ParseImportRows(kind, format, request.Content ?? string.Empty);
        if (rows.Count > MaximumImportEntries) throw new InvalidDataException($"单次导入最多 {MaximumImportEntries} 条记录。");
        var imported = 0;
        var failed = 0;
        var errors = new List<string>();
        for (var index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var row = rows[index];
                if (kind == "whitelist")
                    await AddWhitelistAsync(new WhitelistWriteRequest(row.Identifier, row.DisplayName, row.Notes), actor, cancellationToken);
                else if (kind == "bans")
                    await BanAsync(new BanWriteRequest(row.Identifier, row.BanType, row.DisplayName, row.Reason, row.ExpiresAt, row.Notes), actor, cancellationToken);
                else
                    await AddViolationAsync(new ViolationWriteRequest(row.Identifier, row.DisplayName, row.Severity, row.Category, row.Summary, row.Notes), actor, cancellationToken);
                imported++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (errors.Count < 100) errors.Add($"第 {index + 1} 条：{ex.Message}");
                failed++;
            }
        }
        return new DisciplineImportResult(imported, 0, failed, errors);
    }

    public async Task<DisciplineExportFile> ExportAsync(string kind, string format, CancellationToken cancellationToken = default)
    {
        kind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        format = (format ?? string.Empty).Trim().ToLowerInvariant();
        if (kind is not ("whitelist" or "bans" or "violations" or "identities")) throw new ArgumentException("不支持的导出类型。");
        if (format is not ("json" or "csv")) throw new ArgumentException("导出格式只能是 json 或 csv。");
        var dashboard = await GetDashboardAsync(cancellationToken);
        object data = kind switch
        {
            "whitelist" => dashboard.WhitelistEntries,
            "bans" => dashboard.BanEntries,
            "violations" => dashboard.Violations,
            _ => dashboard.Identities
        };
        var stamp = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss");
        if (format == "json")
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, JsonOptions) + Environment.NewLine);
            return new DisciplineExportFile($"player-discipline-{kind}-{stamp}.json", "application/json; charset=utf-8", bytes);
        }
        var csv = BuildCsv(kind, dashboard);
        return new DisciplineExportFile($"player-discipline-{kind}-{stamp}.csv", "text/csv; charset=utf-8", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray());
    }

    public async Task<int> UnbanExpiredAsync(CancellationToken cancellationToken = default)
    {
        var source = await accessControlReader.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(source.BanlistSha256))
        {
            logger.LogDebug("Skipping temporary-ban expiry because Banlist.json is not readable.");
            return 0;
        }

        var activeSourceBans = source.BanRecords.Select(static item => item.Identifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var state = await repository.ReadAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var expired = state.Bans.Values
            .Where(item => item.Status == "active"
                && item.ExpiresAt.HasValue
                && item.ExpiresAt.Value <= now
                && activeSourceBans.Contains(item.Identifier))
            .OrderBy(static item => item.ExpiresAt).ToArray();
        var completed = 0;
        foreach (var ban in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await UnbanAsync(ban.Identifier, "Temporary ban expired.", "PalOps-AutoUnban", automatic: true, cancellationToken);
                completed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Automatic unban failed for {Identifier}.", ban.Identifier);
                await repository.UpdateAsync(current =>
                {
                    if (current.Bans.TryGetValue(ban.Identifier, out var item)) item.LastError = ex.Message;
                    current.Operations.Insert(0, Operation("ban.auto-unban", ban.Identifier, "failed", "PalOps-AutoUnban", "临时封禁自动解除失败。", ex.Message, timeProvider.GetUtcNow()));
                    return true;
                }, CancellationToken.None);
            }
        }
        return completed;
    }

    public async Task SyncIdentitiesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PalDefenderPlayer> players;
        try { players = await palDefenderApi.GetKnownPlayersAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Skipping player identity synchronization because PalDefender is unavailable.");
            return;
        }

        var now = timeProvider.GetUtcNow();
        await repository.UpdateIfChangedAsync(state =>
        {
            var changed = false;
            foreach (var player in players.Where(static player => !string.IsNullOrWhiteSpace(player.UserId)))
            {
                string userId;
                try { userId = ValidateIdentifier(player.UserId, "UserId"); }
                catch (ArgumentException) { continue; }

                if (!state.Identities.TryGetValue(userId, out var identity))
                {
                    identity = new IdentityMetadata { UserId = userId, FirstSeenAt = now, LastSeenAt = now };
                    state.Identities[userId] = identity;
                    changed = true;
                }
                else if (IsOnlineStatus(player.Status) && now - identity.LastSeenAt >= TimeSpan.FromMinutes(5))
                {
                    identity.LastSeenAt = now;
                    changed = true;
                }

                changed |= AddNonEmpty(identity.Names, player.Name);
                changed |= AddNonEmpty(identity.IpAddresses, player.Ip);
                changed |= AddNonEmpty(identity.PlayerUids, player.PlayerUid);
                changed |= AddNonEmpty(identity.GuildNames, player.GuildName);
            }
            return changed;
        }, cancellationToken);
    }

    private async Task AddWhitelistAndVerifyAsync(
        string userId,
        string displayName,
        string notes,
        string actor,
        CancellationToken cancellationToken)
    {
        await ChangeWhitelistAndVerifyAsync(
            userId: userId,
            include: true,
            actor: actor,
            update: state => state.Whitelist[userId] = new WhitelistMetadata
            {
                UserId = userId,
                DisplayName = displayName,
                AddedAt = timeProvider.GetUtcNow(),
                AddedBy = actor,
                Notes = notes
            },
            operation: "whitelist.add",
            summary: "已写入 WhiteList.json、重载 PalDefender 并确认白名单生效。",
            cancellationToken: cancellationToken);
    }

    private async Task RemoveWhitelistAndVerifyAsync(
        string userId,
        string actor,
        CancellationToken cancellationToken)
    {
        await ChangeWhitelistAndVerifyAsync(
            userId: userId,
            include: false,
            actor: actor,
            update: state => state.Whitelist.Remove(userId),
            operation: "whitelist.remove",
            summary: "已从 WhiteList.json 移除、重载 PalDefender 并确认结果。",
            cancellationToken: cancellationToken);
    }

    private async Task ChangeWhitelistAndVerifyAsync(
        string userId,
        bool include,
        string actor,
        Action<PlayerDisciplineState> update,
        string operation,
        string summary,
        CancellationToken cancellationToken)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var commandAccepted = await ExecuteWhitelistCommandAsync(userId, include, cancellationToken);
                var verified = await WaitForWhitelistMembershipAsync(userId, include, cancellationToken);
                if (!commandAccepted && verified)
                    logger.LogInformation("PalDefender whitelist command response was ambiguous, but WhiteList.json confirmed the requested state for {UserId}.", userId);

                if (!verified)
                {
                    logger.LogWarning(
                        "PalDefender whitelist command did not produce a verifiable file update for {UserId}; applying atomic file fallback.",
                        userId);
                    if (include)
                        await accessControlWriter.AddWhitelistAsync(userId, cancellationToken);
                    else
                        await accessControlWriter.RemoveWhitelistAsync(userId, cancellationToken);

                    await ReloadPalDefenderAsync(cancellationToken);
                    verified = await WaitForWhitelistMembershipAsync(userId, include, cancellationToken);
                }

                if (!verified)
                    throw new InvalidOperationException(include
                        ? "PalDefender 已接收白名单操作，但 WhiteList.json 回读仍未找到目标 UserId。"
                        : "PalDefender 已接收白名单移除操作，但 WhiteList.json 回读仍包含目标 UserId。");

                var now = timeProvider.GetUtcNow();
                await repository.UpdateAsync(state =>
                {
                    update(state);
                    state.Operations.Insert(0, Operation(operation, userId, "success", actor, summary, null, now));
                    return true;
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await RecordFailedOperationBestEffortAsync(operation, userId, actor, summary, ex);
                throw;
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task<bool> ExecuteWhitelistCommandAsync(
        string userId,
        bool include,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await settingsStore.GetAsync(cancellationToken);
            var command = include ? $"whitelist_add {userId}" : $"whitelist_remove {userId}";
            var result = await rconClient.ExecuteAsync(settings.Rcon, command, cancellationToken);
            var interpretation = RconActionResponseInterpreter.Interpret(result.Response);
            if (!interpretation.Success)
            {
                logger.LogWarning(
                    "PalDefender whitelist command {Command} was rejected: {Code} {Message}",
                    command,
                    interpretation.Code,
                    interpretation.Message);
                return false;
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PalDefender whitelist RCON command failed; using atomic file fallback.");
            return false;
        }
    }

    private async Task ReloadPalDefenderAsync(CancellationToken cancellationToken)
    {
        try
        {
            await palDefenderApi.ReloadConfigAsync(cancellationToken);
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PalDefender REST reload failed; falling back to RCON reloadcfg.");
        }

        var settings = await settingsStore.GetAsync(cancellationToken);
        var result = await rconClient.ExecuteAsync(settings.Rcon, "reloadcfg", cancellationToken);
        var interpretation = RconActionResponseInterpreter.Interpret(result.Response);
        if (!interpretation.Success)
            throw new RconException(interpretation.Code, interpretation.Message);
    }

    private async Task<bool> WaitForWhitelistMembershipAsync(
        string userId,
        bool expected,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var source = await accessControlReader.ReadAsync(cancellationToken);
            var actual = source.WhitelistIdentifiers.Contains(userId, StringComparer.OrdinalIgnoreCase);
            if (actual == expected) return true;
            if (attempt < 7) await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
        return false;
    }

    private async Task RecordFailedOperationBestEffortAsync(
        string operation,
        string target,
        string actor,
        string summary,
        Exception exception)
    {
        try
        {
            await repository.UpdateAsync(state =>
            {
                state.Operations.Insert(0, Operation(operation, target, "failed", actor, summary, exception.Message, timeProvider.GetUtcNow()));
                return true;
            }, CancellationToken.None);
        }
        catch (Exception auditException)
        {
            logger.LogError(auditException, "Unable to persist failed discipline operation {Operation} for {Target}.", operation, target);
        }
    }

    private async Task ExecuteAndRecordAsync(
        string operation,
        string target,
        string actor,
        string command,
        Action<PlayerDisciplineState> update,
        string summary,
        CancellationToken cancellationToken)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var settings = await settingsStore.GetAsync(cancellationToken);
                var result = await rconClient.ExecuteAsync(settings.Rcon, command, cancellationToken);
                var interpretation = RconActionResponseInterpreter.Interpret(result.Response);
                if (!interpretation.Success)
                    throw new RconException(interpretation.Code, interpretation.Message);
                var now = timeProvider.GetUtcNow();
                await repository.UpdateAsync(state =>
                {
                    update(state);
                    state.Operations.Insert(0, Operation(operation, target, "success", actor, summary, null, now));
                    return true;
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                try
                {
                    await repository.UpdateAsync(state =>
                    {
                        state.Operations.Insert(0, Operation(operation, target, "failed", actor, summary, ex.Message, timeProvider.GetUtcNow()));
                        return true;
                    }, CancellationToken.None);
                }
                catch (Exception auditException)
                {
                    logger.LogError(auditException, "Unable to persist failed discipline operation {Operation} for {Target}.", operation, target);
                }
                throw;
            }
        }
        finally { _commandGate.Release(); }
    }

    public static string EscapeCommandArgument(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0) throw new ArgumentException("RCON 参数不能为空。", nameof(value));
        if (normalized.Length > 500) throw new ArgumentException("RCON 参数不能超过 500 个字符。", nameof(value));
        if (normalized.Any(char.IsControl)) throw new ArgumentException("RCON 参数不能包含控制字符。", nameof(value));
        return "\"" + normalized.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static IReadOnlyList<string> MergeValues(IEnumerable<string>? existing, string? current)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existing is not null)
        {
            foreach (var value in existing)
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value.Trim());
        }
        if (!string.IsNullOrWhiteSpace(current)) values.Add(current.Trim());
        return values.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsOnlineStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && (status.Equals("online", StringComparison.OrdinalIgnoreCase)
            || status.Contains("connected", StringComparison.OrdinalIgnoreCase)
            || status.Contains("online", StringComparison.OrdinalIgnoreCase));

    private static string ValidateIdentifierOrIp(string value) => IPAddress.TryParse(value?.Trim(), out _) ? ValidateIp(value) : ValidateIdentifier(value, "Identifier");

    private static string ValidatePalDefenderUserId(string value)
    {
        var normalized = ValidateIdentifier(value, "UserId");
        var separator = normalized.IndexOf('_');
        if (separator < 1 || separator == normalized.Length - 1
            || !normalized[..separator].All(static ch => char.IsLetterOrDigit(ch))
            || Guid.TryParse(normalized, out _))
            throw new ArgumentException("白名单必须使用 PalDefender UserId，例如 steam_…、gdk_… 或 ps5_…，不能使用玩家名称或存档 PlayerUID。", nameof(value));
        return normalized;
    }

    private static string ValidateIdentifier(string value, string name)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 128 || normalized.Any(char.IsControl) || normalized.Any(char.IsWhiteSpace)
            || !normalized.All(static ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':'))
            throw new ArgumentException("玩家标识只能包含字母、数字、下划线、连字符、冒号和点。", name);
        return normalized;
    }

    private static string ValidateIp(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!IPAddress.TryParse(normalized, out var address)) throw new ArgumentException("IP 地址格式无效。", nameof(value));
        return address.ToString();
    }

    private static string NormalizeBanType(string value, string identifier)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) return IPAddress.TryParse(identifier?.Trim(), out _) ? "ip" : "account";
        if (normalized is not ("account" or "ip")) throw new ArgumentException("封禁类型只能是 account 或 ip。");
        return normalized;
    }

    private static string NormalizeSeverity(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "info" => "info", "warning" => "warning", "critical" => "critical",
        _ => throw new ArgumentException("违规级别只能是 info、warning 或 critical。")
    };

    private static string RequireText(string? value, int maximum, string message)
    {
        var normalized = NormalizeText(value, maximum);
        if (normalized.Length == 0) throw new ArgumentException(message);
        return normalized;
    }

    private static string NormalizeText(string? value, int maximum)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static DisciplineOperation Operation(string operation, string target, string outcome, string actor, string summary, string? error, DateTimeOffset timestamp) =>
        new(Guid.NewGuid().ToString("N"), operation, target, outcome, actor, summary, timestamp, error);

    private static bool AddNonEmpty(HashSet<string> values, string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && values.Add(value.Trim());
    }

    private sealed record ImportRow(string Identifier, string BanType, string DisplayName, string Reason, DateTimeOffset? ExpiresAt, string Notes, string Severity, string Category, string Summary);

    private static IReadOnlyList<ImportRow> ParseImportRows(string kind, string format, string content)
    {
        if (format == "json") return ParseJsonRows(kind, content);
        return ParseCsvRows(kind, content);
    }

    private static IReadOnlyList<ImportRow> ParseJsonRows(string kind, string content)
    {
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("JSON 导入根节点必须是数组。");
        var rows = new List<ImportRow>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                rows.Add(DefaultRow(element.GetString() ?? string.Empty));
                continue;
            }
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("JSON 导入项必须是字符串或对象。");
            var identifier = Get(element, kind == "violations" ? "userId" : "identifier", "userId", "UserId", "ip");
            rows.Add(new ImportRow(
                identifier,
                Get(element, "banType", "type"),
                Get(element, "displayName", "name"),
                Get(element, "reason"),
                ParseOptionalDate(Get(element, "expiresAt", "expires")),
                Get(element, "notes", "details"),
                Get(element, "severity"),
                Get(element, "category"),
                Get(element, "summary", "reason")));
        }
        return rows;
    }

    private static IReadOnlyList<ImportRow> ParseCsvRows(string kind, string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return [];
        var headers = ParseCsvLine(lines[0]).Select(static value => value.Trim()).ToArray();
        var hasHeader = headers.Any(header => header.Equals("identifier", StringComparison.OrdinalIgnoreCase) || header.Equals("userId", StringComparison.OrdinalIgnoreCase));
        var start = hasHeader ? 1 : 0;
        var rows = new List<ImportRow>();
        for (var index = start; index < lines.Length; index++)
        {
            var values = ParseCsvLine(lines[index]);
            if (!hasHeader) { rows.Add(DefaultRow(values.ElementAtOrDefault(0) ?? string.Empty)); continue; }
            string Cell(params string[] names)
            {
                var headerIndex = Array.FindIndex(headers, header => names.Any(name => header.Equals(name, StringComparison.OrdinalIgnoreCase)));
                return headerIndex >= 0 && headerIndex < values.Count ? values[headerIndex] : string.Empty;
            }
            rows.Add(new ImportRow(
                Cell(kind == "violations" ? "userId" : "identifier", "userId", "ip"),
                Cell("banType", "type"), Cell("displayName", "name"), Cell("reason"),
                ParseOptionalDate(Cell("expiresAt", "expires")), Cell("notes", "details"), Cell("severity"), Cell("category"), Cell("summary", "reason")));
        }
        return rows;
    }

    private static ImportRow DefaultRow(string identifier) => new(identifier, "account", string.Empty, "Imported by PalOps.", null, string.Empty, "warning", "imported", "Imported record");

    private static string Get(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
            if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ToString();
        return string.Empty;
    }

    private static DateTimeOffset? ParseOptionalDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            throw new InvalidDataException($"无效的到期时间：{value}");
        return parsed.ToUniversalTime();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"') { builder.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted) { values.Add(builder.ToString()); builder.Clear(); }
            else builder.Append(ch);
        }
        values.Add(builder.ToString());
        return values;
    }

    private static string BuildCsv(string kind, PlayerDisciplineDashboard dashboard)
    {
        var builder = new StringBuilder();
        if (kind == "whitelist")
        {
            builder.AppendLine("userId,displayName,addedAt,addedBy,notes,online");
            foreach (var item in dashboard.WhitelistEntries) builder.AppendLine(string.Join(",", Csv(item.UserId), Csv(item.DisplayName), Csv(item.AddedAt?.ToString("O")), Csv(item.AddedBy), Csv(item.Notes), Csv(item.Online.ToString())));
        }
        else if (kind == "bans")
        {
            builder.AppendLine("identifier,banType,displayName,reason,operator,bannedAt,expiresAt,status,notes");
            foreach (var item in dashboard.BanEntries) builder.AppendLine(string.Join(",", Csv(item.Identifier), Csv(item.BanType), Csv(item.DisplayName), Csv(item.Reason), Csv(item.Operator), Csv(item.BannedAt?.ToString("O")), Csv(item.ExpiresAt?.ToString("O")), Csv(item.Status), Csv(item.Notes)));
        }
        else if (kind == "violations")
        {
            builder.AppendLine("id,userId,displayName,severity,category,summary,details,operator,createdAt");
            foreach (var item in dashboard.Violations) builder.AppendLine(string.Join(",", Csv(item.Id), Csv(item.UserId), Csv(item.DisplayName), Csv(item.Severity), Csv(item.Category), Csv(item.Summary), Csv(item.Details), Csv(item.Operator), Csv(item.CreatedAt.ToString("O"))));
        }
        else
        {
            builder.AppendLine("userId,names,ipAddresses,playerUids,guildNames,firstSeenAt,lastSeenAt,violationCount,notes");
            foreach (var item in dashboard.Identities) builder.AppendLine(string.Join(",", Csv(item.UserId), Csv(string.Join(";", item.Names)), Csv(string.Join(";", item.IpAddresses)), Csv(string.Join(";", item.PlayerUids)), Csv(string.Join(";", item.GuildNames)), Csv(item.FirstSeenAt.ToString("O")), Csv(item.LastSeenAt.ToString("O")), Csv(item.ViolationCount.ToString(CultureInfo.InvariantCulture)), Csv(item.Notes)));
        }
        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        var normalized = value ?? string.Empty;
        if (normalized.Length > 0 && normalized[0] is '=' or '+' or '-' or '@') normalized = "'" + normalized;
        return "\"" + normalized.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
