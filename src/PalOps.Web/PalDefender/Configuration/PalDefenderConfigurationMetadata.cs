namespace PalOps.Web.PalDefender.Configuration;

/// <summary>
/// PalDefender configuration field catalog. Names and descriptions are kept
/// outside the JSON files because JSON comments are invalid and may prevent
/// PalDefender from loading a configuration. The web editor renders these
/// entries as inline comments next to the original keys.
/// </summary>
public static class PalDefenderConfigurationMetadata
{
    private const string ConfigDocumentation = "https://ultimeit.github.io/PalDefender/zh/FileTypes/Config/";
    private const string RestDocumentation = "https://ultimeit.github.io/PalDefender/zh/RESTAPI/authentication/";

    public static PalDefenderConfigMetadata Get(string kind)
    {
        var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "config" => new(normalized, ConfigDocumentation, ConfigFields),
            "rest-config" => new(normalized, RestDocumentation, RestConfigFields),
            "rest-token" => new(normalized, RestDocumentation, RestTokenFields),
            "whitelist" => new(normalized, "https://ultimeit.github.io/PalDefender/zh/FileTypes/", IdentifierListFields),
            "banlist" => new(normalized, "https://ultimeit.github.io/PalDefender/zh/FileTypes/", IdentifierListFields),
            "pal-template" => new(normalized, "https://ultimeit.github.io/PalDefender/zh/FileTypes/PalTemplate/", PalTemplateFields),
            "pal-summon" => new(normalized, "https://ultimeit.github.io/PalDefender/zh/FileTypes/PalSummon/", PalSummonFields),
            "import-rule" => new(normalized, "https://ultimeit.github.io/PalDefender/zh/FileTypes/PalImportRules/", ImportRuleFields),
            _ => new(normalized, "https://ultimeit.github.io/PalDefender/zh/FileTypes/", [])
        };
    }

    private static PalDefenderConfigFieldMetadata Field(
        string key,
        string type,
        string group,
        string ChineseName,
        string Description,
        string englishName,
        string englishDescription,
        string japaneseName,
        string japaneseDescription,
        bool deprecated = false,
        string? defaultJson = null) =>
        new(key, type, group, ChineseName, englishName, japaneseName, Description, englishDescription, japaneseDescription, deprecated, defaultJson);

    public static IReadOnlyList<PalDefenderConfigFieldMetadata> ConfigFields { get; } =
    [
        Field("version", "string", "general", "配置版本", "配置版本标识，例如 1.0.0。", "Configuration version", "Version identifier for this configuration, for example 1.0.0.", "設定バージョン", "この設定のバージョン識別子です。例: 1.0.0。", defaultJson: "\"1.0.0\""),
        Field("MOTD", "string-array", "general", "每日消息", "玩家加入时显示的每日消息，支持服务器与玩家占位符。", "Message of the day", "Messages shown when a player joins. Server and player placeholders are supported.", "今日のメッセージ", "プレイヤー参加時に表示するメッセージです。サーバーとプレイヤーのプレースホルダーを使用できます。", defaultJson: "[]"),
        Field("exitServerOnStartupFailure", "boolean", "security", "启动失败时退出服务器", "PalDefender 无法启动时关闭服务器，避免存档在未受保护的状态下运行。", "Exit server on startup failure", "Stops the server when PalDefender cannot start so the save is not run unprotected.", "起動失敗時にサーバーを終了", "PalDefender を起動できない場合にサーバーを停止し、保護なしでセーブを実行しないようにします。", defaultJson: "false"),
        Field("preventAdminPasswordInChat", "boolean", "security", "防止管理员密码泄露", "阻止管理员密码被发送到聊天中。", "Prevent admin password in chat", "Prevents the administrator password from being sent in chat.", "管理者パスワードのチャット送信を防止", "管理者パスワードがチャットに送信されることを防ぎます。", defaultJson: "true"),
        Field("shouldWarnCheaters", "boolean", "security", "警告作弊者", "检测到作弊行为时向玩家发送警告消息。", "Warn detected cheaters", "Sends a warning message when cheating is detected.", "チート検出時に警告", "チートを検出したプレイヤーに警告メッセージを送信します。", defaultJson: "true"),
        Field("shouldWarnCheatersReason", "boolean", "security", "警告中包含原因", "在作弊警告消息中包含检测原因。", "Include warning reason", "Includes the detection reason in the cheating warning.", "警告に理由を含める", "チート警告に検出理由を含めます。", defaultJson: "true"),
        Field("shouldKickCheaters", "boolean", "security", "自动踢出作弊者", "自动踢出检测到的作弊者。", "Kick detected cheaters", "Automatically kicks detected cheaters.", "チーターを自動キック", "検出したチーターを自動的にキックします。", defaultJson: "true"),
        Field("shouldBanCheaters", "boolean", "security", "自动封禁作弊者", "自动封禁检测到的作弊者。", "Ban detected cheaters", "Automatically bans detected cheaters.", "チーターを自動BAN", "検出したチーターを自動的にBANします。", defaultJson: "true"),
        Field("shouldIPBanCheaters", "boolean", "security", "自动 IP 封禁作弊者", "自动对检测到的作弊者执行 IP 封禁。", "IP-ban detected cheaters", "Automatically IP-bans detected cheaters.", "チーターを自動IP BAN", "検出したチーターを自動的にIP BANします。", defaultJson: "false"),
        Field("RCONTimeout", "number", "rcon", "RCON 超时时间", "RCON 连接断开前允许的超时时间。", "RCON timeout", "Time allowed before an inactive RCON connection is closed.", "RCON タイムアウト", "RCON 接続を切断するまでのタイムアウト時間です。"),
        Field("RCONUsePacketIdFix", "boolean", "rcon", "RCON 数据包 ID 修复", "修复 Pocketpair RCON 数据包 ID 的错误实现。", "RCON packet ID fix", "Works around the incorrect Pocketpair RCON packet ID implementation.", "RCON パケットID修正", "Pocketpair の誤った RCON パケットID実装を回避します。", defaultJson: "true"),
        Field("RCONbase64", "boolean", "rcon", "RCON Base64 模式", "启用 Base64 编码的 RCON 命令。", "RCON Base64 mode", "Enables Base64-encoded RCON commands.", "RCON Base64 モード", "Base64 エンコードされた RCON コマンドを有効にします。", defaultJson: "false"),
        Field("logNetworking", "boolean", "logging", "记录网络数据", "记录客户端传入的网络数据。", "Log network traffic", "Logs inbound client network data.", "ネットワークデータを記録", "クライアントから受信したネットワークデータを記録します。", defaultJson: "false"),
        Field("logNetworkingToConsole", "boolean", "logging", "网络日志输出到控制台", "将网络流量日志同时输出到控制台。", "Log networking to console", "Also writes network traffic logs to the console.", "ネットワークログをコンソールへ出力", "ネットワークトラフィックのログをコンソールにも出力します。", defaultJson: "false"),
        Field("logChat", "boolean", "logging", "记录聊天", "记录所有玩家聊天消息。", "Log chat", "Logs all player chat messages.", "チャットを記録", "すべてのプレイヤーチャットを記録します。", defaultJson: "true"),
        Field("logRCON", "boolean", "logging", "记录 RCON", "记录 RCON 命令的使用情况。", "Log RCON", "Logs RCON command usage.", "RCON を記録", "RCON コマンドの使用を記録します。", defaultJson: "true"),
        Field("logPlayerUID", "boolean", "logging", "日志记录 PlayerUID", "在相关日志中记录玩家 PlayerUID。", "Log PlayerUID", "Includes the player PlayerUID in relevant logs.", "PlayerUID を記録", "関連ログにプレイヤーの PlayerUID を含めます。", defaultJson: "true"),
        Field("logPlayerIP", "boolean", "logging", "日志记录玩家 IP", "在相关日志中记录玩家 IP 地址。", "Log player IP", "Includes the player IP address in relevant logs.", "プレイヤーIPを記録", "関連ログにプレイヤーのIPアドレスを含めます。", defaultJson: "true"),
        Field("logPlayerDeaths", "boolean", "logging", "记录玩家死亡", "记录玩家死亡事件。", "Log player deaths", "Logs player death events.", "プレイヤー死亡を記録", "プレイヤーの死亡イベントを記録します。", defaultJson: "true"),
        Field("logPlayerLogins", "boolean", "logging", "记录玩家登录", "记录玩家登录和登出事件。", "Log player logins", "Logs player login and logout events.", "ログインを記録", "プレイヤーのログインとログアウトを記録します。", defaultJson: "true"),
        Field("logPlayerBuildings", "boolean", "logging", "记录建筑操作", "记录建造、取消和拆除操作。", "Log building activity", "Logs building, cancellation, and demolition activity.", "建築操作を記録", "建築、キャンセル、解体の操作を記録します。", defaultJson: "true"),
        Field("logHelicopterKills", "boolean", "logging", "记录直升机击杀", "记录直升机击杀事件。", "Log helicopter kills", "Logs helicopter kill events.", "ヘリコプター撃破を記録", "ヘリコプター撃破イベントを記録します。", defaultJson: "true"),
        Field("logPlayerSummons", "boolean", "logging", "记录 Pal 召唤", "记录玩家召唤 Pal 的事件。", "Log Pal summons", "Logs player Pal summon events.", "Pal 召喚を記録", "プレイヤーによる Pal 召喚を記録します。", defaultJson: "true"),
        Field("logPlayerCaptures", "boolean", "logging", "记录 Pal 捕获", "记录玩家捕获 Pal 的事件。", "Log Pal captures", "Logs Pal capture events.", "Pal 捕獲を記録", "プレイヤーによる Pal 捕獲を記録します。", defaultJson: "true"),
        Field("logCraftings", "boolean", "logging", "记录制作行为", "记录玩家制作行为。", "Log crafting", "Logs player crafting activity.", "クラフトを記録", "プレイヤーのクラフト操作を記録します。", defaultJson: "true"),
        Field("logTechUnlocks", "boolean", "logging", "记录科技解锁", "记录玩家解锁科技的事件。", "Log technology unlocks", "Logs player technology unlock events.", "テクノロジー解放を記録", "プレイヤーのテクノロジー解放を記録します。", defaultJson: "true"),
        Field("logOpenOilrigBoxes", "boolean", "logging", "记录油田箱交互", "记录玩家打开油田箱子的事件。", "Log oil-rig boxes", "Logs oil-rig loot-box interactions.", "オイルリグ箱を記録", "オイルリグの戦利品箱の操作を記録します。", defaultJson: "true"),
        Field("OilrigGoalBoxLocktime", "integer", "gameplay", "油田目标箱锁定时间", "油田目标箱保持锁定的秒数，官方默认值为 300。", "Oil-rig goal box lock time", "Seconds the oil-rig goal box remains locked; the documented default is 300.", "オイルリグ目標箱のロック時間", "オイルリグの目標箱をロックする秒数です。文書上の既定値は300です。", defaultJson: "300"),
        Field("useAdminWhitelist", "boolean", "administration", "启用管理员 IP 白名单", "仅允许 adminIPs 中的 IP 使用管理员权限。", "Use admin IP allowlist", "Allows administrator privileges only from IP addresses in adminIPs.", "管理者IP許可リストを使用", "adminIPs に含まれるIPだけに管理者権限を許可します。", defaultJson: "false"),
        Field("adminAutoLogin", "boolean", "administration", "管理员自动登录", "白名单管理员加入时自动进入管理员模式。", "Administrator auto-login", "Automatically enters administrator mode for allowlisted administrators.", "管理者自動ログイン", "許可リストの管理者が参加したとき自動的に管理者モードにします。", defaultJson: "false"),
        Field("adminIPs", "string-array", "administration", "管理员 IP 列表", "允许使用管理员命令的 IP 地址列表。", "Administrator IP addresses", "IP addresses allowed to use administrator commands.", "管理者IP一覧", "管理者コマンドを使用できるIPアドレス一覧です。", defaultJson: "[]"),
        Field("bannedIPs", "string-array", "legacy", "旧版 IP 封禁列表", "已废弃。请使用 Banlist.json 以及 /banip、/unbanip。", "Legacy banned IP list", "Deprecated. Use Banlist.json and /banip or /unbanip instead.", "旧IP BAN一覧", "非推奨です。Banlist.json と /banip、/unbanip を使用してください。", true, "[]"),
        Field("bannedChatWords", "string-array", "chat", "聊天屏蔽词", "过滤聊天中的指定词语，例如广告内容。", "Blocked chat words", "Filters specified words from chat, such as advertising content.", "チャット禁止語", "広告など、指定した語句をチャットからフィルターします。", defaultJson: "[]"),
        Field("bannedMessage", "string", "legacy", "旧版封禁消息", "已废弃。旧版 Config 中的封禁消息设置。", "Legacy ban message", "Deprecated legacy Config-based ban message.", "旧BANメッセージ", "非推奨の旧 Config ベースBANメッセージです。", true),
        Field("bannedNames", "string-array", "security", "禁止使用的名称", "不允许玩家使用的名称列表。", "Blocked player names", "Player names that are not allowed.", "禁止プレイヤー名", "使用を許可しないプレイヤー名の一覧です。", defaultJson: "[]"),
        Field("pvpMaxToBuildingDamage", "integer", "gameplay", "PvP 建筑最大伤害", "允许的最大 PvP 建筑伤害。", "Maximum PvP building damage", "Maximum allowed PvP damage to buildings.", "PvP建築最大ダメージ", "PvPで建築物に与えられる最大ダメージです。"),
        Field("pvpMaxToPalDamage", "integer", "gameplay", "PvP Pal 最大伤害", "允许的最大 PvP Pal 伤害。", "Maximum PvP Pal damage", "Maximum allowed PvP damage to Pals.", "PvP Pal最大ダメージ", "PvPでPalに与えられる最大ダメージです。"),
        Field("pveMaxToPalBanThreshold", "integer", "security", "PvE Pal 伤害封禁阈值", "触发作弊检测的 PvE Pal 伤害阈值。", "PvE Pal ban threshold", "PvE Pal damage threshold that triggers cheat detection.", "PvE Pal BANしきい値", "チート検出を発生させるPvE Palダメージのしきい値です。"),
        Field("treeLimiter", "number", "gameplay", "砍树频率限制", "玩家摧毁一棵树的最短间隔，用于降低大量树木同时销毁造成的卡顿。", "Tree destruction limiter", "Minimum interval between destroyed trees to reduce lag from mass destruction.", "木の破壊頻度制限", "大量の木が同時に破壊される負荷を抑えるための最短間隔です。"),
        Field("allowAdminCheats", "boolean", "administration", "允许管理员作弊命令", "允许管理员使用 godmode 等作弊命令。", "Allow administrator cheats", "Allows administrators to use commands such as godmode.", "管理者チートを許可", "管理者が godmode などのコマンドを使用できるようにします。", defaultJson: "false"),
        Field("allowGodmodeOnehit", "boolean", "administration", "Godmode 一击必杀", "允许 godmode 一击击杀任何目标。", "Allow godmode one-hit", "Allows godmode to defeat any target in one hit.", "Godmode一撃を許可", "godmode で任意の対象を一撃で倒せるようにします。", defaultJson: "false"),
        Field("adminCheats", "string-array", "administration", "管理员作弊命令列表", "指定被视为管理员作弊的命令。", "Administrator cheat commands", "Commands treated as administrator cheats.", "管理者チートコマンド一覧", "管理者チートとして扱うコマンドを指定します。", defaultJson: "[]"),
        Field("isChineseCmd", "boolean", "rcon", "中文控制台编码", "启用旧版控制台中文编码模式。", "Chinese console encoding", "Enables the legacy Chinese console encoding mode.", "中国語コンソール文字コード", "旧式の中国語コンソール文字コードを有効にします。", defaultJson: "false"),
        Field("announceConnections", "boolean", "announcements", "公告玩家连接", "在聊天中公告玩家加入和离开。", "Announce connections", "Announces player joins and leaves in chat.", "接続を告知", "プレイヤーの参加と退出をチャットで告知します。", defaultJson: "false"),
        Field("dontAnnounceAdminConnections", "boolean", "announcements", "隐藏管理员连接公告", "不显示管理员加入和离开的公告。", "Hide administrator connections", "Suppresses join and leave announcements for administrators.", "管理者接続を非表示", "管理者の参加と退出の告知を表示しません。", defaultJson: "false"),
        Field("announcePunishments", "boolean", "announcements", "公告处罚", "向所有玩家公告作弊封禁和踢出。", "Announce punishments", "Announces cheat bans and kicks to all players.", "処罰を告知", "チートBANとキックを全プレイヤーに告知します。", defaultJson: "false"),
        Field("announcePlayerDeaths", "boolean", "announcements", "公告玩家死亡", "在聊天中显示公开死亡消息。", "Announce player deaths", "Shows public player death messages in chat.", "プレイヤー死亡を告知", "プレイヤーの死亡メッセージをチャットに表示します。", defaultJson: "false"),
        Field("announceOpenOilrigBoxes", "boolean", "announcements", "公告油田战利品", "在聊天中公告油田箱子事件。", "Announce oil-rig boxes", "Announces oil-rig loot-box events in chat.", "オイルリグ箱を告知", "オイルリグの戦利品箱イベントをチャットで告知します。", defaultJson: "false"),
        Field("announceHelicopterKills", "boolean", "announcements", "公告直升机击杀", "在聊天中公告直升机击杀事件。", "Announce helicopter kills", "Announces helicopter kill events in chat.", "ヘリコプター撃破を告知", "ヘリコプター撃破イベントをチャットで告知します。", defaultJson: "false"),
        Field("announcePlayerSummons", "boolean", "announcements", "公告玩家召唤", "在聊天中公告玩家召唤 Pal。", "Announce player summons", "Announces player Pal summons in chat.", "プレイヤー召喚を告知", "プレイヤーによるPal召喚をチャットで告知します。", defaultJson: "false"),
        Field("announceAdminSummons", "boolean", "announcements", "公告管理员召唤", "在聊天中公告管理员命令召唤 Pal。", "Announce administrator summons", "Announces administrator-command Pal summons in chat.", "管理者召喚を告知", "管理者コマンドによるPal召喚をチャットで告知します。", defaultJson: "false"),
        Field("announceAdminSummonsKill", "boolean", "announcements", "公告管理员召唤物被击杀", "玩家击杀管理员召唤的 Pal 时进行公告。", "Announce administrator summon kills", "Announces when a player kills a Pal summoned by an administrator.", "管理者召喚Pal撃破を告知", "管理者が召喚したPalをプレイヤーが倒したとき告知します。", defaultJson: "false"),
        Field("chatBypassWait", "boolean", "chat", "取消聊天冷却", "移除聊天消息之间的冷却时间。", "Bypass chat cooldown", "Removes the delay between chat messages.", "チャット待機時間を無効化", "チャットメッセージ間の待機時間を削除します。", defaultJson: "false"),
        Field("chatMessageMaxLen", "integer", "chat", "聊天最大长度", "允许的最大聊天消息长度。", "Maximum chat message length", "Maximum allowed chat message length.", "チャット最大文字数", "許可するチャットメッセージの最大長です。"),
        Field("useWhitelist", "boolean", "security", "启用玩家白名单", "启用 WhiteList.json 玩家白名单。", "Use player allowlist", "Enables the WhiteList.json player allowlist.", "プレイヤー許可リストを使用", "WhiteList.json のプレイヤー許可リストを有効にします。", defaultJson: "false"),
        Field("whitelistMessage", "string", "security", "非白名单提示", "显示给不在白名单中的玩家的消息。", "Allowlist rejection message", "Message shown to players who are not on the allowlist.", "許可リスト拒否メッセージ", "許可リストにないプレイヤーへ表示するメッセージです。"),
        Field("steamidProtection", "boolean", "security", "SteamID/UserId 保护", "防止使用相同 UserId 重复登录。", "SteamID/UserId protection", "Prevents duplicate logins using the same UserId.", "SteamID/UserId保護", "同じUserIdを使用した重複ログインを防ぎます。", defaultJson: "true"),
        Field("blockTowerBossCapture", "boolean", "gameplay", "禁止捕获高塔 Boss", "阻止玩家捕获高塔 Boss。", "Block tower-boss capture", "Prevents players from capturing tower bosses.", "塔ボス捕獲を禁止", "プレイヤーが塔ボスを捕獲することを防ぎます。", defaultJson: "true"),
        Field("disableIllegalItemProtection", "boolean", "security", "禁用非法物品保护", "禁用对改造或调试物品的保护检查。开启后会降低安全性。", "Disable illegal-item protection", "Disables protection against modified or debug items. Enabling this reduces security.", "不正アイテム保護を無効化", "改造・デバッグアイテムに対する保護を無効にします。有効化すると安全性が低下します。", defaultJson: "false"),
        Field("disableButchering", "boolean", "gameplay", "禁用屠宰", "禁止玩家执行屠宰行为。", "Disable butchering", "Prevents players from butchering.", "解体を無効化", "プレイヤーによる解体を禁止します。", defaultJson: "false"),
        Field("disableRenaming", "boolean", "gameplay", "禁用角色重命名", "禁止角色重命名。", "Disable character renaming", "Prevents character renaming.", "キャラクター名変更を無効化", "キャラクターの名前変更を禁止します。", defaultJson: "false"),
        Field("disablePalRenaming", "boolean", "gameplay", "禁用 Pal 重命名", "禁止 Pal 重命名。", "Disable Pal renaming", "Prevents Pal renaming.", "Pal名変更を無効化", "Pal の名前変更を禁止します。", defaultJson: "false"),
        Field("doActionUponIllegalPalStats", "boolean", "security", "处理非法 Pal 属性", "检测到非法 Pal 属性 exploit 时自动执行处罚动作。", "Act on illegal Pal stats", "Automatically applies the configured action when illegal Pal stats are detected.", "不正Palステータスに対応", "不正なPalステータスを検出したとき設定済みの処置を実行します。", defaultJson: "true"),
        Field("palStatsMaxRank", "integer", "security", "Pal 最大强化等级", "允许的最大 Pal 强化等级，-1 表示自动检测。", "Maximum Pal rank", "Maximum allowed Pal rank; -1 selects automatic detection.", "Pal最大ランク", "許可するPalの最大強化ランクです。-1は自動検出です。", defaultJson: "-1"),
        Field("bannedTechnologies", "string-array", "gameplay", "禁止科技列表", "阻止学习指定科技，并在玩家加入时移除这些科技。", "Blocked technologies", "Prevents specified technologies from being learned and removes them when a player joins.", "禁止テクノロジー", "指定テクノロジーの習得を防ぎ、参加時に削除します。", defaultJson: "[]"),
        Field("PalImport_Disabled", "boolean", "legacy", "旧版：禁用 Pal 导入", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: disable Pal import", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: Palインポート無効", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true),
        Field("PalImport_BanIfPalIsImpossible", "boolean", "legacy", "旧版：异常 Pal 导入封禁", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: ban impossible Pal imports", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: 不可能PalのインポートをBAN", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true),
        Field("PalImport_BannedPalIDs", "string-array", "legacy", "旧版：禁止导入 Pal ID", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: blocked Pal IDs", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: 禁止Pal ID", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true),
        Field("PalImport_AllowGenderNone", "boolean", "legacy", "旧版：允许无性别", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: allow no gender", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: 性別なしを許可", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true),
        Field("PalImport_MaxLevel", "integer", "legacy", "旧版：导入最大等级", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: maximum import level", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: インポート最大レベル", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true),
        Field("PalImport_MaxRank", "integer", "legacy", "旧版：导入最大等级强化", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: maximum import rank", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: インポート最大ランク", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true),
        Field("PalImport_MaxSoulHP", "integer", "legacy", "旧版：生命魂上限", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: maximum HP soul", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: HPソウル上限", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true),
        Field("PalImport_MaxSoulATK", "integer", "legacy", "旧版：攻击魂上限", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: maximum attack soul", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: 攻撃ソウル上限", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true),
        Field("PalImport_MaxSoulDEF", "integer", "legacy", "旧版：防御魂上限", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: maximum defense soul", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: 防御ソウル上限", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true),
        Field("PalImport_MaxSoulCS", "integer", "legacy", "旧版：制作速度魂上限", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: maximum craft-speed soul", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: 作業速度ソウル上限", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true),
        Field("PalImport_MaxIV", "integer", "legacy", "旧版：IV 上限", "已废弃。请改用 Pals/ImportRules/Default.json。", "Legacy: maximum IV", "Deprecated. Use Pals/ImportRules/Default.json.", "旧: IV上限", "非推奨です。Pals/ImportRules/Default.json を使用してください。", true)
    ];

    public static IReadOnlyList<PalDefenderConfigFieldMetadata> RestConfigFields { get; } =
    [
        Field("Enabled", "boolean", "rest-api", "启用 REST API", "启用 PalDefender REST API。修改后必须重启服务器。", "Enable REST API", "Enables the PalDefender REST API. A server restart is required.", "REST APIを有効化", "PalDefender REST APIを有効にします。変更後はサーバー再起動が必要です。", defaultJson: "false"),
        Field("Port", "integer", "rest-api", "监听端口", "REST API 监听端口，官方默认值为 17993。不要直接暴露到公网。", "Listening port", "REST API listening port. The documented default is 17993. Do not expose it directly to the public Internet.", "待受ポート", "REST APIの待受ポートです。文書上の既定値は17993です。インターネットへ直接公開しないでください。", defaultJson: "17993")
    ];

    public static IReadOnlyList<PalDefenderConfigFieldMetadata> RestTokenFields { get; } =
    [
        Field("Name", "string", "rest-token", "令牌名称", "用于识别调用方。建议每个人或服务使用独立令牌。", "Token name", "Identifies the caller. Use a separate token for each person or service.", "トークン名", "呼び出し元を識別します。ユーザーまたはサービスごとに個別のトークンを使用してください。"),
        Field("Token", "string", "rest-token", "Bearer 令牌", "用于 Authorization: Bearer 请求头的机密令牌。请按密码保护。", "Bearer token", "Secret used in the Authorization: Bearer header. Protect it as a password.", "Bearerトークン", "Authorization: Bearer ヘッダーで使用する秘密値です。パスワードとして保護してください。"),
        Field("Permissions", "string-or-string-array", "rest-token", "权限", "允许的 REST 权限。可填写字符串或字符串数组；REST.* 表示完整访问。", "Permissions", "Allowed REST permissions. A string or string array is accepted; REST.* grants full access.", "権限", "許可するREST権限です。文字列または文字列配列を使用でき、REST.* は完全アクセスです。", defaultJson: "[\"REST.*\"]")
    ];

    private static IReadOnlyList<PalDefenderConfigFieldMetadata> IdentifierListFields { get; } =
    [
        Field("UserID/IP", "string", "identity", "User ID 或 IP 地址", "支持 User ID、单个 IP 地址以及文档允许的地址范围格式。", "User ID or IP address", "Supports user IDs, single IP addresses, and documented address-range formats.", "User ID またはIPアドレス", "User ID、単一IPアドレス、文書で許可されたアドレス範囲形式を使用できます。")
    ];

    private static IReadOnlyList<PalDefenderConfigFieldMetadata> PalTemplateFields { get; } =
    [
        Field("PalID", "string", "identity", "Pal ID", "要生成或导入的 Pal 内部标识符，必填；请使用精确的 PalID。", "Pal ID", "Required internal Pal identifier to generate or import. Use the exact PalID.", "Pal ID", "生成またはインポートするPalの内部識別子です。必須で、正確なPalIDを使用します。"),
        Field("UniqueNPCID", "string", "identity", "唯一 NPC ID", "生成 NPC Pal 时使用的可选内部 ID。", "Unique NPC ID", "Optional internal ID used when generating an NPC Pal.", "固有NPC ID", "NPC Palを生成するときに使用する任意の内部IDです。"),
        Field("Nickname", "string", "identity", "昵称", "为 Pal 设置的可选昵称。", "Nickname", "Optional nickname assigned to the Pal.", "ニックネーム", "Palに設定する任意のニックネームです。"),
        Field("SkinId", "string", "identity", "皮肤 ID", "Pal 外观覆盖 ID；可使用 /getskinids 查询。", "Skin ID", "Pal appearance override ID; use /getskinids to list valid IDs.", "スキンID", "Palの外観を上書きするIDです。/getskinidsで確認できます。"),
        Field("Gender", "string", "identity", "性别", "只能填写 Male、Female 或 None。", "Gender", "Must be Male, Female, or None.", "性別", "Male、Female、None のいずれかです。", defaultJson: "\"None\""),
        Field("Level", "integer", "stats", "等级", "Pal 等级，必须大于等于 1。", "Level", "Pal level; must be at least 1.", "レベル", "Palのレベルです。1以上である必要があります。", defaultJson: "1"),
        Field("Exp", "integer", "stats", "经验值", "Pal 当前经验值。", "Experience", "Current Pal experience value.", "経験値", "Palの現在の経験値です。", defaultJson: "0"),
        Field("Shiny", "boolean", "identity", "闪光 Pal", "设置该 Pal 是否为闪光。", "Shiny", "Controls whether this Pal is shiny.", "ラッキーPal", "このPalをラッキー個体にするかを指定します。", defaultJson: "false"),
        Field("PartnerSkillLevel", "integer", "stats", "伙伴技能等级", "伙伴技能等级，不能低于 1。", "Partner skill level", "Partner skill level; must not be below 1.", "パートナースキルレベル", "パートナースキルのレベルです。1未満にはできません。", defaultJson: "1"),
        Field("CondensedPals", "integer", "stats", "浓缩 Pal 数量", "已经浓缩或合并到该 Pal 中的 Pal 数量。", "Condensed Pal count", "Number of Pals already condensed into this Pal.", "濃縮Pal数", "このPalに濃縮済みのPal数です。", defaultJson: "0"),
        Field("UnusedStatusPoints", "integer", "stats", "未使用属性点", "尚未分配的属性点；该字段可能主要用于玩家角色数据。", "Unused status points", "Unallocated status points; this field may primarily apply to player-character data.", "未使用ステータスポイント", "未割り当てのステータスポイントです。主にプレイヤーキャラクター向けの場合があります。", defaultJson: "0"),
        Field("FriendshipPoints", "integer", "stats", "友好度", "Pal 的友好度数值。", "Friendship points", "Pal friendship value.", "親密度", "Palの親密度の値です。", defaultJson: "0"),
        Field("PhysicalHealth", "string", "status", "身体健康状态", "有效值：Healthful、MinorInjury、Severe、Dying、DeadBody、CloudCemetery。", "Physical health", "Valid values: Healthful, MinorInjury, Severe, Dying, DeadBody, or CloudCemetery.", "身体状態", "有効値: Healthful、MinorInjury、Severe、Dying、DeadBody、CloudCemetery。", defaultJson: "\"Healthful\""),
        Field("WorkerSick", "string", "status", "工作疾病状态", "有效值：None、Cold、Sprain、Bulimia、GastricUlcer、Fracture、Weakness、DepressionSprain、DisturbingElement。", "Worker sickness", "Valid values: None, Cold, Sprain, Bulimia, GastricUlcer, Fracture, Weakness, DepressionSprain, or DisturbingElement.", "作業疾病状態", "有効値: None、Cold、Sprain、Bulimia、GastricUlcer、Fracture、Weakness、DepressionSprain、DisturbingElement。", defaultJson: "\"None\""),
        Field("ImportedCharacter", "boolean", "identity", "标记为导入角色", "将该 Pal 标记为导入角色。", "Imported character", "Marks this Pal as an imported character.", "インポートキャラクター", "このPalをインポート済みキャラクターとして扱います。", defaultJson: "false"),
        Field("HP", "number", "stats", "生命值 HP", "基础生命值。", "HP", "Base health value.", "HP", "基本HP値です。", defaultJson: "0"),
        Field("SP", "number", "stats", "耐力值 SP", "基础耐力值。", "SP", "Base stamina value.", "SP", "基本スタミナ値です。", defaultJson: "0"),
        Field("MP", "number", "stats", "法力值 MP", "基础法力值。", "MP", "Base mana value.", "MP", "基本マナ値です。", defaultJson: "0"),
        Field("Shield", "number", "stats", "护盾值", "Pal 的护盾数值。", "Shield", "Pal shield value.", "シールド", "Palのシールド値です。", defaultJson: "0"),
        Field("Hunger", "integer", "status", "当前饥饿值", "Pal 当前饥饿值。", "Current hunger", "Current Pal hunger value.", "現在の空腹度", "Palの現在の空腹度です。", defaultJson: "0"),
        Field("MaxHunger", "integer", "status", "最大饥饿值", "Pal 最大饥饿值。", "Maximum hunger", "Maximum Pal hunger value.", "最大空腹度", "Palの最大空腹度です。", defaultJson: "0"),
        Field("SAN", "integer", "status", "SAN 值", "Pal 的精神稳定度。", "SAN", "Pal mental-stability value.", "SAN値", "Palの精神安定度です。", defaultJson: "100"),
        Field("Support", "integer", "stats", "支援等级", "用于 AI 行为和技能的支援等级。", "Support", "Support level used by AI behavior and skills.", "サポート", "AIの挙動やスキルで使用されるサポート値です。", defaultJson: "0"),
        Field("CraftSpeed", "integer", "work", "制作速度", "Pal 的制作速度倍率或覆盖值。", "Craft speed", "Pal crafting-speed multiplier or override value.", "作業速度", "Palの作業速度倍率または上書き値です。", defaultJson: "0"),
        Field("PalSouls", "object", "enhancement", "Pal 魂强化", "Pal 魂强化对象，包含 Health、Attack、Defense、CraftSpeed。数值上限受导入规则控制。", "Pal soul upgrades", "Pal soul upgrade object containing Health, Attack, Defense, and CraftSpeed. Limits are controlled by import rules.", "Palソウル強化", "Health、Attack、Defense、CraftSpeedを含むPalソウル強化オブジェクトです。上限はインポートルールで制御されます。", defaultJson: "{\"Health\":0,\"Attack\":0,\"Defense\":0,\"CraftSpeed\":0}"),
        Field("IVs", "object", "enhancement", "个体值 IV", "个体值对象，包含 Health、AttackMelee、AttackShot、Defense。数值上限受导入规则控制。", "IVs", "IV object containing Health, AttackMelee, AttackShot, and Defense. Limits are controlled by import rules.", "IV", "Health、AttackMelee、AttackShot、Defenseを含むIVオブジェクトです。上限はインポートルールで制御されます。", defaultJson: "{\"Health\":0,\"AttackMelee\":0,\"AttackShot\":0,\"Defense\":0}"),
        Field("ActiveSkills", "string-array", "skills", "当前装备技能", "当前装备的攻击技能列表，官方建议最多 3 个；额外技能请放入 LearntSkills。", "Active skills", "Currently equipped attack skills. The documentation recommends at most three; place additional learned skills in LearntSkills.", "装備中スキル", "現在装備している攻撃スキルです。文書では最大3個を推奨し、それ以外はLearntSkillsへ入れます。", defaultJson: "[]"),
        Field("LearntSkills", "string-array", "skills", "已学习技能", "已学习并可切换、但不一定当前装备的技能列表。", "Learned skills", "Skills learned and available to switch, but not necessarily currently equipped.", "習得スキル", "習得済みで切り替え可能ですが、現在装備しているとは限らないスキル一覧です。", defaultJson: "[]"),
        Field("Passives", "string-array", "skills", "被动词条", "Pal 拥有的被动词条列表；普通 Pal 通常最多 4 个。", "Passives", "Pal passive traits. Ordinary Pals normally use up to four.", "パッシブ", "Palのパッシブ特性一覧です。通常のPalでは一般に最大4個です。", defaultJson: "[]"),
        Field("ExtraWorkSuitabilities", "object", "work", "额外工作适应性", "工作类型与额外等级的对象，例如 {\"Mining\": 2}。必须使用官方工作类型 ID。", "Extra work suitabilities", "Object mapping work-type IDs to extra levels, for example {\"Mining\": 2}. Use documented work-type IDs.", "追加作業適性", "作業タイプIDと追加レベルの対応です。例: {\"Mining\": 2}。文書化されたIDを使用します。", defaultJson: "{}"),
        Field("DisableWorkPreferences", "string-array", "work", "禁用工作偏好", "Pal 拒绝从事的工作类型列表；必须使用官方工作类型 ID。", "Disabled work preferences", "Work-type IDs this Pal refuses to perform. Use documented work-type IDs.", "無効な作業希望", "このPalが拒否する作業タイプID一覧です。文書化されたIDを使用します。", defaultJson: "[]")
    ];

    private static IReadOnlyList<PalDefenderConfigFieldMetadata> PalSummonFields { get; } =
    [
        Field("PalTemplate", "string", "summon", "Pal 模板", "Pals/Templates 中模板文件的名称，必填；文件必须已存在。", "Pal template", "Required template filename from Pals/Templates; the file must already exist.", "Palテンプレート", "Pals/Templates内のテンプレートファイル名です。必須で、ファイルが存在する必要があります。"),
        Field("Uncapturable", "boolean", "summon", "不可捕获", "为 true 时玩家不能捕获该召唤 Pal；省略时默认为 false。", "Uncapturable", "When true, players cannot capture the summoned Pal; defaults to false when omitted.", "捕獲不可", "trueの場合、プレイヤーは召喚Palを捕獲できません。省略時はfalseです。", defaultJson: "false"),
        Field("X", "number", "coordinates", "X 坐标", "召唤位置的必填世界 X 坐标，可使用 /getpos 获取。", "X coordinate", "Required world X coordinate for the summon position; use /getpos to obtain it.", "X座標", "召喚位置の必須ワールドX座標です。/getposで取得できます。", defaultJson: "0"),
        Field("Y", "number", "coordinates", "Y 坐标", "召唤位置的必填世界 Y 坐标，可使用 /getpos 获取。", "Y coordinate", "Required world Y coordinate for the summon position; use /getpos to obtain it.", "Y座標", "召喚位置の必須ワールドY座標です。/getposで取得できます。", defaultJson: "0"),
        Field("Z", "number", "coordinates", "Z 坐标", "召唤位置的必填世界 Z 坐标，可使用 /getpos 获取。", "Z coordinate", "Required world Z coordinate for the summon position; use /getpos to obtain it.", "Z座標", "召喚位置の必須ワールドZ座標です。/getposで取得できます。", defaultJson: "0"),
        Field("DisableStatuses", "string-array", "status", "禁用状态效果", "需要对该召唤 Pal 禁用的状态效果列表；无效或空状态名会被 PalDefender 跳过。", "Disabled statuses", "Status effects disabled for the summoned Pal. PalDefender skips invalid or empty status names.", "無効化する状態異常", "召喚Palで無効化する状態効果一覧です。無効または空の名前はPalDefenderによって無視されます。", defaultJson: "[]")
    ];

    private static IReadOnlyList<PalDefenderConfigFieldMetadata> ImportRuleFields { get; } =
    [
        Field("PalSelectionMode", "string", "selection", "Pal 选择模式", "仅用于 Default.json。AllowAllExceptBanned 允许除禁止列表外的所有 Pal；AllowOnlyListed 仅允许允许列表。", "Pal selection mode", "Default.json only. AllowAllExceptBanned permits every Pal except blocked IDs; AllowOnlyListed permits only allowed IDs.", "Pal選択モード", "Default.json専用です。AllowAllExceptBannedはBAN一覧以外を許可し、AllowOnlyListedは許可一覧のみを許可します。", defaultJson: "\"AllowAllExceptBanned\""),
        Field("AllowedPalIDs", "string-array", "selection", "允许的 Pal ID", "仅用于 Default.json；PalSelectionMode 为 AllowOnlyListed 时允许导入的精确 PalID 列表。", "Allowed Pal IDs", "Default.json only. Exact PalIDs allowed when PalSelectionMode is AllowOnlyListed.", "許可Pal ID", "Default.json専用です。PalSelectionModeがAllowOnlyListedの場合に許可する正確なPalID一覧です。", defaultJson: "[]"),
        Field("BannedPalIDs", "string-array", "selection", "禁止的 Pal ID", "仅用于 Default.json；始终拒绝导入的精确 PalID 列表。", "Blocked Pal IDs", "Default.json only. Exact PalIDs that are always rejected.", "禁止Pal ID", "Default.json専用です。常に拒否する正確なPalID一覧です。", defaultJson: "[]"),
        Field("MaxValueLimitAction", "string", "actions", "超限数值处理", "BlockImport 拒绝超限模板；ClampToMaxValues 将数值降低到配置上限。", "Maximum-value action", "BlockImport rejects over-limit templates; ClampToMaxValues reduces values to configured limits.", "上限超過時の処理", "BlockImportは上限超過テンプレートを拒否し、ClampToMaxValuesは設定上限へ下げます。", defaultJson: "\"BlockImport\""),
        Field("DisallowedPassivesAction", "string", "actions", "禁用被动处理", "BlockImport 拒绝含禁用被动的模板；RemoveFromPal 在导入前移除这些被动。", "Disallowed-passive action", "BlockImport rejects templates containing blocked passives; RemoveFromPal removes them before import.", "禁止パッシブの処理", "BlockImportは禁止パッシブを含むテンプレートを拒否し、RemoveFromPalはインポート前に削除します。", defaultJson: "\"BlockImport\""),
        Field("DisallowedPassives", "string-array", "actions", "禁用被动列表", "由 DisallowedPassivesAction 处理的精确 PassiveID 列表。", "Disallowed passives", "Exact PassiveIDs handled by DisallowedPassivesAction.", "禁止パッシブ一覧", "DisallowedPassivesActionの対象となる正確なPassiveID一覧です。", defaultJson: "[]"),
        Field("Disabled", "boolean", "general", "禁用规则", "为 true 时禁用匹配规则集的导入检查。", "Disable rule", "Disables import checks for the matching rule set when true.", "ルールを無効化", "trueの場合、該当ルールセットのインポート検査を無効にします。", defaultJson: "false"),
        Field("BanIfPalIsImpossible", "boolean", "actions", "异常 Pal 导入时处罚", "为 true 时，PalDefender 可根据服务器设置处罚不可能合法存在的 Pal 导入。", "Punish impossible Pal imports", "Allows PalDefender to punish impossible Pal imports according to server settings.", "不可能Palインポートを処罰", "サーバー設定に従い、合法的に存在できないPalのインポートを処罰できるようにします。", defaultJson: "false"),
        Field("AllowGenderNone", "boolean", "limits", "允许无性别", "为 false 时，Gender 为 None 的模板可能被导入检查拒绝。", "Allow no gender", "When false, templates using Gender: None may be rejected by import checks.", "性別なしを許可", "falseの場合、GenderがNoneのテンプレートはインポート検査で拒否される可能性があります。", defaultJson: "false"),
        Field("MaxLevel", "integer", "limits", "最大等级", "导入模板允许的最高 Pal 等级。", "Maximum level", "Highest Pal level allowed in imported templates.", "最大レベル", "インポートテンプレートで許可するPalの最高レベルです。", defaultJson: "65"),
        Field("MaxRank", "integer", "limits", "最大伙伴技能等级", "导入模板允许的最高伙伴技能等级。", "Maximum partner-skill rank", "Highest partner-skill level allowed in imported templates.", "最大パートナースキルレベル", "インポートテンプレートで許可するパートナースキルの最高レベルです。", defaultJson: "5"),
        Field("PalSouls", "object", "limits", "Pal 魂上限", "允许的 Pal 魂强化上限，包含 Health、Attack、Defense、CraftSpeed。", "Pal soul limits", "Allowed Pal soul limits containing Health, Attack, Defense, and CraftSpeed.", "Palソウル上限", "Health、Attack、Defense、CraftSpeedを含む、許可するPalソウル強化の上限です。", defaultJson: "{\"Health\":20,\"Attack\":20,\"Defense\":20,\"CraftSpeed\":20}"),
        Field("IVs", "object", "limits", "IV 上限", "允许的 IV 上限，包含 Health、AttackMelee、AttackShot、Defense。", "IV limits", "Allowed IV limits containing Health, AttackMelee, AttackShot, and Defense.", "IV上限", "Health、AttackMelee、AttackShot、Defenseを含む、許可するIV上限です。", defaultJson: "{\"Health\":100,\"AttackMelee\":100,\"AttackShot\":100,\"Defense\":100}")
    ];
}
