using System.Text;
using System.Text.RegularExpressions;
using PalOps.Tooling.Cli;
using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Release;

public static partial class ReleaseVerifier
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "node_modules", "bin", "obj", "dist",
        "artifacts", "__pycache__", ".pytest_cache", ".mypy_cache", ".npm-cache"
    };

    private static readonly HashSet<string> BlockedSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sav", ".sqlite", ".sqlite3", ".db", ".pdb", ".user", ".suo", ".py", ".pyc"
    };

    private static readonly HashSet<string> TextSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".slnx", ".ts", ".vue", ".js", ".mjs", ".json", ".md", ".txt",
        ".yml", ".yaml", ".xml", ".ps1", ".cmd", ".ini", ".csv", ".html", ".css"
    };

    private static readonly string[][] BlockedRuntimeParts =
    [
        ["src", "PalOps.Web", "data"]
    ];

    private static readonly string[] RequiredPublishedFiles =
    [
        "PalOps.Web.exe",
        "start.cmd",
        "README.md",
        "README.en.md",
        "CHANGELOG.md",
        "SECURITY.md",
        "THIRD-PARTY-NOTICES.md",
        Path.Combine("docs", "README.md"),
        Path.Combine("docs", "README.en.md"),
        Path.Combine("docs", "architecture.md"),
        Path.Combine("docs", "architecture.en.md"),
        Path.Combine("docs", "build.md"),
        Path.Combine("docs", "build.en.md"),
        Path.Combine("docs", "deployment.md"),
        Path.Combine("docs", "deployment.en.md"),
        Path.Combine("docs", "paldefender-deployment.md"),
        Path.Combine("docs", "paldefender-deployment.en.md"),
        "SHA256SUMS.txt"
    ];

    private static readonly string[] SafeValueMarkers =
    [
        "replace", "example", "sample", "placeholder", "redacted", "secret-value",
        "替换", "示例", "占位", "与服务端配置一致", "同上", "请填写", "<", "${",
        "initial password", "reset password", "password reset", "new password",
        "enter the administrator password again", "current password",
        "初期パスワード", "パスワードリセット", "新しいパスワード",
        "管理パスワードをもう一度入力", "現在のパスワード",
        "初始密码", "重置密码", "新密码", "再次输入管理密码", "当前管理密码"
    ];

    public static async Task<int> RunAsync(
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        arguments.EnsureOnly("root", "strict-tree");
        var root = arguments.GetOptionalPath("root") ?? Environment.CurrentDirectory;
        var strictTree = arguments.HasFlag("strict-tree");
        if (!Directory.Exists(root))
            throw ToolExitException.Usage($"发布校验目录不存在：{root}");

        var problems = await VerifyAsync(Path.GetFullPath(root), strictTree, cancellationToken);
        if (problems.Count > 0)
            throw ToolExitException.Verification(
                "Release verification failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(problem => "- " + problem)));

        Console.WriteLine("Release verification passed.");
        return 0;
    }

    public static async Task<IReadOnlyList<string>> VerifyAsync(
        string root,
        bool strictTree,
        CancellationToken cancellationToken)
    {
        root = Path.GetFullPath(root);
        var problems = new List<string>();
        if (File.Exists(Path.Combine(root, "PalOpsWeb.slnx")))
        {
            var documentationProblems = await DocumentationVerifier.VerifyAsync(root, cancellationToken);
            problems.AddRange(documentationProblems);
        }

        if (strictTree)
        {
            foreach (var relative in RequiredPublishedFiles)
            {
                if (!File.Exists(Path.Combine(root, relative)))
                    problems.Add($"required published file missing: {relative}");
            }
        }

        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, entry);
                var parts = SplitParts(relative);

                if (Directory.Exists(entry))
                {
                    var name = Path.GetFileName(entry);
                    if (IgnoredDirectoryNames.Contains(name))
                    {
                        if (strictTree)
                            problems.Add($"blocked directory: {relative}");
                        continue;
                    }

                    if (BlockedRuntimeParts.Any(sequence => ContainsPartSequence(parts, sequence)))
                    {
                        var unsafeFile = Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories)
                            .FirstOrDefault(file => !string.Equals(Path.GetFileName(file), ".gitkeep", StringComparison.Ordinal));
                        if (unsafeFile is not null)
                            problems.Add($"runtime/private directory contains files: {relative}");
                        continue;
                    }

                    directories.Push(entry);
                    continue;
                }

                var suffix = Path.GetExtension(entry);
                if (BlockedSuffixes.Contains(suffix))
                {
                    problems.Add($"blocked file type: {relative}");
                    continue;
                }
                if (!TextSuffixes.Contains(suffix))
                    continue;

                var info = new FileInfo(entry);
                if (info.Length > 8L * 1024 * 1024)
                    continue;

                string text;
                try
                {
                    text = await ReadUtf8TextAsync(entry, cancellationToken);
                }
                catch (DecoderFallbackException)
                {
                    continue;
                }

                var authorization = AuthorizationPattern().Match(text);
                if (authorization.Success)
                {
                    var sample = authorization.Value[..Math.Min(48, authorization.Value.Length)];
                    problems.Add($"possible Authorization credential: {relative}: {sample}");
                }

                // The repository has no Python build or maintenance dependency.
                // Markdown examples remain documentation rather than an invocation surface.
                if (!IsMarkdownFile(suffix) && !IsSuperpowersDocumentation(parts))
                {
                    var invocation = PythonInvocationPattern().Match(text);
                    if (invocation.Success)
                        problems.Add($"active Python invocation: {relative}: {invocation.Value.Trim()}");
                    if (SetupPythonPattern().IsMatch(text))
                        problems.Add($"Python CI dependency: {relative}: setup-" + "python");
                }

                if (IsLocaleFile(parts))
                    continue;

                foreach (Match assignment in AssignmentPattern().Matches(text))
                {
                    var value = assignment.Groups[2].Value;
                    if (!LooksLikeRealSecret(value))
                        continue;
                    problems.Add(
                        $"possible credential assignment: {relative}: {assignment.Groups[1].Value}={value[..Math.Min(24, value.Length)]}");
                    break;
                }
            }
        }

        return problems;
    }

    private static async Task<string> ReadUtf8TextAsync(string path, CancellationToken cancellationToken)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, useAsync: true);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static bool LooksLikeRealSecret(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length < 8)
            return false;
        if (SafeValueMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
            return false;
        return normalized is not ("string.empty" or "null" or "default" or "true" or "false");
    }


    private static bool IsMarkdownFile(string suffix) =>
        string.Equals(suffix, ".md", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocaleFile(IReadOnlyList<string> parts) =>
        parts.Count >= 3 &&
        string.Equals(parts[0], "frontend-vue", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(parts[1], "src", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(parts[2], "i18n", StringComparison.OrdinalIgnoreCase);

    private static bool IsSuperpowersDocumentation(IReadOnlyList<string> parts) =>
        parts.Count >= 2 &&
        string.Equals(parts[0], "docs", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(parts[1], "superpowers", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsPartSequence(IReadOnlyList<string> parts, IReadOnlyList<string> sequence)
    {
        if (sequence.Count > parts.Count)
            return false;
        for (var start = 0; start <= parts.Count - sequence.Count; start++)
        {
            var match = true;
            for (var offset = 0; offset < sequence.Count; offset++)
            {
                if (string.Equals(parts[start + offset], sequence[offset], StringComparison.OrdinalIgnoreCase))
                    continue;
                match = false;
                break;
            }
            if (match)
                return true;
        }
        return false;
    }

    private static string[] SplitParts(string relative) =>
        relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"(?i)authorization\s*[:=]\s*(?:bearer|basic)\s+[A-Za-z0-9+/=_-]{12,}")]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex(
        @"(?i)(?<![\w-])[""']?(adminpassword|rconpassword|password|token|secret|apikey)[""']?(?![\w-])\s*[:=]\s*[""']([^""']+)[""']")]
    private static partial Regex AssignmentPattern();

    [GeneratedRegex(@"(?im)^\s*(?:python|python3)(?:\.exe)?\s+[^\r\n]+")]
    private static partial Regex PythonInvocationPattern();

    [GeneratedRegex(@"(?i)(?:actions/)?setup[-]python")]
    private static partial Regex SetupPythonPattern();
}
