using System.Text.Json;
using System.Text.Json.Nodes;

namespace PalOps.Web.PalDefender.Configuration;

public interface IPalDefenderConfigurationValidator
{
    PalDefenderConfigValidation Validate(string kind, string content);
}

public sealed class PalDefenderConfigurationValidator : IPalDefenderConfigurationValidator
{
    private static readonly JsonSerializerOptions PrettyJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> ConfigBooleanKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "exitServerOnStartupFailure", "preventAdminPasswordInChat", "shouldWarnCheaters", "shouldWarnCheatersReason",
        "shouldKickCheaters", "shouldBanCheaters", "shouldIPBanCheaters", "RCONUsePacketIdFix", "logNetworking",
        "logNetworkingToConsole", "logChat", "logRCON", "logPlayerUID", "logPlayerIP", "logPlayerDeaths",
        "logPlayerLogins", "logPlayerBuildings", "logHelicopterKills", "logPlayerSummons", "logPlayerCaptures",
        "logCraftings", "logTechUnlocks", "logOpenOilrigBoxes", "useAdminWhitelist", "adminAutoLogin",
        "allowAdminCheats", "allowGodmodeOnehit", "isChineseCmd", "announceConnections", "dontAnnounceAdminConnections",
        "announcePunishments", "announcePlayerDeaths", "announceOpenOilrigBoxes", "announceHelicopterKills",
        "announcePlayerSummons", "announceAdminSummons", "announceAdminSummonsKill", "chatBypassWait", "useWhitelist",
        "steamidProtection", "blockTowerBossCapture", "RCONbase64", "disableIllegalItemProtection", "disableButchering",
        "disableRenaming", "disablePalRenaming", "doActionUponIllegalPalStats", "PalImport_Disabled",
        "PalImport_BanIfPalIsImpossible", "PalImport_AllowGenderNone"
    };
    private static readonly HashSet<string> ConfigIntegerKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "OilrigGoalBoxLocktime", "pvpMaxToBuildingDamage", "pvpMaxToPalDamage", "pveMaxToPalBanThreshold",
        "chatMessageMaxLen", "palStatsMaxRank", "PalImport_MaxLevel", "PalImport_MaxRank", "PalImport_MaxSoulHP",
        "PalImport_MaxSoulATK", "PalImport_MaxSoulDEF", "PalImport_MaxSoulCS", "PalImport_MaxIV"
    };
    private static readonly HashSet<string> ConfigNumberKeys = new(StringComparer.OrdinalIgnoreCase) { "RCONTimeout", "treeLimiter" };
    private static readonly HashSet<string> ConfigArrayKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "MOTD", "adminIPs", "bannedIPs", "bannedChatWords", "bannedNames", "adminCheats", "bannedTechnologies",
        "PalImport_BannedPalIDs"
    };
    private static readonly HashSet<string> DeprecatedConfigKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "bannedIPs", "bannedMessage", "PalImport_Disabled", "PalImport_BanIfPalIsImpossible", "PalImport_BannedPalIDs",
        "PalImport_AllowGenderNone", "PalImport_MaxLevel", "PalImport_MaxRank", "PalImport_MaxSoulHP", "PalImport_MaxSoulATK",
        "PalImport_MaxSoulDEF", "PalImport_MaxSoulCS", "PalImport_MaxIV"
    };

    public PalDefenderConfigValidation Validate(string kind, string content)
    {
        var diagnostics = new List<PalDefenderConfigDiagnostic>();
        JsonNode? node;
        try { node = JsonNode.Parse(content, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false }); }
        catch (JsonException exception)
        {
            diagnostics.Add(new("$", "error", $"JSON 无效：{exception.Message}"));
            return new(false, kind, content, ActivationHint(kind), diagnostics);
        }
        if (node is null)
        {
            diagnostics.Add(new("$", "error", "JSON 内容不能为空。"));
            return new(false, kind, content, ActivationHint(kind), diagnostics);
        }

        switch (kind)
        {
            case "config": ValidateConfig(node, diagnostics); break;
            case "whitelist":
            case "banlist": ValidateIdentifierList(node, diagnostics); break;
            case "pal-template": ValidatePalTemplate(node, diagnostics); break;
            case "pal-summon": ValidatePalSummon(node, diagnostics); break;
            case "import-rule": ValidateImportRule(node, diagnostics); break;
            case "rest-config": ValidateRestConfig(node, diagnostics); break;
            case "rest-token": ValidateRestToken(node, diagnostics); break;
            default: diagnostics.Add(new("$", "error", "未知 PalDefender 配置文件类型。")); break;
        }
        var normalized = node.ToJsonString(PrettyJson) + Environment.NewLine;
        return new(diagnostics.All(static item => item.Severity != "error"), kind, normalized, ActivationHint(kind), diagnostics);
    }

    private static void ValidateConfig(JsonNode node, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (node is not JsonObject obj) { Error(diagnostics, "$", "Config.json 必须是 JSON 对象。"); return; }
        foreach (var pair in obj)
        {
            var path = "$." + pair.Key;
            if (ConfigBooleanKeys.Contains(pair.Key)) RequireValueKind(pair.Value, path, JsonValueKind.True, JsonValueKind.False, diagnostics, "布尔值");
            else if (ConfigIntegerKeys.Contains(pair.Key)) RequireInteger(pair.Value, path, diagnostics);
            else if (ConfigNumberKeys.Contains(pair.Key)) RequireNumber(pair.Value, path, diagnostics);
            else if (ConfigArrayKeys.Contains(pair.Key)) RequireStringArray(pair.Value, path, diagnostics);
            else if (pair.Key.Equals("version", StringComparison.OrdinalIgnoreCase) || pair.Key.EndsWith("Message", StringComparison.OrdinalIgnoreCase))
                RequireValueKind(pair.Value, path, JsonValueKind.String, diagnostics, "字符串");
            if (DeprecatedConfigKeys.Contains(pair.Key)) diagnostics.Add(new(path, "warning", "该配置项已被 PalDefender 标记为废弃，建议迁移到新版文件。"));
        }
    }


    private static void ValidateRestConfig(JsonNode node, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (node is not JsonObject obj) { Error(diagnostics, "$", "RESTConfig.json 必须是 JSON 对象。"); return; }
        if (obj["Enabled"] is null) Error(diagnostics, "$.Enabled", "Enabled 为必填布尔值。");
        else RequireValueKind(obj["Enabled"], "$.Enabled", JsonValueKind.True, JsonValueKind.False, diagnostics, "布尔值");
        if (obj["Port"] is null) Error(diagnostics, "$.Port", "Port 为必填端口。");
        else if (!TryInteger(obj["Port"], out var port)) Error(diagnostics, "$.Port", "Port 必须是整数。");
        else if (port is < 1 or > 65535) Error(diagnostics, "$.Port", "Port 必须在 1 到 65535 之间。");
    }

    private static void ValidateRestToken(JsonNode node, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (node is not JsonObject obj) { Error(diagnostics, "$", "REST API 令牌文件必须是 JSON 对象。"); return; }
        RequireNonEmptyString(obj["Name"], "$.Name", diagnostics, "Name 为必填项。");
        RequireNonEmptyString(obj["Token"], "$.Token", diagnostics, "Token 为必填项。");
        var token = ValueString(obj["Token"]);
        if (!string.IsNullOrWhiteSpace(token) && token.Length < 32)
            diagnostics.Add(new("$.Token", "warning", "Token 长度少于 32 个字符，建议重新生成更强的令牌。"));
        if (obj["Permissions"] is null)
        {
            Error(diagnostics, "$.Permissions", "Permissions 为必填项。");
            return;
        }
        if (obj["Permissions"] is JsonValue)
        {
            RequireNonEmptyString(obj["Permissions"], "$.Permissions", diagnostics, "Permissions 不能为空。");
            return;
        }
        if (obj["Permissions"] is JsonArray permissions)
        {
            if (permissions.Count == 0) Error(diagnostics, "$.Permissions", "Permissions 至少需要一个权限。");
            RequireStringArray(permissions, "$.Permissions", diagnostics);
            return;
        }
        Error(diagnostics, "$.Permissions", "Permissions 必须是字符串或字符串数组。");
    }

    private static void ValidateIdentifierList(JsonNode node, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (node is not JsonArray && node is not JsonObject) { Error(diagnostics, "$", "白名单/封禁列表必须是 JSON 数组或对象。"); return; }
        VisitStrings(node, "$", diagnostics);
    }

    private static void ValidatePalTemplate(JsonNode node, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (node is not JsonObject obj) { Error(diagnostics, "$", "PalTemplate 必须是 JSON 对象。"); return; }
        RequireNonEmptyString(obj["PalID"], "$.PalID", diagnostics, "PalID 为必填项。");
        if (obj["Gender"] is not null)
        {
            var gender = ValueString(obj["Gender"]);
            if (gender is not ("Male" or "Female" or "None")) Error(diagnostics, "$.Gender", "Gender 只能是 Male、Female 或 None。");
        }
        foreach (var key in new[] { "UniqueNPCID", "Nickname", "SkinId" })
            if (obj[key] is not null) RequireValueKind(obj[key], "$." + key, JsonValueKind.String, diagnostics, "字符串");
        ValidateEnum(obj, "PhysicalHealth", ["Healthful", "MinorInjury", "Severe", "Dying", "DeadBody", "CloudCemetery"], diagnostics);
        ValidateEnum(obj, "WorkerSick", ["None", "Cold", "Sprain", "Bulimia", "GastricUlcer", "Fracture", "Weakness", "DepressionSprain", "DisturbingElement"], diagnostics);
        foreach (var key in new[] { "Shiny", "ImportedCharacter" })
            if (obj[key] is not null) RequireValueKind(obj[key], "$." + key, JsonValueKind.True, JsonValueKind.False, diagnostics, "布尔值");
        foreach (var key in new[] { "Level", "PartnerSkillLevel" })
            RequireMinimumIntegerWhenPresent(obj, key, 1, diagnostics);
        foreach (var key in new[] { "Exp", "CondensedPals", "UnusedStatusPoints", "FriendshipPoints", "Hunger", "MaxHunger", "SAN", "Support", "CraftSpeed" })
            if (obj[key] is not null) RequireInteger(obj[key], "$." + key, diagnostics);
        foreach (var key in new[] { "HP", "SP", "MP", "Shield" })
            if (obj[key] is not null) RequireNumber(obj[key], "$." + key, diagnostics);
        foreach (var key in new[] { "ActiveSkills", "LearntSkills", "Passives", "DisableWorkPreferences" })
            if (obj[key] is not null) RequireStringArray(obj[key], "$." + key, diagnostics);
        foreach (var key in new[] { "PalSouls", "IVs", "ExtraWorkSuitabilities" })
            if (obj[key] is not null) RequireNumericObject(obj[key], "$." + key, diagnostics);
        if (obj["ActiveSkills"] is JsonArray active && active.Count > 3)
            diagnostics.Add(new("$.ActiveSkills", "warning", "官方文档建议最多放置 3 个当前装备技能，其他技能放入 LearntSkills。"));
        if (obj["Passives"] is JsonArray passives && passives.Count > 4)
            diagnostics.Add(new("$.Passives", "warning", "普通 Pal 通常最多使用 4 个被动词条；请确认导入规则允许更多被动。"));
    }

    private static void ValidatePalSummon(JsonNode node, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (node is not JsonObject obj) { Error(diagnostics, "$", "PalSummon 必须是 JSON 对象。"); return; }
        RequireNonEmptyString(obj["PalTemplate"], "$.PalTemplate", diagnostics, "PalTemplate 为必填项。");
        foreach (var key in new[] { "X", "Y", "Z" })
        {
            if (obj[key] is null) Error(diagnostics, "$." + key, key + " 为必填坐标。");
            else RequireNumber(obj[key], "$." + key, diagnostics);
        }
        if (obj["Uncapturable"] is not null)
            RequireValueKind(obj["Uncapturable"], "$.Uncapturable", JsonValueKind.True, JsonValueKind.False, diagnostics, "布尔值");
        if (obj["DisableStatuses"] is not null)
        {
            RequireStringArray(obj["DisableStatuses"], "$.DisableStatuses", diagnostics);
            if (obj["DisableStatuses"] is JsonArray statuses)
            {
                var allowedStatuses = new HashSet<string>(StringComparer.Ordinal)
                {
                    "DrownCheck", "Poison", "Stun", "Coma", "Sleep", "Overwork", "Drown", "FallDamage",
                    "LavaDamage", "Burn", "Wetness", "Freeze", "Electrical", "Muddy", "IvyCling", "Darkness", "CollectItem"
                };
                for (var index = 0; index < statuses.Count; index++)
                {
                    var status = ValueString(statuses[index]);
                    if (string.IsNullOrWhiteSpace(status))
                        diagnostics.Add(new($"$.DisableStatuses[{index}]", "warning", "空状态名称会被 PalDefender 跳过。"));
                    else if (!allowedStatuses.Contains(status))
                        diagnostics.Add(new($"$.DisableStatuses[{index}]", "warning", $"未知状态 {status} 会被 PalDefender 跳过。"));
                }
            }
        }
    }

    private static void ValidateImportRule(JsonNode node, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (node is not JsonObject obj) { Error(diagnostics, "$", "导入规则必须是 JSON 对象。"); return; }
        ValidateEnum(obj, "PalSelectionMode", ["AllowAllExceptBanned", "AllowOnlyListed"], diagnostics);
        ValidateEnum(obj, "MaxValueLimitAction", ["BlockImport", "ClampToMaxValues"], diagnostics);
        ValidateEnum(obj, "DisallowedPassivesAction", ["BlockImport", "RemoveFromPal"], diagnostics);
        foreach (var key in new[] { "AllowedPalIDs", "BannedPalIDs", "DisallowedPassives" })
            if (obj[key] is not null) RequireStringArray(obj[key], "$." + key, diagnostics);
        foreach (var key in new[] { "Disabled", "BanIfPalIsImpossible", "AllowGenderNone" })
            if (obj[key] is not null) RequireValueKind(obj[key], "$." + key, JsonValueKind.True, JsonValueKind.False, diagnostics, "布尔值");
        foreach (var key in new[] { "MaxLevel", "MaxRank" })
        {
            if (obj[key] is null) continue;
            if (!TryInteger(obj[key], out var value)) Error(diagnostics, "$." + key, "最大值限制必须是整数。");
            else if (value < -1) Error(diagnostics, "$." + key, "最大值限制不能小于 -1。");
        }
        foreach (var key in new[] { "PalSouls", "IVs" })
            if (obj[key] is not null) RequireNumericObject(obj[key], "$." + key, diagnostics, -1);
    }

    private static void VisitStrings(JsonNode? node, string path, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        switch (node)
        {
            case JsonArray array:
                for (var index = 0; index < array.Count; index++) VisitStrings(array[index], $"{path}[{index}]", diagnostics);
                break;
            case JsonObject obj:
                foreach (var pair in obj) VisitStrings(pair.Value, path + "." + pair.Key, diagnostics);
                break;
            case JsonValue value when value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text):
                Error(diagnostics, path, "标识符或地址不能为空字符串。");
                break;
        }
    }

    private static void ValidateEnum(JsonObject obj, string key, string[] allowed, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (obj[key] is null) return;
        var value = ValueString(obj[key]);
        if (value is null || !allowed.Contains(value, StringComparer.Ordinal))
            Error(diagnostics, "$." + key, $"只能是 {string.Join("、", allowed)}。 ");
    }

    private static void RequireNumericObject(JsonNode? node, string path, List<PalDefenderConfigDiagnostic> diagnostics, double? minimum = null)
    {
        if (node is not JsonObject obj) { Error(diagnostics, path, "必须是数值对象。"); return; }
        foreach (var pair in obj)
        {
            var childPath = path + "." + pair.Key;
            if (!TryNumber(pair.Value, out var value)) Error(diagnostics, childPath, "必须是有限数字。");
            else if (minimum.HasValue && value < minimum.Value) Error(diagnostics, childPath, $"不能小于 {minimum.Value}。");
        }
    }

    private static void RequireMinimumIntegerWhenPresent(JsonObject obj, string key, long minimum, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (obj[key] is null) return;
        if (!TryInteger(obj[key], out var value)) Error(diagnostics, "$." + key, key + " 必须是整数。");
        else if (value < minimum) Error(diagnostics, "$." + key, key + $" 不能小于 {minimum}。");
    }

    private static void RequireStringArray(JsonNode? node, string path, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (node is not JsonArray array) { Error(diagnostics, path, "必须是字符串数组。"); return; }
        for (var index = 0; index < array.Count; index++)
            RequireValueKind(array[index], $"{path}[{index}]", JsonValueKind.String, diagnostics, "字符串");
    }

    private static void RequireNonEmptyString(JsonNode? node, string path, List<PalDefenderConfigDiagnostic> diagnostics, string message)
    {
        if (string.IsNullOrWhiteSpace(ValueString(node))) Error(diagnostics, path, message);
    }

    private static void RequireInteger(JsonNode? node, string path, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (!TryInteger(node, out _)) Error(diagnostics, path, "必须是整数。");
    }

    private static bool TryInteger(JsonNode? node, out long value)
    {
        value = 0;
        if (node is not JsonValue jsonValue) return false;
        if (jsonValue.TryGetValue<long>(out value)) return true;
        if (!jsonValue.TryGetValue<double>(out var number) || !double.IsFinite(number) || number < long.MinValue || number > long.MaxValue || Math.Truncate(number) != number)
            return false;
        value = checked((long)number);
        return true;
    }

    private static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue jsonValue || !jsonValue.TryGetValue<double>(out value)) return false;
        return double.IsFinite(value);
    }

    private static void RequireNumber(JsonNode? node, string path, List<PalDefenderConfigDiagnostic> diagnostics)
    {
        if (!TryNumber(node, out _)) Error(diagnostics, path, "必须是有限数字。");
    }

    private static void RequireValueKind(JsonNode? node, string path, JsonValueKind kind, List<PalDefenderConfigDiagnostic> diagnostics, string label)
        => RequireValueKind(node, path, [kind], diagnostics, label);

    private static void RequireValueKind(JsonNode? node, string path, JsonValueKind first, JsonValueKind second, List<PalDefenderConfigDiagnostic> diagnostics, string label)
        => RequireValueKind(node, path, [first, second], diagnostics, label);

    private static void RequireValueKind(JsonNode? node, string path, JsonValueKind[] allowed, List<PalDefenderConfigDiagnostic> diagnostics, string label)
    {
        if (node is null) { Error(diagnostics, path, $"必须是{label}。"); return; }
        using var document = JsonDocument.Parse(node.ToJsonString());
        if (!allowed.Contains(document.RootElement.ValueKind)) Error(diagnostics, path, $"必须是{label}。");
    }

    private static string? ValueString(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static string ActivationHint(string kind) => kind switch
    {
        "rest-config" or "rest-token" => "restart",
        "config" or "import-rule" or "pal-template" or "pal-summon" => "reloadcfg-or-restart",
        _ => "reloadcfg"
    };
    private static void Error(List<PalDefenderConfigDiagnostic> diagnostics, string path, string message) => diagnostics.Add(new(path, "error", message));
}
