using System.Text.Json;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;

namespace PalOps.Web.Security;

public static class PalOpsRoles
{
    public const string Owner = "Owner";
    public const string Administrator = "Administrator";
    public const string Operator = "Operator";
    public const string Auditor = "Auditor";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Owner, Administrator, Operator, Auditor, Viewer];
    public static bool IsValid(string? value) => All.Contains(value?.Trim(), StringComparer.OrdinalIgnoreCase);
    public static string Normalize(string value) => All.First(x => x.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record UserAccount(
    string Id,
    string UserName,
    string DisplayName,
    string PasswordHash,
    string Role,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastLoginAt);

public interface IUserAccountStore
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);
    Task EnsureOwnerFromLegacyAsync(CancellationToken cancellationToken = default);
    Task<UserAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);
    Task<UserAccount?> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default);
    Task<UserAccount> CreateOwnerAsync(string passwordHash, CancellationToken cancellationToken = default);
    Task<UserAccount> CreateAsync(string userName, string displayName, string passwordHash, string role, bool enabled, CancellationToken cancellationToken = default);
    Task<UserAccount> UpdateAsync(string id, string displayName, string role, bool enabled, CancellationToken cancellationToken = default);
    Task<UserAccount> SetPasswordAsync(string id, string passwordHash, CancellationToken cancellationToken = default);
    Task RecordLoginAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, string currentUserId, CancellationToken cancellationToken = default);
}

public sealed class JsonUserAccountStore : IUserAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly string _legacyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonUserAccountStore(IHostEnvironment environment, IOptions<AppRuntimeOptions> options)
    {
        var dataDirectory = Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "users.json");
        _legacyPath = Path.Combine(dataDirectory, "auth.json");
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOwnerFromLegacyAsync(cancellationToken);
        return (await LoadAsync(cancellationToken)).Count > 0;
    }

    public async Task EnsureOwnerFromLegacyAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_path) || !File.Exists(_legacyPath)) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_path) || !File.Exists(_legacyPath)) return;
            await using var stream = File.OpenRead(_legacyPath);
            var legacy = await JsonSerializer.DeserializeAsync<AuthState>(stream, JsonOptions, cancellationToken);
            if (legacy is null || string.IsNullOrWhiteSpace(legacy.PasswordHash)) return;
            var now = DateTimeOffset.UtcNow;
            await WriteUnlockedAsync([
                new UserAccount("owner", "admin", "Administrator", legacy.PasswordHash, PalOpsRoles.Owner, true, now, now, null)
            ], cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<UserAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
        => (await LoadAsync(cancellationToken)).FirstOrDefault(x => x.UserName.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase));

    public async Task<UserAccount?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        => (await LoadAsync(cancellationToken)).FirstOrDefault(x => x.Id.Equals(id, StringComparison.Ordinal));

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken = default)
        => (await LoadAsync(cancellationToken)).OrderBy(x => x.Role == PalOpsRoles.Owner ? 0 : 1).ThenBy(x => x.UserName, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<UserAccount> CreateOwnerAsync(string passwordHash, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await LoadUnlockedAsync(cancellationToken);
            var existing = users.FirstOrDefault(x => x.Role == PalOpsRoles.Owner);
            if (existing is not null) return existing;
            var now = DateTimeOffset.UtcNow;
            var account = new UserAccount("owner", "admin", "Administrator", passwordHash, PalOpsRoles.Owner, true, now, now, null);
            users.Add(account);
            await WriteUnlockedAsync(users, cancellationToken);
            return account;
        }
        finally { _gate.Release(); }
    }

    public async Task<UserAccount> CreateAsync(string userName, string displayName, string passwordHash, string role, bool enabled, CancellationToken cancellationToken = default)
    {
        userName = NormalizeUserName(userName);
        if (!PalOpsRoles.IsValid(role) || role.Equals(PalOpsRoles.Owner, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("不能通过此接口创建所有者账户。");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await LoadUnlockedAsync(cancellationToken);
            if (users.Any(x => x.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("用户名已经存在。");
            var now = DateTimeOffset.UtcNow;
            var account = new UserAccount(Guid.NewGuid().ToString("N"), userName, NormalizeDisplayName(displayName, userName), passwordHash, PalOpsRoles.Normalize(role), enabled, now, now, null);
            users.Add(account);
            await WriteUnlockedAsync(users, cancellationToken);
            return account;
        }
        finally { _gate.Release(); }
    }

    public async Task<UserAccount> UpdateAsync(string id, string displayName, string role, bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await LoadUnlockedAsync(cancellationToken);
            var index = users.FindIndex(x => x.Id == id);
            if (index < 0) throw new KeyNotFoundException("用户不存在。");
            var current = users[index];
            if (current.Role == PalOpsRoles.Owner)
            {
                role = PalOpsRoles.Owner;
                enabled = true;
            }
            else
            {
                if (!PalOpsRoles.IsValid(role) || role.Equals(PalOpsRoles.Owner, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("角色无效。");
                role = PalOpsRoles.Normalize(role);
            }
            var updated = current with { DisplayName = NormalizeDisplayName(displayName, current.UserName), Role = role, Enabled = enabled, UpdatedAt = DateTimeOffset.UtcNow };
            users[index] = updated;
            await WriteUnlockedAsync(users, cancellationToken);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public async Task<UserAccount> SetPasswordAsync(string id, string passwordHash, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await LoadUnlockedAsync(cancellationToken);
            var index = users.FindIndex(x => x.Id == id);
            if (index < 0) throw new KeyNotFoundException("用户不存在。");
            var updated = users[index] with { PasswordHash = passwordHash, UpdatedAt = DateTimeOffset.UtcNow };
            users[index] = updated;
            await WriteUnlockedAsync(users, cancellationToken);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public async Task RecordLoginAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await LoadUnlockedAsync(cancellationToken);
            var index = users.FindIndex(x => x.Id == id);
            if (index < 0) return;
            users[index] = users[index] with { LastLoginAt = DateTimeOffset.UtcNow };
            await WriteUnlockedAsync(users, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string id, string currentUserId, CancellationToken cancellationToken = default)
    {
        if (id == currentUserId) throw new InvalidOperationException("不能删除当前登录账户。");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await LoadUnlockedAsync(cancellationToken);
            var account = users.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("用户不存在。");
            if (account.Role == PalOpsRoles.Owner) throw new InvalidOperationException("不能删除所有者账户。");
            users.Remove(account);
            await WriteUnlockedAsync(users, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<UserAccount>> LoadAsync(CancellationToken cancellationToken)
    {
        await EnsureOwnerFromLegacyAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try { return await LoadUnlockedAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<List<UserAccount>> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<UserAccount>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteUnlockedAsync(IReadOnlyList<UserAccount> users, CancellationToken cancellationToken)
    {
        var temporaryPath = _path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, users, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, _path, true);
    }

    private static string NormalizeUserName(string value)
    {
        value = value.Trim();
        if (value.Length is < 3 or > 64 || value.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.'))) throw new ArgumentException("用户名必须为 3-64 个字母、数字、点、下划线或连字符。");
        return value;
    }

    private static string NormalizeDisplayName(string value, string fallback)
    {
        value = value.Trim();
        if (value.Length > 100) throw new ArgumentException("显示名称不能超过 100 个字符。");
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
