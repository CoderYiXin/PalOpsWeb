using System.Text.Json;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;

namespace PalOps.Web.Security;

public sealed record AuthState(string PasswordHash, DateTimeOffset UpdatedAt);

public interface IAuthStateStore
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);
    Task<AuthState?> GetAsync(CancellationToken cancellationToken = default);
    Task SetPasswordAsync(string password, CancellationToken cancellationToken = default);
    Task EnsureBootstrapPasswordAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthStateStore : IAuthStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly IPasswordHasher _passwordHasher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AuthStateStore(IHostEnvironment environment, IOptions<AppRuntimeOptions> options, IPasswordHasher passwordHasher)
    {
        var dataDirectory = ResolveDataDirectory(environment.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "auth.json");
        _passwordHasher = passwordHasher;
    }

    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) => Task.FromResult(File.Exists(_path));

    public async Task<AuthState?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<AuthState>(stream, JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        var state = new AuthState(_passwordHasher.Hash(password), DateTimeOffset.UtcNow);
        await WriteAtomicAsync(state, cancellationToken);
    }

    public async Task EnsureBootstrapPasswordAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_path))
        {
            return;
        }

        var bootstrapPassword = Environment.GetEnvironmentVariable("PALOPS_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(bootstrapPassword))
        {
            return;
        }

        await SetPasswordAsync(bootstrapPassword, cancellationToken);
    }

    private async Task WriteAtomicAsync(AuthState state, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = _path + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ResolveDataDirectory(string contentRoot, string configured)
        => Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot, configured);
}
