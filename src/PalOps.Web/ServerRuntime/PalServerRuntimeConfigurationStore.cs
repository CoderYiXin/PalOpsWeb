using System.Text.Json;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;

namespace PalOps.Web.ServerRuntime;

public interface IPalServerRuntimeConfigurationStore
{
    Task<PalServerRuntimeConfiguration> GetAsync(CancellationToken cancellationToken = default);
    Task<PalServerRuntimeConfiguration> SaveAsync(PalServerRuntimeConfiguration configuration, CancellationToken cancellationToken = default);
}

public sealed class PalServerRuntimeConfigurationStore : IPalServerRuntimeConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly AppRuntimeOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PalServerRuntimeConfigurationStore(IHostEnvironment environment, IOptions<AppRuntimeOptions> options)
    {
        _options = options.Value;
        var data = Path.IsPathRooted(_options.DataDirectory)
            ? _options.DataDirectory
            : Path.Combine(environment.ContentRootPath, _options.DataDirectory);
        Directory.CreateDirectory(data);
        _path = Path.Combine(data, "palserver-runtime.json");
    }

    public async Task<PalServerRuntimeConfiguration> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return Default();
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, true);
            var file = await JsonSerializer.DeserializeAsync<RuntimeConfigurationFile>(stream, JsonOptions, cancellationToken);
            if (file is null || file.SchemaVersion != 1 || file.Data.SchemaVersion != 1) return Default();
            return file.Data;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Default();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PalServerRuntimeConfiguration> SaveAsync(PalServerRuntimeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        configuration = Validate(configuration);
        await _gate.WaitAsync(cancellationToken);
        string? temporary = null;
        try
        {
            var file = new RuntimeConfigurationFile(1, configuration.UpdatedAt, configuration);
            temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, file, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
            File.Move(temporary, _path, true);
            temporary = null;
            return configuration;
        }
        finally
        {
            if (temporary is not null)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
            _gate.Release();
        }
    }

    private PalServerRuntimeConfiguration Default() => PalServerRuntimeConfiguration.Unconfirmed(
        _options.RuntimeStartupTimeoutSeconds,
        _options.RuntimeShutdownTimeoutSeconds,
        _options.RuntimeSaveWaitSeconds,
        _options.RuntimeRestartCooldownSeconds);

    private static PalServerRuntimeConfiguration Validate(PalServerRuntimeConfiguration value)
    {
        if (!OperatingSystem.IsWindows())
            throw new PalServerRuntimeException(409, "PALSERVER_WINDOWS_REQUIRED", "进程管理仅支持 Windows 本机部署。");
        if (!value.Confirmed)
            throw new PalServerRuntimeException(400, "PALSERVER_CONFIGURATION_NOT_CONFIRMED", "启动配置必须由 Owner 确认。");

        var executable = string.Empty;
        var script = string.Empty;
        string activePath;

        if (value.LaunchMode == PalServerLaunchMode.Script)
        {
            script = FullLocalPath(value.ScriptPath, "启动脚本");
            var extension = Path.GetExtension(script);
            if (!File.Exists(script)
                || (!extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)))
                throw new PalServerRuntimeException(422, "PALSERVER_CONFIGURATION_INVALID", "Script 模式仅支持存在的 .bat 或 .cmd 文件。", script);
            activePath = script;
        }
        else
        {
            executable = FullLocalPath(value.ExecutablePath, "EXE 路径");
            if (!File.Exists(executable)
                || !Path.GetFileName(executable).Equals("PalServer.exe", StringComparison.OrdinalIgnoreCase))
                throw new PalServerRuntimeException(422, "PALSERVER_CONFIGURATION_INVALID", "Executable 模式需要有效的 PalServer.exe。", executable);
            activePath = executable;
        }

        var workingValue = string.IsNullOrWhiteSpace(value.WorkingDirectory)
            ? Path.GetDirectoryName(activePath) ?? string.Empty
            : value.WorkingDirectory;
        var working = FullLocalPath(workingValue, "工作目录");
        if (!Directory.Exists(working))
            throw new PalServerRuntimeException(422, "PALSERVER_CONFIGURATION_INVALID", "工作目录不存在。", working);

        if (value.StartupTimeoutSeconds is < 10 or > 600
            || value.ShutdownTimeoutSeconds is < 10 or > 900
            || value.SaveWaitSeconds is < 1 or > 60
            || value.RestartCooldownSeconds is < 0 or > 120)
            throw new PalServerRuntimeException(422, "PALSERVER_CONFIGURATION_INVALID", "启动、停止、保存等待或重启冷却时间超出允许范围。");

        return value with
        {
            SchemaVersion = 1,
            Confirmed = true,
            WorkingDirectory = working,
            ExecutablePath = executable,
            ScriptPath = script,
            Arguments = value.Arguments?.Trim() ?? string.Empty,
            UpdatedAt = value.UpdatedAt == default ? DateTimeOffset.UtcNow : value.UpdatedAt,
            UpdatedBy = string.IsNullOrWhiteSpace(value.UpdatedBy) ? "unknown" : value.UpdatedBy.Trim()
        };
    }

    private static string FullLocalPath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PalServerRuntimeException(422, "PALSERVER_CONFIGURATION_INVALID", $"{label}不能为空。");
        string full;
        try { full = Path.GetFullPath(value.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PalServerRuntimeException(422, "PALSERVER_CONFIGURATION_INVALID", $"{label}不是有效路径。", value, null, ex);
        }
        if (IsUncPath(full))
            throw new PalServerRuntimeException(422, "PALSERVER_NETWORK_PATH_UNSUPPORTED", $"{label}不能使用网络共享路径。", full);
        return Path.TrimEndingDirectorySeparator(full);
    }

    private static bool IsUncPath(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal));

    private sealed record RuntimeConfigurationFile(int SchemaVersion, DateTimeOffset UpdatedAt, PalServerRuntimeConfiguration Data);
}
