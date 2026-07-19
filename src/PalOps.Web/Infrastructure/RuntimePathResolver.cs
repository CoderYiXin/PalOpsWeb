using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;

namespace PalOps.Web.Infrastructure;

public interface IRuntimePathResolver
{
    string DataDirectory { get; }
    string ResolveDataPath(params string[] segments);
    string ResolveConfiguredDirectory(string configured, string fallbackSubdirectory);
}

public sealed class RuntimePathResolver : IRuntimePathResolver
{
    public RuntimePathResolver(IHostEnvironment environment, IOptions<AppRuntimeOptions> options)
    {
        DataDirectory = Path.GetFullPath(Path.IsPathRooted(options.Value.DataDirectory)
            ? options.Value.DataDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.DataDirectory));
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }

    public string ResolveDataPath(params string[] segments)
    {
        var path = segments.Aggregate(DataDirectory, Path.Combine);
        return Path.GetFullPath(path);
    }

    public string ResolveConfiguredDirectory(string configured, string fallbackSubdirectory)
    {
        var path = string.IsNullOrWhiteSpace(configured)
            ? ResolveDataPath(fallbackSubdirectory)
            : Path.GetFullPath(configured.Trim());
        Directory.CreateDirectory(path);
        return path;
    }
}
