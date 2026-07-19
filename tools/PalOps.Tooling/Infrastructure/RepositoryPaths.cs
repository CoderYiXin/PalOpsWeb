namespace PalOps.Tooling.Infrastructure;

public sealed record RepositoryPaths(
    string Root,
    string MapSourcesPath,
    string FrontendTilesPath)
{
    public static RepositoryPaths Resolve(string? requestedRoot)
    {
        var root = requestedRoot is null
            ? Discover(Environment.CurrentDirectory)
            : Path.GetFullPath(requestedRoot);

        if (!Directory.Exists(root))
            throw ToolExitException.Usage($"仓库根目录不存在：{root}");
        if (!File.Exists(Path.Combine(root, "PalOpsWeb.slnx")) ||
            !Directory.Exists(Path.Combine(root, "src", "PalOps.Web")))
            throw ToolExitException.Usage($"目录不是 PalOps Web 仓库根目录：{root}");

        return new RepositoryPaths(
            root,
            Path.Combine(root, "scripts", "map-sources.json"),
            Path.Combine(root, "frontend-vue", "public", "map", "tiles"));
    }

    private static string Discover(string start)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(start));
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PalOpsWeb.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "PalOps.Web")))
                return directory.FullName;
        }

        throw ToolExitException.Usage("无法自动发现 PalOps Web 仓库根目录，请使用 --root 指定。");
    }
}
