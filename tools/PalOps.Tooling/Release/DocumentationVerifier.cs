using System.Text.RegularExpressions;
using PalOps.Tooling.Cli;
using PalOps.Tooling.Infrastructure;

namespace PalOps.Tooling.Release;

public static partial class DocumentationVerifier
{
    private static readonly string[] RequiredDocuments =
    [
        "README.md",
        "README.en.md",
        "CHANGELOG.md",
        "CONTRIBUTING.md",
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
        Path.Combine("docs", "map-data-package.md"),
        Path.Combine("docs", "map-data-package.en.md"),
        Path.Combine("docs", "paldeck-data-sources.md"),
        Path.Combine("docs", "paldeck-data-sources.en.md"),
        Path.Combine("docs", "paldefender-configuration-management.md"),
        Path.Combine("docs", "paldefender-configuration-management.en.md"),
        Path.Combine("docs", "paldefender-deployment.md"),
        Path.Combine("docs", "paldefender-deployment.en.md"),
        Path.Combine("docs", "release-checklist.md"),
        Path.Combine("docs", "release-checklist.en.md"),
        Path.Combine("docs", "world-map-data-1.1.0.md"),
        Path.Combine("docs", "world-map-data-1.1.0.en.md"),
        Path.Combine("docs", "images", "README.md"),
        Path.Combine("docs", "images", "README.en.md"),
        Path.Combine("frontend-vue", "README.md"),
        Path.Combine("frontend-vue", "README.en.md")
    ];

    private static readonly string[] RequiredImages =
    [
        Path.Combine("docs", "images", "overview-dashboard.webp"),
        Path.Combine("docs", "images", "player-management.webp"),
        Path.Combine("docs", "images", "guild-bases.webp"),
        Path.Combine("docs", "images", "world-map.webp"),
        Path.Combine("docs", "images", "resource-grant.webp"),
        Path.Combine("docs", "images", "message-center.webp"),
        Path.Combine("docs", "images", "rcon-console.webp"),
        Path.Combine("docs", "images", "automation-jobs.webp"),
        Path.Combine("docs", "images", "save-backups.webp"),
        Path.Combine("docs", "images", "notification-channels.webp"),
        Path.Combine("docs", "images", "notification-history.webp"),
        Path.Combine("docs", "images", "system-settings.webp"),
        Path.Combine("docs", "images", "paldefender-console.webp"),
        Path.Combine("docs", "images", "save-index.webp"),
        Path.Combine("docs", "images", "catalog-management.webp"),
        Path.Combine("docs", "images", "audit-log.webp"),
        Path.Combine("docs", "images", "system-logs.webp"),
        Path.Combine("docs", "images", "user-management.webp"),
        Path.Combine("docs", "images", "about-project.webp")
    ];

    private static readonly string[] ForbiddenPlaceholderTokens =
    [
        "T" + "BD",
        "T" + "ODO",
        "implement " + "later",
        "fill in " + "details"
    ];

    public static async Task<int> RunAsync(
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        arguments.EnsureOnly("root");
        var root = arguments.GetOptionalPath("root") ?? Environment.CurrentDirectory;
        if (!Directory.Exists(root))
            throw ToolExitException.Usage($"文档校验目录不存在：{root}");

        var problems = await VerifyAsync(Path.GetFullPath(root), cancellationToken);
        if (problems.Count > 0)
            throw ToolExitException.Verification(
                "Documentation verification failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(problem => "- " + problem)));

        Console.WriteLine("Documentation verification passed.");
        return 0;
    }

    public static async Task<IReadOnlyList<string>> VerifyAsync(
        string root,
        CancellationToken cancellationToken)
    {
        root = Path.GetFullPath(root);
        var problems = new List<string>();
        foreach (var relative in RequiredDocuments.Concat(RequiredImages))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(Path.Combine(root, relative)))
                problems.Add($"required documentation asset missing: {relative}");
        }

        foreach (var relative in RequiredDocuments.Where(path =>
                     path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(root, relative);
            if (!File.Exists(path))
                continue;
            var text = await File.ReadAllTextAsync(path, cancellationToken);
            VerifyPlaceholders(relative, text, problems);
            VerifyRelativeLinks(root, relative, text, problems);
        }

        VerifyReadmeParity(root, problems);
        VerifyDirectFrontendDependencyNotices(root, problems);
        return problems;
    }

    private static void VerifyPlaceholders(
        string relative,
        string text,
        ICollection<string> problems)
    {
        foreach (var token in ForbiddenPlaceholderTokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                problems.Add($"placeholder token in documentation: {relative}: {token}");
        }
    }

    private static void VerifyRelativeLinks(
        string root,
        string markdownRelativePath,
        string text,
        ICollection<string> problems)
    {
        var documentDirectory = Path.GetDirectoryName(Path.Combine(root, markdownRelativePath)) ?? root;
        foreach (Match match in MarkdownLinkPattern().Matches(text))
        {
            var target = match.Groups[1].Value.Trim();
            if (target.Length == 0 ||
                target.StartsWith('#') ||
                target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                continue;

            var withoutAnchor = target.Split('#', 2)[0];
            if (withoutAnchor.Length == 0)
                continue;

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(withoutAnchor.Replace('/', Path.DirectorySeparatorChar));
            }
            catch (UriFormatException)
            {
                problems.Add($"invalid relative documentation link: {markdownRelativePath}: {target}");
                continue;
            }

            string resolved;
            try
            {
                resolved = Path.GetFullPath(Path.Combine(documentDirectory, decoded));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                problems.Add($"invalid relative documentation link: {markdownRelativePath}: {target}");
                continue;
            }

            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(resolved, root, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"documentation link escapes repository: {markdownRelativePath}: {target}");
                continue;
            }
            if (!File.Exists(resolved) && !Directory.Exists(resolved))
                problems.Add($"broken relative documentation link: {markdownRelativePath}: {target}");
        }
    }

    private static void VerifyReadmeParity(string root, ICollection<string> problems)
    {
        var readmes = new[] { "README.md", "README.en.md" }
            .ToDictionary(
                relative => relative,
                relative => File.Exists(Path.Combine(root, relative))
                    ? File.ReadAllText(Path.Combine(root, relative))
                    : string.Empty,
                StringComparer.OrdinalIgnoreCase);

        string[] requiredTerms =
        [
            "Windows",
            "SignalR",
            "PalDefender",
            "Webhook",
            "Palpagos",
            "World Tree",
            "docs/images/overview-dashboard.webp",
            "docs/images/world-map.webp"
        ];

        foreach (var (relative, text) in readmes)
        {
            foreach (var term in requiredTerms)
            {
                if (!text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"README parity term missing: {relative}: {term}");
            }
        }
    }

    private static void VerifyDirectFrontendDependencyNotices(
        string root,
        ICollection<string> problems)
    {
        var packagePath = Path.Combine(root, "frontend-vue", "package.json");
        var noticePath = Path.Combine(root, "THIRD-PARTY-NOTICES.md");
        if (!File.Exists(packagePath) || !File.Exists(noticePath))
            return;

        try
        {
            using var package = System.Text.Json.JsonDocument.Parse(File.ReadAllText(packagePath));
            if (!package.RootElement.TryGetProperty("dependencies", out var dependencies))
                return;

            var notice = File.ReadAllText(noticePath);
            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (!notice.Contains(dependency.Name, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"direct frontend dependency missing from notices: {dependency.Name}");
            }
        }
        catch (System.Text.Json.JsonException exception)
        {
            problems.Add($"invalid frontend dependency manifest: frontend-vue/package.json: {exception.Message}");
        }
    }

    [GeneratedRegex(@"!?(?:\[[^\]]*\])\(([^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex MarkdownLinkPattern();
}
