namespace PalOps.Web.SaveGames;

public interface ISavePathGuard
{
    string NormalizeWorldDirectory(string path);
    string EnsureChildPath(string root, string candidate);
}

public sealed class SavePathGuard : ISavePathGuard
{
    public string NormalizeWorldDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException("存档路径包含无效字符。", nameof(path));

        var fullPath = Path.GetFullPath(path.Trim());
        if (File.Exists(fullPath) && Path.GetFileName(fullPath).Equals("Level.sav", StringComparison.OrdinalIgnoreCase))
            fullPath = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("无法解析 Level.sav 所在目录。");

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string EnsureChildPath(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);

        if (!normalizedCandidate.StartsWith(normalizedRoot, PathComparison))
            throw new InvalidOperationException("目标路径越过允许的存档根目录。");

        return normalizedCandidate;
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
