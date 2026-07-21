using System.Reflection;
using System.Runtime.InteropServices;

namespace PalOps.Web.Versioning;

public sealed record ApplicationVersionInfo(
    string Application,
    string CurrentVersion,
    string ProductHeaderVersion,
    string Runtime,
    string OperatingSystem,
    string Architecture,
    DateTimeOffset BuildTime);

public interface IApplicationVersionProvider
{
    ApplicationVersionInfo Get();
}

public sealed class ApplicationVersionProvider : IApplicationVersionProvider
{
    private readonly ApplicationVersionInfo _value = Create(typeof(Program).Assembly);

    public ApplicationVersionInfo Get() => _value;

    public static string NormalizeDisplayVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "0.0.0";
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        normalized = normalized.Split('+', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return "0.0.0";

        var suffixIndex = normalized.IndexOf('-');
        var core = suffixIndex >= 0 ? normalized[..suffixIndex] : normalized;
        var suffix = suffixIndex >= 0 ? normalized[suffixIndex..] : string.Empty;
        if (SemanticVersionValue.TryParse(core, out var parsed)) core = parsed.ToString();
        return string.IsNullOrWhiteSpace(core) ? "0.0.0" : core + suffix;
    }

    private static ApplicationVersionInfo Create(Assembly assembly)
    {
        var name = assembly.GetName();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var source = string.IsNullOrWhiteSpace(informational) ? name.Version?.ToString() : informational;
        var displayVersion = NormalizeDisplayVersion(source);
        var productHeaderVersion = new string(displayVersion
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-')
            .ToArray());
        if (string.IsNullOrWhiteSpace(productHeaderVersion)) productHeaderVersion = "0.0.0";

        var buildTime = File.Exists(assembly.Location)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(assembly.Location), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        return new(
            name.Name ?? "PalOps.Web",
            displayVersion,
            productHeaderVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            buildTime);
    }
}
