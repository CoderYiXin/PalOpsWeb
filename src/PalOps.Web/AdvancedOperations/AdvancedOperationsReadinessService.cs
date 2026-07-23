using PalOps.Web.Backups;
using PalOps.Web.Contracts;
using PalOps.Web.Health;
using PalOps.Web.Infrastructure;
using PalOps.Web.PalworldConfiguration;
using PalOps.Web.Platform.Caching;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.SaveGames.Index;
using PalOps.Web.Settings;

namespace PalOps.Web.AdvancedOperations;

public interface IAdvancedOperationsReadinessService
{
    Task<AdvancedModuleReadiness> GetAsync(string module, CancellationToken cancellationToken = default);
}

public sealed class AdvancedOperationsReadinessService(
    IServerSettingsStore settingsStore,
    ISaveIndexRepository saveIndex,
    IBackupService backupService,
    IPalworldConfigurationService configurationService,
    IRuntimePathResolver paths,
    ISystemHealthService health,
    IPlatformCache cache,
    IOperationalReadinessGate operationalReadiness,
    ILogger<AdvancedOperationsReadinessService> logger) : IAdvancedOperationsReadinessService
{
    private static readonly HashSet<string> SupportedModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "system-onboarding",
        "setup-center",
        "task-center",
        "health-center",
        "overview",
        "players",
        "guilds",
        "map",
        "palworld-configuration",
        "resources",
        "messages",
        "rcon",
        "automation",
        "maintenance",
        "server-statistics",
        "save-diff",
        "player-discipline",
        "backups",
        "diagnostics",
        "incidents",
        "player-insights",
        "world-governance",
        "disaster-recovery",
        "update-center",
        "configuration-versions",
        "operations-playbooks",
        "security-center",
        "integration-center",
        "notifications",
        "notification-history",
        "paldefender",
        "plugin-management",
        "save-index",
        "catalog",
        "audit",
        "system-logs",
        "users",
        "about"
    };

    private static readonly HashSet<string> SelfConfigurationModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "system-onboarding",
        "setup-center",
        "task-center",
        "health-center",
        "overview",
        "map",
        "palworld-configuration",
        "automation",
        "maintenance",
        "player-discipline",
        "diagnostics",
        "update-center",
        "paldefender",
        "plugin-management",
        "save-index"
    };

    public Task<AdvancedModuleReadiness> GetAsync(string module, CancellationToken cancellationToken = default)
    {
        var normalized = (module ?? string.Empty).Trim().ToLowerInvariant();
        if (!SupportedModules.Contains(normalized))
            throw new ArgumentException("Unsupported configuration readiness module.", nameof(module));

        return cache.GetOrCreateAsync(
            $"readiness:{normalized}",
            TimeSpan.FromSeconds(10),
            token => GetUncachedAsync(normalized, token),
            ["readiness"],
            cancellationToken);
    }

    private async Task<AdvancedModuleReadiness> GetUncachedAsync(string normalized, CancellationToken cancellationToken)
    {

        var operational = await operationalReadiness.GetSnapshotAsync(cancellationToken);
        var signals = await ReadSignalsAsync(operational.HasAnyOperationalConfiguration, cancellationToken);
        var catalog = BuildCheckCatalog(signals);
        var selected = SelectChecks(normalized, catalog);

        var required = selected.Where(item => item.Required).ToArray();
        var missingRequired = required.Where(item => item.Status == "missing").ToArray();
        var unavailableRequired = required.Where(item => item.Status == "unavailable").ToArray();
        var unresolvedRequired = required.Where(item => item.Status is not "ready").ToArray();
        var warningChecks = selected.Where(item => item.Status is not "ready").ToArray();

        var status = missingRequired.Length > 0
            ? "setup-required"
            : unavailableRequired.Length > 0
                ? "unavailable"
                : warningChecks.Length > 0 ? "partial" : "ready";

        var blocked = !SelfConfigurationModules.Contains(normalized)
                      && unresolvedRequired.Length > 0;

        var onboarding = SelectChecks("system-onboarding", catalog);
        var completionPercent = CalculateCompletion(onboarding);
        var firstRun = !signals.SaveDirectoryConfigured
                       && !signals.RestConfigured
                       && !signals.DefenderConfigured
                       && !signals.RconConfigured
                       && !signals.SaveIndexAvailable;

        var title = status switch
        {
            "setup-required" when firstRun => $"首次使用：请先完成系统配置（{completionPercent}%）",
            "setup-required" => "需要先完成系统配置",
            "unavailable" => "已配置的服务当前不可用",
            "partial" when blocked => "正在确认当前页面的必需服务",
            "partial" when firstRun => $"首次使用引导（{completionPercent}%）",
            "partial" => "部分配置或服务尚未就绪",
            _ => "模块已就绪"
        };

        var description = status switch
        {
            "setup-required" => blocked
                ? "为避免连续报错，当前业务页面暂不加载。完成下列配置后系统会自动重新检测并恢复页面。"
                : "当前页面仍可用于完成配置；完成下列项目后相关提示会自动消失。",
            "unavailable" => blocked
                ? "配置已经保存，但依赖服务当前无法连接。业务页面暂不加载，服务恢复后提示会自动消失。"
                : "配置已经保存，但部分依赖服务当前无法连接。可继续进行不依赖该服务的操作。",
            "partial" when blocked => "必需服务正在进行首次健康检测。检测完成前业务页面暂不加载，确认可用后会自动恢复。",
            "partial" => "基础页面可以使用，但完成建议项或等待服务恢复后才能获得完整功能。",
            _ => "当前模块所需的配置和服务状态均已满足。"
        };

        return new AdvancedModuleReadiness(
            normalized,
            status,
            status == "ready",
            blocked,
            firstRun,
            completionPercent,
            title,
            description,
            required.Select(item => item.Label).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            missingRequired.Select(item => item.Message).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warningChecks
                .Where(item => !missingRequired.Contains(item))
                .Select(item => item.Message)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            selected,
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<ConfigurationReadinessCheck> SelectChecks(
        string module,
        IReadOnlyDictionary<string, ConfigurationReadinessCheck> catalog)
    {
        var selected = new List<ConfigurationReadinessCheck>();

        void Add(string code, bool required)
        {
            var item = catalog[code];
            selected.Add(item with { Required = required });
        }

        Add("storage", true);

        switch (module)
        {
            case "system-onboarding":
            case "setup-center":
                Add("save-directory", true);
                Add("palworld-rest", false);
                Add("rcon", false);
                Add("paldefender-rest", false);
                Add("backup-directory", false);
                Add("save-index", false);
                Add("palworld-configuration", false);
                Add("automation", false);
                break;
            case "task-center":
                Add("automation", false);
                break;
            case "health-center":
                Add("server-connection", false);
                Add("save-directory", false);
                Add("backup-directory", false);
                break;
            case "overview":
                Add("server-connection", false);
                Add("save-directory", false);
                Add("save-index", false);
                break;
            case "players":
            case "guilds":
                Add("save-directory", true);
                Add("save-index", true);
                Add("server-connection", false);
                break;
            case "map":
                Add("save-index", false);
                Add("server-connection", false);
                break;
            case "palworld-configuration":
                Add("palworld-configuration", false);
                break;
            case "resources":
            case "messages":
                Add("paldefender-rest", true);
                break;
            case "rcon":
                Add("rcon", true);
                break;
            case "automation":
                Add("automation", false);
                Add("server-connection", false);
                Add("backup-directory", false);
                break;
            case "maintenance":
                Add("server-connection", false);
                Add("rcon", false);
                Add("backup-directory", false);
                Add("palworld-configuration", false);
                break;
            case "server-statistics":
                Add("server-connection", true);
                break;
            case "save-diff":
                Add("save-directory", true);
                Add("save-index", true);
                break;
            case "player-discipline":
                Add("save-index", false);
                Add("paldefender-rest", false);
                break;
            case "backups":
                Add("save-directory", true);
                Add("backup-directory", true);
                Add("rcon", false);
                break;
            case "diagnostics":
                Add("server-connection", false);
                Add("save-directory", false);
                Add("backup-directory", false);
                break;
            case "incidents":
                Add("server-connection", true);
                Add("backup-directory", false);
                break;
            case "player-insights":
                Add("save-directory", true);
                Add("save-index", true);
                Add("server-connection", false);
                break;
            case "world-governance":
                Add("save-directory", true);
                Add("save-index", true);
                break;
            case "disaster-recovery":
                Add("save-directory", true);
                Add("backup-directory", true);
                Add("backup-record", true);
                Add("rcon", false);
                break;
            case "update-center":
                Add("palworld-configuration", false);
                Add("server-connection", false);
                Add("backup-record", false);
                break;
            case "configuration-versions":
                Add("palworld-configuration", true);
                break;
            case "operations-playbooks":
                Add("server-connection", true);
                Add("automation", false);
                Add("backup-directory", false);
                break;
            case "security-center":
            case "integration-center":
            case "notifications":
            case "notification-history":
            case "catalog":
            case "audit":
            case "system-logs":
            case "users":
            case "about":
                break;
            case "paldefender":
                Add("paldefender-rest", false);
                break;
            case "plugin-management":
                Add("palworld-configuration", false);
                Add("backup-directory", false);
                break;
            case "save-index":
                Add("save-directory", true);
                Add("save-index", false);
                break;
        }

        return selected
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Required).First())
            .ToArray();
    }

    private static IReadOnlyDictionary<string, ConfigurationReadinessCheck> BuildCheckCatalog(ReadinessSignals signals)
    {
        var healthByName = signals.HealthComponents.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        ConfigurationReadinessCheck Connection(
            string code,
            string label,
            bool configured,
            string healthName,
            string missingMessage,
            string actionRoute)
        {
            if (!configured)
                return Check(code, label, "missing", missingMessage, "打开系统配置", actionRoute);

            if (!healthByName.TryGetValue(healthName, out var component))
                return Check(code, label, "unknown", $"{label} 已保存，正在等待首次健康检测。", "重新检测", actionRoute);

            return component.Status switch
            {
                "healthy" => Check(code, label, "ready", $"{label} 可用，延迟 {component.LatencyMs ?? 0} ms。", "查看系统配置", actionRoute),
                "notConfigured" => Check(code, label, "missing", component.Message ?? missingMessage, "打开系统配置", actionRoute),
                _ => Check(code, label, "unavailable", $"{label} 当前不可用：{component.Message ?? component.Status}", "检查系统配置", actionRoute)
            };
        }

        var rest = Connection(
            "palworld-rest",
            "Palworld REST API",
            signals.RestConfigured,
            "palworldRest",
            "请在“系统配置”中填写 Palworld REST 地址、用户名和 AdminPassword，并执行连接测试。",
            "/system/settings#settings-palworld");
        var defender = Connection(
            "paldefender-rest",
            "PalDefender REST API",
            signals.DefenderConfigured,
            "palDefenderRest",
            "请在“系统配置”中填写 PalDefender REST 地址和已加载 Token，并执行连接测试。",
            "/system/settings#settings-paldefender");
        var rcon = Connection(
            "rcon",
            "RCON",
            signals.RconConfigured,
            "rcon",
            "请在“系统配置”中填写 RCON 主机、端口和 AdminPassword，并执行连接测试。",
            "/system/settings#settings-rcon");

        var anyConnectionConfigured = signals.RestConfigured || signals.DefenderConfigured || signals.RconConfigured;
        var connectionItems = new[] { rest, defender, rcon };
        var serverConnection = connectionItems.Any(item => item.Status == "ready")
            ? Check("server-connection", "服务器连接", "ready", "至少一项服务器连接当前可用。", "查看系统配置", "/system/settings")
            : !anyConnectionConfigured
                ? Check("server-connection", "服务器连接", "missing", "请至少配置并测试 Palworld REST、PalDefender REST 或 RCON 其中一项。", "打开系统配置", "/system/settings")
                : connectionItems.Any(item => item.Status == "unavailable")
                    ? Check("server-connection", "服务器连接", "unavailable", "服务器连接已经配置，但当前没有可用连接。请检查服务器进程、端口和凭据。", "检查系统配置", "/system/settings")
                    : Check("server-connection", "服务器连接", "unknown", "服务器连接已保存，正在等待健康检测。", "重新检测", "/system/settings");

        var storage = signals.StorageReady
            ? Check("storage", "PalOps 本地数据", "ready", "PalOps 数据目录可读写。", "查看系统配置", "/system/settings#settings-storage")
            : Check("storage", "PalOps 本地数据", "missing", "自动初始化本地数据失败，请在“系统配置”中查看原因并执行修复。", "修复本地数据", "/system/settings#settings-storage");
        var saveDirectory = signals.SaveDirectoryReady
            ? Check("save-directory", "世界存档目录", "ready", "世界存档目录存在且可访问。", "查看系统配置", "/system/settings#settings-save")
            : Check("save-directory", "世界存档目录", "missing", signals.SaveDirectoryConfigured
                ? "已填写的世界存档目录不存在或当前运行账户无权访问，请检查路径和目录权限。"
                : "请在“系统配置”中填写包含 Level.sav 和 Players 目录的世界存档目录，并执行目录测试。", "配置存档目录", "/system/settings#settings-save");
        var index = signals.SaveIndexAvailable
            ? Check("save-index", "存档索引", "ready", "已经存在可用的存档索引。", "打开存档索引", "/system/save-index")
            : Check("save-index", "存档索引", "missing", "请先配置世界存档目录，再进入“存档索引”执行一次解析。", "建立存档索引", "/system/save-index");
        var backupDirectory = signals.BackupDirectoryReady
            ? Check("backup-directory", "备份目录", "ready", "备份目录存在且可访问。", "查看备份设置", "/system/settings#settings-backup")
            : Check("backup-directory", "备份目录", "missing", "请在“系统配置”中填写可写的备份目录；留空时请确认默认 data/backups/files 可写。", "配置备份目录", "/system/settings#settings-backup");
        var backupRecord = signals.BackupAvailable
            ? Check("backup-record", "已验证备份", "ready", "至少存在一份备份记录。", "打开存档备份", "/operations/backups")
            : Check("backup-record", "已验证备份", "missing", "请进入“存档备份”至少创建并验证一份备份。", "创建备份", "/operations/backups");
        var automation = signals.AutomationEnabled
            ? Check("automation", "自动化调度器", "ready", "自动化调度器已启用。", "查看自动化设置", "/system/settings#settings-automation")
            : Check("automation", "自动化调度器", "disabled", "自动化调度器当前已关闭，定时任务和运维剧本不会自动执行。", "启用自动化", "/system/settings#settings-automation");
        var palworldConfiguration = signals.PalworldConfigurationAvailable
            ? Check("palworld-configuration", "Palworld 配置文件", "ready", "已定位 PalWorldSettings.ini。", "打开 Palworld 配置", "/server/configuration")
            : Check("palworld-configuration", "Palworld 配置文件", "missing", "请进入“Palworld 配置”确认 PalWorldSettings.ini 路径和服务器启动参数。", "定位配置文件", "/server/configuration");

        return new Dictionary<string, ConfigurationReadinessCheck>(StringComparer.OrdinalIgnoreCase)
        {
            [storage.Code] = storage,
            [rest.Code] = rest,
            [defender.Code] = defender,
            [rcon.Code] = rcon,
            [serverConnection.Code] = serverConnection,
            [saveDirectory.Code] = saveDirectory,
            [index.Code] = index,
            [backupDirectory.Code] = backupDirectory,
            [backupRecord.Code] = backupRecord,
            [automation.Code] = automation,
            [palworldConfiguration.Code] = palworldConfiguration
        };
    }

    private static ConfigurationReadinessCheck Check(
        string code,
        string label,
        string status,
        string message,
        string actionLabel,
        string actionRoute) => new(code, label, status, false, message, actionLabel, actionRoute);

    private static int CalculateCompletion(IReadOnlyList<ConfigurationReadinessCheck> checks)
    {
        if (checks.Count == 0) return 100;
        var completed = checks.Count(item => item.Status is "ready" or "unknown" or "unavailable");
        return (int)Math.Round(completed * 100d / checks.Count, MidpointRounding.AwayFromZero);
    }

    private async Task<ReadinessSignals> ReadSignalsAsync(
        bool hasAnyOperationalConfiguration,
        CancellationToken cancellationToken)
    {
        var storageReady = Directory.Exists(paths.DataDirectory)
                           && File.Exists(paths.ResolveDataPath("storage-state.json"));
        var restConfigured = false;
        var defenderConfigured = false;
        var rconConfigured = false;
        var saveDirectoryConfigured = false;
        var saveDirectoryReady = false;
        var backupDirectoryReady = false;
        var automationEnabled = false;

        try
        {
            var summary = await settingsStore.GetSummaryAsync(cancellationToken);
            restConfigured = !string.IsNullOrWhiteSpace(summary.PalworldBaseUrl) && summary.PalworldPasswordConfigured;
            defenderConfigured = !string.IsNullOrWhiteSpace(summary.PalDefenderBaseUrl) && summary.PalDefenderTokenConfigured;
            rconConfigured = !string.IsNullOrWhiteSpace(summary.RconHost) && summary.RconPort > 0 && summary.RconPasswordConfigured;
            saveDirectoryConfigured = !string.IsNullOrWhiteSpace(summary.SaveWorldDirectory);
            saveDirectoryReady = saveDirectoryConfigured && Directory.Exists(summary.SaveWorldDirectory);
            automationEnabled = summary.AutomationEnabled;

            try
            {
                var backupPath = paths.ResolveConfiguredDirectory(summary.BackupDirectory, Path.Combine("backups", "files"));
                backupDirectoryReady = Directory.Exists(backupPath);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Configuration readiness could not resolve the backup directory.");
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Configuration readiness could not read server settings.");
        }

        var saveIndexAvailable = false;
        try { saveIndexAvailable = await saveIndex.GetCurrentAsync(cancellationToken) is not null; }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Configuration readiness could not read the current save index.");
        }

        var backupAvailable = false;
        var palworldConfigurationAvailable = false;
        if (hasAnyOperationalConfiguration)
        {
            try
            {
                var summary = await backupService.GetSummaryAsync(cancellationToken);
                backupDirectoryReady |= Directory.Exists(summary.Directory);
                backupAvailable = summary.Count > 0;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Configuration readiness could not read the backup summary.");
            }

            try { palworldConfigurationAvailable = (await configurationService.GetAsync(cancellationToken)).ConfigurationExists; }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Configuration readiness could not inspect Palworld configuration.");
            }
        }

        return new ReadinessSignals(
            storageReady,
            restConfigured,
            defenderConfigured,
            rconConfigured,
            saveDirectoryConfigured,
            saveDirectoryReady,
            saveIndexAvailable,
            backupDirectoryReady,
            backupAvailable,
            automationEnabled,
            palworldConfigurationAvailable,
            health.Components);
    }

    private sealed record ReadinessSignals(
        bool StorageReady,
        bool RestConfigured,
        bool DefenderConfigured,
        bool RconConfigured,
        bool SaveDirectoryConfigured,
        bool SaveDirectoryReady,
        bool SaveIndexAvailable,
        bool BackupDirectoryReady,
        bool BackupAvailable,
        bool AutomationEnabled,
        bool PalworldConfigurationAvailable,
        IReadOnlyList<HealthComponentV1> HealthComponents);
}
