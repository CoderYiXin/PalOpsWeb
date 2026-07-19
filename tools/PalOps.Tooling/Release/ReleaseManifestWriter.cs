using System.Security.Cryptography;
using PalOps.Tooling.Cli;
using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Release;

public static class ReleaseManifestWriter
{
    public static async Task<int> RunAsync(
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        arguments.EnsureOnly("root", "output");
        var root = Path.GetFullPath(arguments.GetOptionalPath("root") ?? Environment.CurrentDirectory);
        var output = arguments.GetOptionalPath("output") ?? Path.Combine(root, "SHA256SUMS.txt");
        output = Path.GetFullPath(output);
        if (!Directory.Exists(root))
            throw ToolExitException.Usage($"发布目录不存在：{root}");

        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw ToolExitException.Usage("清单输出必须位于发布目录内。");

        var lines = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(path => !string.Equals(path, output, StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            lines.Add($"{Convert.ToHexString(hash).ToLowerInvariant()}  {relative}");
        }

        await File.WriteAllLinesAsync(output, lines, cancellationToken);
        Console.WriteLine($"Release manifest written: {output}");
        return 0;
    }
}
