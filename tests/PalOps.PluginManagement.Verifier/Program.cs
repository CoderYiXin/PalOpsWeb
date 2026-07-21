using PalOps.Web.PluginManagement;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var valid = PluginPackageService.ValidateManifest(new(
    1,
    "example-mod",
    "Example Mod",
    PluginPackageKind.ServerMod,
    "1.2.3",
    "Pal/Binaries/Win64/Mods/ExampleMod",
    ["ExampleMod.dll"],
    [new("ue4ss", "3.0.0", false)],
    ["1.*"],
    "owner/repository",
    "test"));
Assert(valid.Id == "example-mod" && valid.EntryPaths.Count == 1, "valid manifest was not normalized");
Assert(PluginPackageService.NormalizeArchiveEntry("folder/file.dll") == "folder/file.dll", "safe ZIP path changed");
Assert(PluginInventoryScanner.VersionAtLeast("v3.1.0", "3.0.0"), "semantic minimum comparison failed");
Assert(!PluginInventoryScanner.VersionAtLeast("2.9.9", "3.0.0"), "old dependency version was accepted");
Assert(PluginInventoryScanner.IsGameVersionCompatible("1.4.2", ["1.*"]), "game version wildcard failed");
Assert(!PluginInventoryScanner.IsGameVersionCompatible("2.0.0", ["1.*"]), "incompatible game version was accepted");
Assert(PluginReleaseClient.NormalizeRepository("UE4SS-RE/RE-UE4SS") == "UE4SS-RE/RE-UE4SS", "repository normalization failed");

try
{
    PluginPackageService.NormalizeArchiveEntry("../escape.dll");
    throw new InvalidOperationException("Zip Slip path was accepted");
}
catch (PluginManagementException) { }

try
{
    PluginPackageService.ValidateManifest(valid with { Dependencies = [new("example-mod", "1.0.0", false)] });
    throw new InvalidOperationException("self dependency was accepted");
}
catch (PluginManagementException) { }

try
{
    PluginPackageService.ValidateManifest(valid with { InstallDirectory = "Pal/Binaries/Win64/Mods/example.palops-disabled" });
    throw new InvalidOperationException("reserved management path was accepted");
}
catch (PluginManagementException) { }

var existing = new Dictionary<string, ManagedPluginRegistration>(StringComparer.OrdinalIgnoreCase)
{
    ["ue4ss"] = new(
        "ue4ss", "UE4SS", PluginPackageKind.UE4SS, "3.0.0", "Pal/Binaries/Win64",
        ["UE4SS.dll"], ["Pal/Binaries/Win64/UE4SS.dll"], [new("example-mod", "1.0.0", false)],
        ["*"], "UE4SS-RE/RE-UE4SS", "hash", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "test")
};
try
{
    PluginPackageService.ValidateDependencyGraph(valid, existing);
    throw new InvalidOperationException("dependency cycle was accepted");
}
catch (PluginManagementException) { }

PluginPackageService.ValidateEnabledDependentsForVersion("example-mod", "1.2.3", existing);
try
{
    PluginPackageService.ValidateEnabledDependentsForVersion("example-mod", "0.9.0", existing);
    throw new InvalidOperationException("dependent minimum version violation was accepted");
}
catch (PluginManagementException) { }
try
{
    PluginPackageService.ValidateEnabledDependentsForVersion("example-mod", null, existing);
    throw new InvalidOperationException("removal of a required dependency was accepted");
}
catch (PluginManagementException) { }

var temporary = Path.Combine(Path.GetTempPath(), "palops-plugin-verifier-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    Directory.CreateDirectory(Path.Combine(temporary, "nested"));
    await File.WriteAllTextAsync(Path.Combine(temporary, "b.txt"), "bravo");
    await File.WriteAllTextAsync(Path.Combine(temporary, "nested", "a.txt"), "alpha");
    var first = await PluginInventoryScanner.ComputeTreeHashAsync(temporary);
    File.SetLastWriteTimeUtc(Path.Combine(temporary, "b.txt"), DateTime.UtcNow.AddDays(-1));
    var second = await PluginInventoryScanner.ComputeTreeHashAsync(temporary);
    Assert(first == second, "tree hash must not depend on timestamps or enumeration order");
}
finally
{
    Directory.Delete(temporary, true);
}

Console.WriteLine("Plugin management verifier passed.");
