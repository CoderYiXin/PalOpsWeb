using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PalOps.Web.PalDefender.Configuration;

public interface IPalDefenderConfigurationService
{
    Task<IReadOnlyList<PalDefenderConfigFileSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<PalDefenderConfigFileContent> ReadAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<PalDefenderConfigValidation> ValidateAsync(string relativePath, string content, CancellationToken cancellationToken = default);
    Task<PalDefenderGeneratedConfig> GenerateAsync(string kind, string? name, CancellationToken cancellationToken = default);
    Task<PalDefenderConfigFileContent> SaveAsync(string relativePath, string content, string? expectedSha256, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativePath, string? expectedSha256, CancellationToken cancellationToken = default);
}

public sealed class PalDefenderConfigurationService(
    IPalDefenderConfigurationPathResolver pathResolver,
    IPalDefenderConfigurationValidator validator) : IPalDefenderConfigurationService
{
    private const int MaximumFileBytes = 2 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<PalDefenderConfigFileSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var root = await pathResolver.GetRootAsync(cancellationToken);
        var results = new List<PalDefenderConfigFileSummary>();
        foreach (var rootFile in new[] { "Config.json", "WhiteList.json", "Banlist.json", "RESTAPI/RESTConfig.json" })
        {
            var resolved = await pathResolver.ResolveAsync(rootFile, cancellationToken);
            if (File.Exists(resolved.FullPath)) results.Add(await SummaryAsync(resolved, cancellationToken));
        }
        foreach (var directory in PalDefenderConfigurationPathResolver.ManagedDirectoryNames)
        {
            var fullDirectory = Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullDirectory)) continue;
            foreach (var file in Directory.EnumerateFiles(fullDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (directory.Equals("RESTAPI/Tokens", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(file).Equals("TokenExample.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                var relative = directory + "/" + Path.GetFileName(file);
                var resolved = await pathResolver.ResolveAsync(relative, cancellationToken);
                results.Add(await SummaryAsync(resolved, cancellationToken));
            }
        }
        return results.OrderBy(static item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<PalDefenderConfigFileContent> ReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var resolved = await pathResolver.ResolveAsync(relativePath, cancellationToken);
        var info = new FileInfo(resolved.FullPath);
        if (!info.Exists) throw new FileNotFoundException("PalDefender 配置文件不存在。", resolved.FullPath);
        if (info.Length > MaximumFileBytes) throw new InvalidDataException("PalDefender 配置文件超过 2 MiB 限制。");
        var content = await File.ReadAllTextAsync(resolved.FullPath, Encoding.UTF8, cancellationToken);
        return new PalDefenderConfigFileContent(await SummaryAsync(resolved, cancellationToken), content);
    }

    public async Task<PalDefenderConfigValidation> ValidateAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var resolved = await pathResolver.ResolveAsync(relativePath, cancellationToken);
        ValidateSize(content);
        return validator.Validate(resolved.Kind, content);
    }

    public Task<PalDefenderGeneratedConfig> GenerateAsync(string kind, string? name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        var safeName = SafeFileName(name);
        var (relativePath, value) = normalizedKind switch
        {
            "config" => ("Config.json", (object)new Dictionary<string, object?>
            {
                ["version"] = "1.0.0",
                ["MOTD"] = Array.Empty<string>(),
                ["shouldWarnCheaters"] = true,
                ["shouldWarnCheatersReason"] = true,
                ["shouldKickCheaters"] = true,
                ["shouldBanCheaters"] = true,
                ["shouldIPBanCheaters"] = false,
                ["logChat"] = true,
                ["logRCON"] = true,
                ["useAdminWhitelist"] = false,
                ["adminIPs"] = Array.Empty<string>(),
                ["useWhitelist"] = false,
                ["disableIllegalItemProtection"] = false
            }),
            "whitelist" => ("WhiteList.json", (object)Array.Empty<string>()),
            "banlist" => ("Banlist.json", (object)Array.Empty<string>()),
            "rest-config" => ("RESTAPI/RESTConfig.json", (object)new Dictionary<string, object?>
            {
                ["Enabled"] = false,
                ["Port"] = 17993
            }),
            "rest-token" => ($"RESTAPI/Tokens/{safeName ?? DefaultTokenName()}.json", (object)new Dictionary<string, object?>
            {
                ["Name"] = string.IsNullOrWhiteSpace(name) ? "PalOps" : name.Trim(),
                ["Token"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
                ["Permissions"] = new[] { "REST.*" }
            }),
            "import-rule" => ($"Pals/ImportRules/{safeName ?? "CustomRule"}.json", (object)new Dictionary<string, object?>
            {
                ["PalSelectionMode"] = "AllowAllExceptBanned",
                ["AllowedPalIDs"] = Array.Empty<string>(),
                ["BannedPalIDs"] = Array.Empty<string>(),
                ["MaxValueLimitAction"] = "BlockImport",
                ["DisallowedPassivesAction"] = "BlockImport",
                ["DisallowedPassives"] = Array.Empty<string>(),
                ["Disabled"] = false,
                ["BanIfPalIsImpossible"] = false,
                ["AllowGenderNone"] = false,
                ["MaxLevel"] = 65,
                ["MaxRank"] = 5,
                ["PalSouls"] = new Dictionary<string, int>
                {
                    ["Health"] = 20,
                    ["Attack"] = 20,
                    ["Defense"] = 20,
                    ["CraftSpeed"] = 20
                },
                ["IVs"] = new Dictionary<string, int>
                {
                    ["Health"] = 100,
                    ["AttackMelee"] = 100,
                    ["AttackShot"] = 100,
                    ["Defense"] = 100
                }
            }),
            "pal-template" => ($"Pals/Templates/{safeName ?? "CustomPal"}.json", (object)new Dictionary<string, object?>
            {
                ["PalID"] = "",
                ["Gender"] = "None",
                ["Level"] = 1,
                ["ActiveSkills"] = Array.Empty<string>(),
                ["LearntSkills"] = Array.Empty<string>(),
                ["Passives"] = Array.Empty<string>()
            }),
            "pal-summon" => ($"Pals/Summons/{safeName ?? "CustomSummon"}.json", (object)new Dictionary<string, object?>
            {
                ["PalTemplate"] = "",
                ["X"] = 0,
                ["Y"] = 0,
                ["Z"] = 0,
                ["Uncapturable"] = false,
                ["DisableStatuses"] = Array.Empty<string>()
            }),
            _ => throw new ArgumentException("不支持的 PalDefender 配置类型。", nameof(kind))
        };

        // Resolve the generated path before returning it so generation obeys
        // exactly the same path allowlist used by read/write/delete operations.
        _ = PalDefenderConfigurationPathResolver.Describe(relativePath);
        var content = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine;
        return Task.FromResult(new PalDefenderGeneratedConfig(relativePath, normalizedKind, content));
    }

    public async Task<PalDefenderConfigFileContent> SaveAsync(string relativePath, string content, string? expectedSha256, CancellationToken cancellationToken = default)
    {
        var resolved = await pathResolver.ResolveAsync(relativePath, cancellationToken);
        ValidateSize(content);
        var validation = validator.Validate(resolved.Kind, content);
        if (!validation.Valid) throw new InvalidDataException("PalDefender 配置校验失败：" + string.Join("；", validation.Diagnostics.Where(static item => item.Severity == "error").Select(static item => $"{item.Path} {item.Message}")));

        await _gate.WaitAsync(cancellationToken);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolved.FullPath)!);
            if (File.Exists(resolved.FullPath))
            {
                var currentHash = await HashAsync(resolved.FullPath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(expectedSha256) && !currentHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("配置文件已被其他进程修改，请重新加载后再保存。");
                await BackupAsync(resolved, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                throw new InvalidOperationException("配置文件已不存在，请刷新列表。");
            }

            temporary = Path.Combine(Path.GetDirectoryName(resolved.FullPath)!, $".{Path.GetFileName(resolved.FullPath)}.palops-tmp-{Guid.NewGuid():N}");
            var bytes = new UTF8Encoding(false).GetBytes(validation.NormalizedContent);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, resolved.FullPath, overwrite: true);
            temporary = null;
            return await ReadAsync(resolved.RelativePath, cancellationToken);
        }
        finally
        {
            if (temporary is not null)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string relativePath, string? expectedSha256, CancellationToken cancellationToken = default)
    {
        var resolved = await pathResolver.ResolveAsync(relativePath, cancellationToken);
        if (!resolved.CanDelete) throw new InvalidOperationException("核心 PalDefender 配置文件不能删除。");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(resolved.FullPath)) return;
            var currentHash = await HashAsync(resolved.FullPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(expectedSha256) && !currentHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("配置文件已被其他进程修改，请重新加载后再删除。");
            await BackupAsync(resolved, cancellationToken);
            File.Delete(resolved.FullPath);
        }
        finally { _gate.Release(); }
    }

    private static string DefaultTokenName() => $"PalOps-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";

    private static string? SafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        var builder = new StringBuilder(trimmed.Length);
        foreach (var character in trimmed)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.') builder.Append(character);
            else if (char.IsWhiteSpace(character)) builder.Append('-');
        }
        var result = builder.ToString();
        if (result.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("配置文件名称不能包含连续句点。", nameof(value));
        result = result.Trim('.', '-', '_');
        if (result.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            result = result[..^5].TrimEnd('.', '-', '_');
        if (result.Length is < 1 or > 80) throw new ArgumentException("配置文件名称必须为 1-80 个安全字符。", nameof(value));
        if (result.Equals("TokenExample", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("TokenExample 是 PalDefender 保留的示例令牌名称。", nameof(value));
        return result;
    }

    private static void ValidateSize(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("PalDefender 配置内容不能为空。");
        if (Encoding.UTF8.GetByteCount(content) > MaximumFileBytes) throw new InvalidDataException("PalDefender 配置内容超过 2 MiB 限制。");
    }

    private static async Task<PalDefenderConfigFileSummary> SummaryAsync(PalDefenderResolvedPath resolved, CancellationToken cancellationToken)
    {
        var info = new FileInfo(resolved.FullPath);
        return new PalDefenderConfigFileSummary(
            resolved.RelativePath,
            info.Name,
            resolved.Kind,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            await HashAsync(resolved.FullPath, cancellationToken),
            resolved.CanDelete,
            resolved.ActivationHint);
    }

    private static async Task BackupAsync(PalDefenderResolvedPath resolved, CancellationToken cancellationToken)
    {
        var backupDirectory = Path.Combine(resolved.Root, ".palops-backups", resolved.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(backupDirectory);
        var backup = Path.Combine(backupDirectory, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".json");
        await using var source = new FileStream(resolved.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var target = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(target, cancellationToken);
        await target.FlushAsync(cancellationToken);
        target.Flush(flushToDisk: true);
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
