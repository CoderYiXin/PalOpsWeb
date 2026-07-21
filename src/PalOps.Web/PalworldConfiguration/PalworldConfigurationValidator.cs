using System.Globalization;
using System.Net;

namespace PalOps.Web.PalworldConfiguration;

public sealed class PalworldConfigurationValidator(PalworldConfigurationMetadata metadata)
{
    private const int DefaultGameListenerPort = 8211;
    public PalworldConfigurationValidationResult Validate(PalworldSettingsDocument document, string launchArguments, bool worldOptionExists)
    {
        var diagnostics = new List<PalworldConfigurationDiagnostic>();
        var settings = document.ToDictionary();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.Entries)
        {
            if (!seen.Add(entry.Key))
                diagnostics.Add(new("DUPLICATE_SETTING", PalworldConfigurationSeverity.Error, entry.Key, $"配置项 {entry.Key} 重复。"));
            var field = metadata.ResolveField(entry.Key, entry.Value);
            ValidateField(field, entry.Value, diagnostics);
        }

        var launch = PalworldLaunchArgumentParser.Parse(launchArguments);
        diagnostics.AddRange(launch.Diagnostics);
        ValidateLaunchValues(launch, diagnostics);
        ValidatePortConflicts(settings, launch.Values, diagnostics);
        ValidateOverrides(settings, launch.Values, diagnostics);

        if (TryNumber(settings, "PalSpawnNumRate", out var spawnRate) && spawnRate > 3)
            diagnostics.Add(new("PERFORMANCE_RISK", PalworldConfigurationSeverity.Warning, "PalSpawnNumRate", "帕鲁生成倍率高于 3，可能显著增加 CPU、内存和存档压力。"));
        if (TryNumber(settings, "BaseCampMaxNum", out var baseCount) && baseCount > 256)
            diagnostics.Add(new("PERFORMANCE_RISK", PalworldConfigurationSeverity.Warning, "BaseCampMaxNum", "全服据点参考上限高于 256，可能造成服务器负载和同步压力。"));
        if (TryNumber(settings, "DropItemMaxNum", out var dropCount) && dropCount > 5000)
            diagnostics.Add(new("PERFORMANCE_RISK", PalworldConfigurationSeverity.Warning, "DropItemMaxNum", "掉落物上限高于 5000，可能增加世界实体和存档压力。"));
        if (worldOptionExists)
            diagnostics.Add(new("WORLD_OPTION_DETECTED", PalworldConfigurationSeverity.Warning, "WorldOption.sav", "检测到 WorldOption.sav。游戏可能优先使用世界选项；本模块仅只读检测，不会修改二进制存档。"));
        if (!settings.ContainsKey("ServerName"))
            diagnostics.Add(new("RECOMMENDED_SETTING_MISSING", PalworldConfigurationSeverity.Information, "ServerName", "建议配置服务器名称。"));

        var normalized = new PalworldSettingsIniCodec().Serialize(document);
        return new(
            !diagnostics.Any(static item => item.Severity == PalworldConfigurationSeverity.Error),
            normalized,
            settings,
            diagnostics);
    }

    private static void ValidateField(PalworldConfigurationFieldMetadata field, string raw, List<PalworldConfigurationDiagnostic> diagnostics)
    {
        var path = field.Key;
        switch (field.ValueType)
        {
            case "string":
            case "password":
                if (raw.Length < 2 || raw[0] != '"' || raw[^1] != '"')
                    AddError("STRING_QUOTE_REQUIRED", path, $"{field.ChineseName} 必须使用双引号。", diagnostics);
                else if (PalworldSettingsIniCodec.Unquote(raw).Length > 1024)
                    AddError("STRING_TOO_LONG", path, $"{field.ChineseName} 超过 1024 字符。", diagnostics);
                if (field.Key.Equals("PublicIP", StringComparison.OrdinalIgnoreCase))
                {
                    var ip = PalworldSettingsIniCodec.Unquote(raw);
                    if (!string.IsNullOrWhiteSpace(ip) && !IPAddress.TryParse(ip, out _))
                        AddError("INVALID_IP", path, "公网 IP 格式无效。", diagnostics);
                }
                break;
            case "boolean":
                if (!raw.Equals("True", StringComparison.OrdinalIgnoreCase) && !raw.Equals("False", StringComparison.OrdinalIgnoreCase))
                    AddError("INVALID_BOOLEAN", path, $"{field.ChineseName} 必须为 True 或 False。", diagnostics);
                break;
            case "integer":
                if (!TryParseIntegerValue(raw, out var integer))
                    AddError("INVALID_INTEGER", path, $"{field.ChineseName} 必须为整数。允许使用 72 或 72.000000 这类等价格式。", diagnostics);
                else if (field.EnforceRange) ValidateRange(field, integer, diagnostics);
                break;
            case "number":
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                    AddError("INVALID_NUMBER", path, $"{field.ChineseName} 必须为有效数字。", diagnostics);
                else if (field.EnforceRange) ValidateRange(field, number, diagnostics);
                break;
            case "enum":
                if (field.Options is not null && !field.Options.Contains(PalworldSettingsIniCodec.Unquote(raw), StringComparer.OrdinalIgnoreCase))
                    AddError("INVALID_ENUM", path, $"{field.ChineseName} 只允许：{string.Join("、", field.Options)}。", diagnostics);
                break;
            case "list":
                ValidateList(field, raw, diagnostics);
                break;
        }
    }


    private static bool TryParseIntegerValue(string raw, out long value)
    {
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue)
            && decimalValue == decimal.Truncate(decimalValue)
            && decimalValue >= long.MinValue
            && decimalValue <= long.MaxValue)
        {
            value = decimal.ToInt64(decimalValue);
            return true;
        }
        value = 0;
        return false;
    }

    private static void ValidateList(PalworldConfigurationFieldMetadata field, string raw, List<PalworldConfigurationDiagnostic> diagnostics)
    {
        if (raw.Length < 2 || raw[0] != '(' || raw[^1] != ')')
        {
            AddError("INVALID_LIST", field.Key, $"{field.ChineseName} 必须使用括号列表。", diagnostics);
            return;
        }
        var values = raw[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (field.Options is null) return;
        foreach (var value in values)
        {
            if (!field.Options.Contains(value.Trim('"'), StringComparer.OrdinalIgnoreCase))
                AddError("INVALID_LIST_VALUE", field.Key, $"{field.ChineseName} 包含不支持的值 {value}。", diagnostics);
        }
    }

    private static void ValidateRange(PalworldConfigurationFieldMetadata field, double value, List<PalworldConfigurationDiagnostic> diagnostics)
    {
        if ((field.Minimum.HasValue && value < field.Minimum.Value) || (field.Maximum.HasValue && value > field.Maximum.Value))
            AddError("OUT_OF_RANGE", field.Key, $"{field.ChineseName} 必须在 {field.Minimum?.ToString(CultureInfo.InvariantCulture)} 到 {field.Maximum?.ToString(CultureInfo.InvariantCulture)} 之间。", diagnostics);
    }

    private static void ValidateLaunchValues(PalworldLaunchArgumentParseResult launch, List<PalworldConfigurationDiagnostic> diagnostics)
    {
        foreach (var key in new[] { "port", "publicport" })
        {
            if (!launch.Values.TryGetValue(key, out var raw)) continue;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
                AddError("OUT_OF_RANGE", $"launchArguments.{key}", $"启动参数 -{key} 必须为 1 到 65535。", diagnostics);
        }
        if (launch.Values.TryGetValue("players", out var playersRaw)
            && (!int.TryParse(playersRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var players) || players is < 1 or > 512))
            AddError("OUT_OF_RANGE", "launchArguments.players", "启动参数 -players 必须为 1 到 512。", diagnostics);
        if (launch.Values.TryGetValue("numberofworkerthreadsserver", out var workerThreadsRaw)
            && (!int.TryParse(workerThreadsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var workerThreads) || workerThreads is < 1 or > 256))
            AddError("OUT_OF_RANGE", "launchArguments.numberofworkerthreadsserver", "启动参数 -NumberOfWorkerThreadsServer 必须为 1 到 256。", diagnostics);
        if (launch.Values.TryGetValue("publicip", out var publicIp) && !IPAddress.TryParse(publicIp, out _))
            AddError("INVALID_IP", "launchArguments.publicip", "启动参数 -publicip 格式无效。", diagnostics);
        if (launch.Values.TryGetValue("logformat", out var logFormat)
            && !new[] { "text", "json" }.Contains(logFormat, StringComparer.OrdinalIgnoreCase))
            AddError("INVALID_ENUM", "launchArguments.logformat", "启动参数 -logformat 只允许 text 或 json。", diagnostics);
        if (launch.Flags.Any(static value => value is "useperfthreads" or "noasyncloadingthread" or "usemultithreadfords")
            || launch.Values.ContainsKey("numberofworkerthreadsserver"))
            diagnostics.Add(new("LEGACY_ARGUMENT", PalworldConfigurationSeverity.Warning, "launchArguments", "Palworld 1.0 及以后不设置旧性能线程参数可能获得更好性能；请仅在压测确认后启用。"));
        if (launch.Values.ContainsKey("numberofworkerthreadsserver")
            && !(launch.Flags.Contains("useperfthreads") && launch.Flags.Contains("noasyncloadingthread") && launch.Flags.Contains("usemultithreadfords")))
            diagnostics.Add(new("WORKER_THREADS_REQUIRES_PERFORMANCE_FLAGS", PalworldConfigurationSeverity.Warning, "launchArguments.numberofworkerthreadsserver", "-NumberOfWorkerThreadsServer 应与 -UsePerfThreads、-NoAsyncLoadingThread、-UseMultithreadForDS 组合使用。"));
    }

    private static void ValidatePortConflicts(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> launch, List<PalworldConfigurationDiagnostic> diagnostics)
    {
        var listeners = new List<(string Path, int Port)>();

        // Palworld's INI PublicPort is advertised-only. The actual game listener is
        // controlled by -port and otherwise remains on the official default 8211.
        if (launch.ContainsKey("port")) AddPort(launch, "port", listeners, "launchArguments.");
        else listeners.Add(("launchArguments.port(default)", DefaultGameListenerPort));

        // Disabled services do not bind their configured ports and therefore cannot
        // collide with the game listener or each other.
        if (IsEnabled(settings, "RCONEnabled")) AddPort(settings, "RCONPort", listeners);
        if (IsEnabled(settings, "RESTAPIEnabled")) AddPort(settings, "RESTAPIPort", listeners);

        foreach (var group in listeners.GroupBy(static item => item.Port).Where(static group => group.Count() > 1))
        {
            diagnostics.Add(new("PORT_CONFLICT", PalworldConfigurationSeverity.Error, string.Join(",", group.Select(static item => item.Path)), $"端口 {group.Key} 被多个已启用监听项重复使用：{string.Join("、", group.Select(static item => item.Path))}。"));
        }
    }

    private static void ValidateOverrides(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> launch, List<PalworldConfigurationDiagnostic> diagnostics)
    {
        if (!launch.ContainsKey("port") && settings.TryGetValue("PublicPort", out var advertisedPort)
            && !PalworldSettingsIniCodec.Unquote(advertisedPort).Equals(DefaultGameListenerPort.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            diagnostics.Add(new("PUBLIC_PORT_IS_ADVERTISED_ONLY", PalworldConfigurationSeverity.Information, "PublicPort", $"PublicPort 仅用于对外公布；未设置 -port 时，PalServer 仍监听默认端口 {DefaultGameListenerPort}。"));
        if (launch.ContainsKey("players") && settings.ContainsKey("ServerPlayerMaxNum"))
            diagnostics.Add(new("LAUNCH_ARGUMENT_OVERRIDE", PalworldConfigurationSeverity.Information, "ServerPlayerMaxNum", "启动参数 -players 会覆盖或影响 INI 中的最大玩家数，请保持一致。"));
        if (launch.ContainsKey("publicip") && settings.ContainsKey("PublicIP"))
            diagnostics.Add(new("LAUNCH_ARGUMENT_OVERRIDE", PalworldConfigurationSeverity.Information, "PublicIP", "启动参数 -publicip 会覆盖或影响 INI 中的 PublicIP，请保持一致。"));
        if (launch.ContainsKey("publicport") && settings.ContainsKey("PublicPort"))
            diagnostics.Add(new("LAUNCH_ARGUMENT_OVERRIDE", PalworldConfigurationSeverity.Information, "PublicPort", "启动参数 -publicport 与 INI PublicPort 都用于对外公布端口，请保持一致。"));
    }

    private static bool IsEnabled(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var raw)
        && raw.Equals("True", StringComparison.OrdinalIgnoreCase);

    private static void AddPort(IReadOnlyDictionary<string, string> values, string key, List<(string Path, int Port)> ports, string prefix = "")
    {
        if (values.TryGetValue(key, out var raw) && int.TryParse(PalworldSettingsIniCodec.Unquote(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535)
            ports.Add((prefix + key, port));
    }

    private static bool TryNumber(IReadOnlyDictionary<string, string> settings, string key, out double value)
    {
        value = 0;
        return settings.TryGetValue(key, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void AddError(string code, string path, string message, List<PalworldConfigurationDiagnostic> diagnostics) =>
        diagnostics.Add(new(code, PalworldConfigurationSeverity.Error, path, message));
}
