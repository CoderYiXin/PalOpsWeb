using PalOps.Web.Endpoints;
using PalOps.Web.PlayerDiscipline;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Assert(PlayerDisciplineService.EscapeCommandArgument("steam_123") == "\"steam_123\"", "identifier escaping failed");
Assert(PlayerDisciplineService.EscapeCommandArgument("quoted \"reason\"") == "\"quoted \\\"reason\\\"\"", "quote escaping failed");
try
{
    PlayerDisciplineService.EscapeCommandArgument("line\nbreak");
    throw new InvalidOperationException("control character must fail");
}
catch (ArgumentException) { }

var state = new PlayerDisciplineState();
state.Identities["steam_1"] = new IdentityMetadata
{
    UserId = "steam_1",
    FirstSeenAt = DateTimeOffset.UtcNow,
    LastSeenAt = DateTimeOffset.UtcNow,
    Names = new(StringComparer.OrdinalIgnoreCase) { "Alice", "alice" }
};
Assert(state.Identities["steam_1"].Names.Count == 1, "identity aliases must be case insensitive");
static T InvokePrivate<T>(string name, params object[] arguments)
{
    var method = typeof(PalDefenderAccessControlReader).GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing parser method {name}");
    return (T)(method.Invoke(null, arguments) ?? throw new InvalidOperationException($"Parser method {name} returned null"));
}

var whitelistWarnings = new List<string>();
var whitelist = InvokePrivate<IReadOnlyList<string>>(
    "ExtractWhitelist",
    "{\"whitelist\":[\"steam_12345\"],\"description\":\"operator note\"}",
    whitelistWarnings);
Assert(whitelist.SequenceEqual(["steam_12345"]), "whitelist parser must ignore structural fields");

var banWarnings = new List<string>();
var bans = InvokePrivate<IReadOnlyList<PalDefenderBanRecord>>(
    "ExtractBans",
    "[{\"userId\":\"steam_98765\",\"reason\":\"cheating\"}]",
    banWarnings);
Assert(bans.Count == 1 && bans[0].Identifier == "steam_98765" && bans[0].Reason == "cheating", "ban parser must not turn metadata keys into identifiers");

var mappedBanWarnings = new List<string>();
var mappedBans = InvokePrivate<IReadOnlyList<PalDefenderBanRecord>>(
    "ExtractBans",
    "{\"bans\":{\"steam_55555\":{\"reason\":\"repeat abuse\"}}}",
    mappedBanWarnings);
Assert(mappedBans.Count == 1 && mappedBans[0].Identifier == "steam_55555" && mappedBans[0].Reason == "repeat abuse", "mapped ban parser must retain nested reason");

static bool MutateWhitelist(System.Text.Json.Nodes.JsonNode root, string userId, bool include)
{
    var method = typeof(PalDefenderAccessControlWriter).GetMethod(
        "Mutate",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing whitelist mutation helper");
    return (bool)(method.Invoke(null, [root, userId, include])
        ?? throw new InvalidOperationException("Whitelist mutation returned null"));
}

var arrayRoot = System.Text.Json.Nodes.JsonNode.Parse("[]")!;
Assert(MutateWhitelist(arrayRoot, "steam_12345", true), "whitelist array add must change the document");
Assert(arrayRoot.ToJsonString().Contains("steam_12345", StringComparison.Ordinal), "whitelist array add must persist the UserId");
Assert(!MutateWhitelist(arrayRoot, "STEAM_12345", true), "whitelist add must be case-insensitive and idempotent");
Assert(MutateWhitelist(arrayRoot, "steam_12345", false), "whitelist array remove must change the document");

var objectRoot = System.Text.Json.Nodes.JsonNode.Parse("{\"WhiteList\":[]}")!;
Assert(MutateWhitelist(objectRoot, "gdk_98765", true), "whitelist object collection add must be supported");
Assert(objectRoot.ToJsonString().Contains("gdk_98765", StringComparison.Ordinal), "whitelist object add must persist the UserId");


static string ValidateWhitelistUserId(string value)
{
    var method = typeof(PlayerDisciplineService).GetMethod(
        "ValidatePalDefenderUserId",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing PalDefender UserId validator");
    try
    {
        return (string)(method.Invoke(null, [value])
            ?? throw new InvalidOperationException("UserId validator returned null"));
    }
    catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
    {
        throw exception.InnerException;
    }
}

Assert(ValidateWhitelistUserId("steam_76561198012345678") == "steam_76561198012345678", "Steam UserId must be accepted");
Assert(ValidateWhitelistUserId("gdk_2533274812345678") == "gdk_2533274812345678", "GDK UserId must be accepted");
try
{
    ValidateWhitelistUserId("b7f4e91a-2c53-4d8f-a6e1-93c4bb62a7d1");
    throw new InvalidOperationException("save-game PlayerUID must not be accepted as whitelist UserId");
}
catch (ArgumentException) { }


static (bool Parsed, string UserId, string? Reason) ParseKickCommand(string command)
{
    var method = typeof(RconEndpoints).GetMethod(
        "TryParseKickCommand",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing RCON kick parser");
    object?[] arguments = [command, string.Empty, null];
    var parsed = (bool)(method.Invoke(null, arguments)
        ?? throw new InvalidOperationException("RCON kick parser returned null"));
    return (parsed, (string)(arguments[1] ?? string.Empty), arguments[2] as string);
}

var parsedKick = ParseKickCommand("/kick steam_76561198012345678 \"Repeated abuse\"");
Assert(parsedKick.Parsed, "RCON kick command must be detected");
Assert(parsedKick.UserId == "steam_76561198012345678", "RCON kick UserId parsing failed");
Assert(parsedKick.Reason == "Repeated abuse", "RCON kick reason parsing failed");
Assert(!ParseKickCommand("whitelist_add steam_76561198012345678").Parsed, "non-kick RCON command must not be recorded as a kick");

var kickState = new PlayerDisciplineState();
kickState.Kicks.Add(new KickMetadata
{
    Id = "kick-1",
    UserId = "steam_12345",
    Reason = "test",
    Operator = "owner",
    KickedAt = DateTimeOffset.UtcNow,
    Source = "palops"
});
Assert(kickState.Kicks.Count == 1 && kickState.Kicks[0].UserId == "steam_12345", "kick history state must retain successful kicks");

Console.WriteLine("Player discipline verifier passed.");
