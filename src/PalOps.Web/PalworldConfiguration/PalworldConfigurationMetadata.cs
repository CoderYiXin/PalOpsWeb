namespace PalOps.Web.PalworldConfiguration;

public sealed class PalworldConfigurationMetadata
{
    private readonly Dictionary<string, PalworldConfigurationFieldMetadata> _fields;

    private PalworldConfigurationMetadata(
        IReadOnlyList<PalworldConfigurationFieldMetadata> fields,
        IReadOnlyList<PalworldConfigurationFieldMetadata> launchArguments)
    {
        Fields = fields;
        LaunchArguments = launchArguments;
        _fields = fields.ToDictionary(static field => field.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PalworldConfigurationFieldMetadata> Fields { get; }
    public IReadOnlyList<PalworldConfigurationFieldMetadata> LaunchArguments { get; }
    public PalworldConfigurationFieldMetadata? Find(string key) => _fields.GetValueOrDefault(key);

    public PalworldConfigurationFieldMetadata ResolveField(string key, string rawValue) =>
        Find(key) ?? InferField(key, rawValue);

    public PalworldConfigurationMetadataResponse ToResponse(IReadOnlyDictionary<string, string> settings)
    {
        var runtimeFields = settings
            .Where(pair => !_fields.ContainsKey(pair.Key))
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => InferField(pair.Key, pair.Value));
        return new(
            "https://docs.palworldgame.com/settings-and-operation/configuration/",
            "https://docs.palworldgame.com/settings-and-operation/arguments/",
            Fields.Concat(runtimeFields).ToArray(),
            LaunchArguments);
    }

    public PalworldConfigurationMetadataResponse ToResponse() =>
        ToResponse(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static PalworldConfigurationMetadata Create()
    {
        var fields = new List<PalworldConfigurationFieldMetadata>
        {
            Text("ServerName", "basic", "服务器名称", "Server name", "サーバー名", "显示在服务器列表中的名称。", "Name shown in the server list.", "サーバー一覧に表示される名前。", "Default Palworld Server"),
            Text("ServerDescription", "basic", "服务器说明", "Server description", "サーバー説明", "服务器列表和连接页面显示的说明。", "Description shown to players.", "プレイヤーに表示する説明。", "", advanced: false),
            Text("AdminPassword", "access", "管理员密码", "Administrator password", "管理者パスワード", "REST、RCON 和管理命令使用的管理员密码。", "Administrator password used by REST, RCON and admin commands.", "REST、RCON、管理コマンドで使用するパスワード。", "", sensitive: true),
            Text("ServerPassword", "access", "入服密码", "Server password", "参加パスワード", "玩家进入服务器时使用的密码。", "Password required to join the server.", "サーバー参加時に必要なパスワード。", "", sensitive: true),
            Integer("PublicPort", "network", "对外公布端口", "Advertised public port", "公開通知ポート", "社区服务器对外公布的公网端口；该配置不会改变服务器监听端口，监听端口由启动参数 -port 控制。", "External port advertised by a community server; this setting does not change the PalServer listening port, which is controlled by -port.", "コミュニティサーバーが公開する外部ポート。この設定では実際の待受ポートは変更されず、待受ポートは -port で指定します。", 8211, 1, 65535, enforceRange: true),
            Text("PublicIP", "network", "公网 IP", "Public IP", "公開 IP", "向服务器列表公开的 IP；留空通常由启动参数或平台处理。", "Public IP advertised to the server list.", "サーバー一覧に公開する IP。", "", advanced: true),
            Integer("ServerPlayerMaxNum", "network", "最大玩家数", "Maximum players", "最大プレイヤー数", "同时在线玩家上限；当前配置工具参考范围为 1 到 512。", "Maximum concurrent players; the current editor reference range is 1 to 512.", "同時接続プレイヤー上限。現在のエディター参照範囲は 1～512。", 32, 1, 512),
            Boolean("bIsUseBackupSaveData", "save", "启用世界备份", "Enable world backup", "ワールドバックアップ", "启用游戏服务器自身的存档备份。", "Enable the game server's own world backup.", "ゲームサーバーのワールドバックアップを有効化。", true),
            Number("AutoSaveSpan", "save", "自动保存间隔（秒）", "Auto-save interval (seconds)", "自動保存間隔（秒）", "世界自动保存间隔；Palworld 使用浮点格式保存该值。", "World auto-save interval; Palworld serializes this value as a floating-point number.", "ワールド自動保存間隔。Palworld は浮動小数形式で保存します。", 30, 0, 86400),
            Number("AutoResetGuildTimeNoOnlinePlayers", "guild", "公会无人在线解散小时数", "Guild auto-reset hours", "ギルド自動解散時間", "公会长期无人在线后自动解散的小时数；Palworld 使用浮点格式保存。", "Hours before an inactive guild is reset; Palworld serializes it as a floating-point number.", "無人ギルドをリセットするまでの時間。Palworld は浮動小数形式で保存します。", 72, 0, 8760),
            Integer("GuildPlayerMaxNum", "guild", "公会最大人数", "Maximum guild members", "ギルド最大人数", "单个公会成员上限。", "Maximum members per guild.", "ギルドの最大メンバー数。", 20, 1, 100),
            Integer("BaseCampMaxNumInGuild", "guild", "公会据点上限", "Guild base limit", "ギルド拠点上限", "单个公会可拥有的据点数量；当前配置工具参考范围为 1 到 50。", "Maximum bases per guild; the current editor reference range is 1 to 50.", "ギルドごとの拠点数。現在のエディター参照範囲は 1～50。", 4, 1, 50),
            Integer("BaseCampWorkerMaxNum", "guild", "据点工作帕鲁上限", "Base worker limit", "拠点作業パル上限", "每个据点可工作的帕鲁数量，上限 50。", "Maximum working Pals per base, up to 50.", "拠点で作業できるパル数、最大 50。", 15, 1, 50),
            Integer("BaseCampMaxNum", "guild", "全服据点参考上限", "Global base reference limit", "全体拠点参考上限", "全服据点数量参考值；过高可能影响性能。", "Reference limit for all bases; high values can affect performance.", "全拠点数の参考値。高すぎると性能に影響。", 128, 1, 512, performanceRisk: true),
            Number("ServerReplicatePawnCullDistance", "performance", "玩家同步距离", "Player replication distance", "プレイヤー同期距離", "玩家同步剔除距离，建议 5000 到 15000。", "Player replication cull distance, recommended 5000 to 15000.", "プレイヤー同期距離。推奨 5000～15000。", 15000, 5000, 15000, performanceRisk: true),
            Integer("RCONPort", "network", "RCON 端口", "RCON port", "RCON ポート", "远程控制台端口。", "Remote console port.", "リモートコンソールのポート。", 25575, 1, 65535, enforceRange: true),
            Boolean("RCONEnabled", "network", "启用 RCON", "Enable RCON", "RCON を有効化", "启用远程控制台。", "Enable the remote console.", "リモートコンソールを有効化。", false),
            Integer("RESTAPIPort", "network", "REST API 端口", "REST API port", "REST API ポート", "Palworld REST API 监听端口。", "Palworld REST API listening port.", "Palworld REST API の待受ポート。", 8212, 1, 65535, enforceRange: true),
            Boolean("RESTAPIEnabled", "network", "启用 REST API", "Enable REST API", "REST API を有効化", "启用官方 REST API。", "Enable the official REST API.", "公式 REST API を有効化。", false),
            Enum("LogFormatType", "logging", "日志格式", "Log format", "ログ形式", "服务器日志输出格式。", "Server log output format.", "サーバーログの出力形式。", "Text", ["Text", "Json"]),
            Enum("DeathPenalty", "rules", "死亡惩罚", "Death penalty", "死亡ペナルティ", "玩家死亡后的掉落规则。", "Drop rule after player death.", "プレイヤー死亡時のドロップ規則。", "All", ["None", "Item", "ItemAndEquipment", "All"]),
            Boolean("bIsPvP", "rules", "启用 PvP", "Enable PvP", "PvP を有効化", "允许玩家之间进行 PvP。", "Allow player-versus-player combat.", "プレイヤー間 PvP を許可。", false),
            Boolean("bCanPickupOtherGuildDeathPenaltyDrop", "rules", "拾取其他公会死亡掉落", "Loot other guild death drops", "他ギルド死亡ドロップ取得", "允许拾取其他公会玩家的死亡掉落。", "Allow looting death drops from other guilds.", "他ギルドの死亡ドロップ取得を許可。", false),
            Boolean("bEnableInvaderEnemy", "rules", "启用入侵事件", "Enable invasion events", "襲撃イベント", "启用据点入侵事件。", "Enable base invasion events.", "拠点襲撃イベントを有効化。", true),
            Boolean("bEnableNonLoginPenalty", "rules", "启用离线惩罚", "Enable offline penalty", "未ログインペナルティ", "对长期离线玩家或公会应用相关惩罚。", "Apply penalties associated with long inactivity.", "長期未ログインに関連するペナルティ。", true),
            Boolean("bEnableFastTravel", "rules", "启用快速传送", "Enable fast travel", "ファストトラベル", "允许快速传送。", "Allow fast travel.", "ファストトラベルを許可。", true),
            Boolean("bIsStartLocationSelectByMap", "rules", "地图选择出生点", "Select start location on map", "開始地点を地図で選択", "允许玩家从地图选择初始位置。", "Allow players to select the starting location on the map.", "マップから開始地点を選択可能。", true),
            Boolean("bExistPlayerAfterLogout", "rules", "登出后保留角色", "Keep player after logout", "ログアウト後にキャラを保持", "玩家登出后角色仍保留在世界中。", "Keep the player character in the world after logout.", "ログアウト後もキャラクターをワールドに保持。", false),
            Boolean("bEnableDefenseOtherGuildPlayer", "rules", "防御其他公会玩家", "Defend against other guild players", "他ギルドプレイヤー防御", "启用针对其他公会玩家的防御规则。", "Enable defense rules against other guild players.", "他ギルドプレイヤーへの防御規則。", false),
            Boolean("bBuildAreaLimit", "rules", "限制建筑区域", "Limit building area", "建築範囲制限", "启用建筑区域限制。", "Enable building area restrictions.", "建築範囲制限を有効化。", false),
            Boolean("bHardcore", "rules", "硬核模式", "Hardcore mode", "ハードコアモード", "启用硬核规则。", "Enable hardcore rules.", "ハードコアルールを有効化。", false),
            Boolean("bPalLost", "rules", "死亡丢失帕鲁", "Lose Pals on death", "死亡時にパルを失う", "死亡时按规则丢失帕鲁。", "Lose Pals according to death rules.", "死亡時にパルを失う規則。", false),
            Boolean("bCharacterRecreateInHardcore", "rules", "硬核角色重建", "Character recreation in hardcore", "ハードコアでキャラ再作成", "硬核模式死亡后允许角色重建。", "Allow character recreation after hardcore death.", "ハードコア死亡後のキャラ再作成。", false),
            Boolean("bPalWorldMode", "rules", "Palworld 模式", "Palworld mode", "Palworld モード", "启用标准 Palworld 世界规则。", "Enable standard Palworld world rules.", "標準 Palworld ワールド規則。", true, advanced: true),
            List("CrossplayPlatforms", "network", "跨平台列表", "Cross-play platforms", "クロスプレイ対象", "允许连接的平台列表。", "Platforms allowed to connect.", "接続を許可するプラットフォーム。", "(Steam,Xbox,PS5,Mac)", ["Steam", "Xbox", "PS5", "Mac"]),
            Number("DayTimeSpeedRate", "balance", "白天速度倍率", "Day speed rate", "昼速度倍率", "白天时间流逝倍率。", "Daytime progression rate.", "昼の時間進行倍率。", 1, 0.1, 5),
            Number("NightTimeSpeedRate", "balance", "夜晚速度倍率", "Night speed rate", "夜速度倍率", "夜晚时间流逝倍率。", "Nighttime progression rate.", "夜の時間進行倍率。", 1, 0.1, 5),
            Number("ExpRate", "balance", "经验倍率", "Experience rate", "経験値倍率", "获得经验值倍率。", "Experience gain multiplier.", "経験値獲得倍率。", 1, 0.1, 20),
            Number("PalCaptureRate", "balance", "捕获倍率", "Capture rate", "捕獲倍率", "帕鲁捕获概率倍率。", "Pal capture rate multiplier.", "パル捕獲率倍率。", 1, 0.1, 10),
            Number("PalSpawnNumRate", "balance", "帕鲁生成倍率", "Pal spawn rate", "パル出現倍率", "帕鲁生成数量倍率；高值会显著增加 CPU 和内存压力。", "Pal spawn multiplier; high values significantly increase CPU and memory load.", "パル出現数倍率。高値は CPU とメモリ負荷を増加。", 1, 0.1, 5, performanceRisk: true),
            Number("PalDamageRateAttack", "balance", "帕鲁攻击伤害倍率", "Pal outgoing damage", "パル与ダメージ倍率", "帕鲁造成的伤害倍率。", "Damage dealt by Pals.", "パルが与えるダメージ倍率。", 1, 0.1, 10),
            Number("PalDamageRateDefense", "balance", "帕鲁承受伤害倍率", "Pal incoming damage", "パル被ダメージ倍率", "帕鲁受到的伤害倍率。", "Damage received by Pals.", "パルが受けるダメージ倍率。", 1, 0.1, 10),
            Number("PlayerDamageRateAttack", "balance", "玩家攻击伤害倍率", "Player outgoing damage", "プレイヤー与ダメージ倍率", "玩家造成的伤害倍率。", "Damage dealt by players.", "プレイヤーが与えるダメージ倍率。", 1, 0.1, 10),
            Number("PlayerDamageRateDefense", "balance", "玩家承受伤害倍率", "Player incoming damage", "プレイヤー被ダメージ倍率", "玩家受到的伤害倍率。", "Damage received by players.", "プレイヤーが受けるダメージ倍率。", 1, 0.1, 10),
            Number("PlayerStomachDecreaceRate", "balance", "玩家饱食度消耗倍率", "Player hunger drain", "プレイヤー空腹減少倍率", "玩家饱食度消耗倍率。", "Player hunger drain multiplier.", "プレイヤー空腹度減少倍率。", 1, 0.1, 10),
            Number("PlayerStaminaDecreaceRate", "balance", "玩家耐力消耗倍率", "Player stamina drain", "プレイヤースタミナ減少倍率", "玩家耐力消耗倍率。", "Player stamina drain multiplier.", "プレイヤースタミナ減少倍率。", 1, 0.1, 10),
            Number("PlayerAutoHPRegeneRate", "balance", "玩家生命恢复倍率", "Player HP regeneration", "プレイヤー HP 回復倍率", "玩家自然生命恢复倍率。", "Player natural HP regeneration multiplier.", "プレイヤー自然 HP 回復倍率。", 1, 0, 10),
            Number("CollectionDropRate", "balance", "采集掉落倍率", "Gathering drop rate", "採集ドロップ倍率", "采集资源掉落倍率。", "Gathering resource drop multiplier.", "採集資源ドロップ倍率。", 1, 0.1, 10),
            Number("CollectionObjectHpRate", "balance", "采集物耐久倍率", "Gatherable object HP", "採集物 HP 倍率", "采集物耐久倍率。", "Gatherable object durability multiplier.", "採集物の耐久倍率。", 1, 0.1, 10),
            Number("CollectionObjectRespawnSpeedRate", "balance", "采集物刷新速度倍率", "Gatherable respawn rate", "採集物再出現倍率", "采集物刷新速度倍率。", "Gatherable object respawn speed multiplier.", "採集物再出現速度倍率。", 1, 0.1, 10),
            Number("EnemyDropItemRate", "balance", "敌人掉落倍率", "Enemy drop rate", "敵ドロップ倍率", "敌人掉落物倍率。", "Enemy item drop multiplier.", "敵アイテムドロップ倍率。", 1, 0, 10),
            Number("BuildObjectDamageRate", "balance", "建筑受伤倍率", "Building damage rate", "建築被ダメージ倍率", "建筑受到的伤害倍率。", "Damage received by buildings.", "建築物が受けるダメージ倍率。", 1, 0, 10),
            Number("BuildObjectDeteriorationDamageRate", "balance", "建筑衰减倍率", "Building deterioration rate", "建築劣化倍率", "建筑自然衰减伤害倍率。", "Building deterioration damage multiplier.", "建築物の自然劣化倍率。", 1, 0, 10),
            Number("DropItemAliveMaxHours", "balance", "掉落物保留小时数", "Dropped item lifetime hours", "ドロップ保持時間", "地面掉落物最大保留时间。", "Maximum lifetime of dropped items.", "地面ドロップの最大保持時間。", 1, 0.1, 240),
            Integer("DropItemMaxNum", "performance", "全服掉落物上限", "Maximum dropped items", "ドロップ最大数", "全服同时存在的掉落物上限。", "Maximum number of dropped items in the world.", "ワールド内ドロップ最大数。", 3000, 100, 10000, performanceRisk: true),
            Integer("DropItemMaxNum_UNKO", "performance", "特殊掉落物上限", "Special dropped item limit", "特殊ドロップ上限", "特殊掉落物数量上限。", "Maximum special dropped items.", "特殊ドロップ最大数。", 100, 0, 10000, performanceRisk: true),
            Number("WorkSpeedRate", "balance", "工作速度倍率", "Work speed rate", "作業速度倍率", "据点工作速度倍率。", "Base work speed multiplier.", "拠点作業速度倍率。", 1, 0.1, 10),
            Integer("CoopPlayerMaxNum", "network", "合作队伍人数", "Co-op party size", "協力プレイ人数", "单个合作队伍最大人数。", "Maximum co-op party size.", "協力パーティ最大人数。", 4, 1, 32),
            Enum("RandomizerType", "rules", "随机化类型", "Randomizer type", "ランダマイザー種類", "帕鲁随机化模式。", "Pal randomization mode.", "パルのランダム化モード。", "None", ["None", "Region", "All"]),
            Text("RandomizerSeed", "rules", "随机种子", "Randomizer seed", "ランダムシード", "随机化使用的种子；空字符串表示不指定。", "Seed used by randomization; an empty string leaves it unspecified.", "ランダム化に使用するシード。空文字列は未指定を表します。", "", advanced: true),
        };

        var launch = new List<PalworldConfigurationFieldMetadata>
        {
            Integer("port", "launch-network", "启动游戏端口", "Launch game port", "起動ゲームポート", "启动命令覆盖游戏连接端口。", "Overrides the game port from the launch command.", "起動コマンドでゲームポートを上書き。", 8211, 1, 65535, enforceRange: true),
            Integer("players", "launch-network", "启动最大玩家数", "Launch maximum players", "起動最大プレイヤー数", "启动命令覆盖最大玩家数。", "Overrides maximum players from the launch command.", "起動コマンドで最大人数を上書き。", 32, 1, 512, enforceRange: true),
            Text("publicip", "launch-network", "启动公网 IP", "Launch public IP", "起動公開 IP", "启动命令公开的 IP。", "Public IP advertised by the launch command.", "起動コマンドで公開する IP。", "", advanced: true),
            Integer("publicport", "launch-network", "启动公网端口", "Launch public port", "起動公開ポート", "启动命令公开的端口。", "Public port advertised by the launch command.", "起動コマンドで公開するポート。", 8211, 1, 65535, advanced: true, enforceRange: true),
            Enum("logformat", "launch", "启动日志格式", "Launch log format", "起動ログ形式", "启动参数日志格式。", "Log format launch argument.", "起動引数のログ形式。", "text", ["text", "json"]),
            Boolean("publiclobby", "launch", "公开大厅", "Public lobby", "公開ロビー", "向公共服务器列表公开服务器。", "Publish the server to the public lobby.", "公開サーバー一覧に掲載。", false),
            Boolean("UsePerfThreads", "launch-performance", "性能线程", "Performance threads", "性能スレッド", "官方旧版性能参数；Palworld 1.0 及以后不设置这些参数可能获得更好性能。", "Official legacy performance switch; on Palworld 1.0 and later, leaving these switches unset may improve performance.", "公式の旧性能スイッチ。Palworld 1.0 以降は未指定の方が性能が向上する場合があります。", false, advanced: true),
            Boolean("NoAsyncLoadingThread", "launch-performance", "禁用异步加载线程", "Disable async loading thread", "非同期ロードスレッド無効", "官方旧版性能参数，需要与其他性能线程参数组合使用；1.0 及以后通常建议先保持未启用。", "Official legacy performance switch used with the other thread switches; on 1.0 and later it is generally best left disabled unless tested.", "他のスレッド引数と組み合わせる公式旧引数。1.0 以降は検証しない限り未指定を推奨します。", false, advanced: true),
            Boolean("UseMultithreadForDS", "launch-performance", "专用服务器多线程", "Dedicated-server multithreading", "専用サーバーマルチスレッド", "官方旧版专用服务器多线程参数；Palworld 1.0 及以后可能不再需要。", "Official legacy dedicated-server multithreading switch; it may no longer be needed on Palworld 1.0 and later.", "公式の旧専用サーバーマルチスレッド引数。Palworld 1.0 以降は不要な場合があります。", false, advanced: true),
            Integer("NumberOfWorkerThreadsServer", "launch-performance", "服务器工作线程数", "Server worker thread count", "サーバーワーカースレッド数", "设置服务器工作线程数；仅在明确测试旧版性能参数组合后使用。", "Sets the server worker-thread count; use only after testing the legacy performance-switch combination.", "サーバーのワーカースレッド数。旧性能引数の組み合わせを検証した場合のみ使用します。", 4, 1, 256, advanced: true, performanceRisk: true),
        };
        return new(fields, launch);
    }

    private static PalworldConfigurationFieldMetadata InferField(string key, string rawValue)
    {
        var value = rawValue.Trim();
        var valueType = "raw";
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            valueType = "string";
        else if (value.Equals("True", StringComparison.OrdinalIgnoreCase) || value.Equals("False", StringComparison.OrdinalIgnoreCase))
            valueType = "boolean";
        else if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
            valueType = "integer";
        else if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            valueType = "number";

        return new(
            key,
            valueType,
            "other",
            key,
            key,
            key,
            "当前 Palworld 配置项。PalOps 会保留原始键名和值；未建立专用元数据时不会据此阻止保存。",
            "Current Palworld setting. PalOps preserves the original key and value and does not block saving when dedicated metadata is unavailable.",
            "現在の Palworld 設定です。専用メタデータがない場合も元のキーと値を保持し、保存を妨げません。",
            rawValue,
            null,
            null,
            null,
            true,
            false,
            false,
            false,
            false);
    }

    private static PalworldConfigurationFieldMetadata Text(string key, string group, string zh, string en, string ja, string zhDesc, string enDesc, string jaDesc, string defaultValue, bool sensitive = false, bool advanced = false) =>
        new(key, sensitive ? "password" : "string", group, zh, en, ja, zhDesc, enDesc, jaDesc, PalworldSettingsIniCodec.Quote(defaultValue), null, null, null, true, sensitive, advanced);
    private static PalworldConfigurationFieldMetadata Boolean(string key, string group, string zh, string en, string ja, string zhDesc, string enDesc, string jaDesc, bool defaultValue, bool advanced = false, bool performanceRisk = false) =>
        new(key, "boolean", group, zh, en, ja, zhDesc, enDesc, jaDesc, defaultValue ? "True" : "False", null, null, null, true, false, advanced, performanceRisk);
    private static PalworldConfigurationFieldMetadata Integer(string key, string group, string zh, string en, string ja, string zhDesc, string enDesc, string jaDesc, int defaultValue, int minimum, int maximum, bool advanced = false, bool performanceRisk = false, bool enforceRange = false) =>
        new(key, "integer", group, zh, en, ja, zhDesc, enDesc, jaDesc, defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture), minimum, maximum, null, true, false, advanced, performanceRisk, enforceRange);
    private static PalworldConfigurationFieldMetadata Number(string key, string group, string zh, string en, string ja, string zhDesc, string enDesc, string jaDesc, double defaultValue, double minimum, double maximum, bool advanced = false, bool performanceRisk = false, bool enforceRange = false) =>
        new(key, "number", group, zh, en, ja, zhDesc, enDesc, jaDesc, defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture), minimum, maximum, null, true, false, advanced, performanceRisk, enforceRange);
    private static PalworldConfigurationFieldMetadata Enum(string key, string group, string zh, string en, string ja, string zhDesc, string enDesc, string jaDesc, string defaultValue, IReadOnlyList<string> options, bool advanced = false) =>
        new(key, "enum", group, zh, en, ja, zhDesc, enDesc, jaDesc, defaultValue, null, null, options, true, false, advanced);
    private static PalworldConfigurationFieldMetadata List(string key, string group, string zh, string en, string ja, string zhDesc, string enDesc, string jaDesc, string defaultValue, IReadOnlyList<string> options) =>
        new(key, "list", group, zh, en, ja, zhDesc, enDesc, jaDesc, defaultValue, null, null, options);
}
