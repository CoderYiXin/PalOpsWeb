using PalOps.Tooling.Catalog;
using PalOps.Tooling.Cli;
using PalOps.Tooling.Infrastructure;
using PalOps.Tooling.Map;
using PalOps.Tooling.Release;

try
{
    var arguments = CliArguments.Parse(args);
    if (arguments.CommandPath == "release verify")
        return await ReleaseVerifier.RunAsync(arguments, CancellationToken.None);
    if (arguments.CommandPath == "docs verify")
        return await DocumentationVerifier.RunAsync(arguments, CancellationToken.None);
    if (arguments.CommandPath == "release manifest")
        return await ReleaseManifestWriter.RunAsync(arguments, CancellationToken.None);

    var paths = RepositoryPaths.Resolve(arguments.GetOptionalPath("root"));
    return arguments.CommandPath switch
    {
        "map verify" => await MapVerifier.RunAsync(paths, arguments, CancellationToken.None),
        "map fetch" => await MapFetcher.RunAsync(paths, arguments, CancellationToken.None),
        "catalog normalize" => await CatalogNormalizer.RunAsync(paths, arguments, CancellationToken.None),
        "catalog merge-names" => await NameMapMerger.RunAsync(paths, arguments, CancellationToken.None),
        "catalog verify" => await CatalogVerifier.RunAsync(paths, arguments, CancellationToken.None),
        _ => throw ToolExitException.Usage(
            $"未知命令“{arguments.CommandPath}”。可用命令：map fetch、map verify、catalog normalize、catalog merge-names、catalog verify、docs verify、release verify、release manifest。")
    };
}
catch (ToolExitException exception)
{
    Console.Error.WriteLine(exception.Message);
    return exception.ExitCode;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("操作已取消。");
    return 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"工具执行失败：{exception.Message}");
    return 1;
}
