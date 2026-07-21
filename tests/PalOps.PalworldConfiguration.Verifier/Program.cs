using PalOps.Web.PalworldConfiguration;

VerifyCodec();
VerifyValidation();
VerifyLaunchArguments();
VerifyEffectivePortConflicts();
VerifyDocumentedRangePolicy();
VerifyCurrentPalConfCompatibleConfiguration();
Console.WriteLine("Palworld configuration verifier passed.");

static void VerifyCodec()
{
    const string source = """
        [/Script/Pal.PalGameWorldSettings]
        OptionSettings=(ServerName="Alpha, Tokyo",ServerDescription="A \\"quoted\\" server",PublicPort=8211,bIsPvP=False,CrossplayPlatforms=(Steam,Xbox),UnknownFutureValue=(A=1,B=2))
        """;
    var codec = new PalworldSettingsIniCodec();
    var document = codec.Parse(source);
    Assert(document.GetRawValue("ServerName") == "\"Alpha, Tokyo\"", "quoted commas must remain inside one value");
    Assert(document.GetRawValue("CrossplayPlatforms") == "(Steam,Xbox)", "nested lists must be retained");
    Assert(document.GetRawValue("UnknownFutureValue") == "(A=1,B=2)", "unknown nested fields must round-trip");
    document.SetRawValue("PublicPort", "9011");
    var reparsed = codec.Parse(codec.Serialize(document));
    Assert(reparsed.GetRawValue("PublicPort") == "9011", "updated fields must serialize");
    Assert(reparsed.GetRawValue("UnknownFutureValue") == "(A=1,B=2)", "unknown fields must survive structured edits");
}

static void VerifyValidation()
{
    var codec = new PalworldSettingsIniCodec();
    var validator = new PalworldConfigurationValidator(PalworldConfigurationMetadata.Create());
    var document = codec.Parse("[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(PublicPort=70000,RCONEnabled=True,RCONPort=8211,RESTAPIEnabled=True,RESTAPIPort=8211,ServerPlayerMaxNum=64,PalSpawnNumRate=4.5)");
    var result = validator.Validate(document, "-port=8211 -players=33", false);
    Assert(!result.Valid, "out-of-range public port must fail validation");
    Assert(result.Diagnostics.Any(item => item.Code == "PORT_CONFLICT"), "duplicate ports must be reported");
    Assert(result.Diagnostics.Any(item => item.Code == "PERFORMANCE_RISK"), "high spawn rate must produce a performance warning");
}

static void VerifyLaunchArguments()
{
    var parsed = PalworldLaunchArgumentParser.Parse("-port=8211 -players=32 -logformat=json -publiclobby -UsePerfThreads -NoAsyncLoadingThread -UseMultithreadForDS -NumberOfWorkerThreadsServer=8");
    Assert(parsed.Values["port"] == "8211", "port argument must parse");
    Assert(parsed.Values["players"] == "32", "players argument must parse");
    Assert(parsed.Flags.Contains("publiclobby"), "flag argument must parse");
    Assert(parsed.Flags.Contains("noasyncloadingthread"), "official async-loading flag must parse");
    Assert(parsed.Values["numberofworkerthreadsserver"] == "8", "official worker-thread argument must parse");
    var duplicate = PalworldLaunchArgumentParser.Parse("-port=8211 -port=9000");
    Assert(duplicate.Diagnostics.Any(item => item.Code == "DUPLICATE_ARGUMENT"), "duplicate startup switches must be reported");
    var unclosedQuote = PalworldLaunchArgumentParser.Parse("-publicip=\"127.0.0.1");
    Assert(unclosedQuote.Diagnostics.Any(item => item.Code == "ARGUMENT_QUOTE_UNCLOSED"), "unclosed startup-argument quotes must be rejected");
}

static void VerifyEffectivePortConflicts()
{
    var codec = new PalworldSettingsIniCodec();
    var validator = new PalworldConfigurationValidator(PalworldConfigurationMetadata.Create());

    var disabledServices = codec.Parse("[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(PublicPort=8211,RCONEnabled=False,RCONPort=8211,RESTAPIEnabled=False,RESTAPIPort=8211)");
    var consistent = validator.Validate(disabledServices, "-port=8211 -publicport=8211", false);
    Assert(!consistent.Diagnostics.Any(item => item.Code == "PORT_CONFLICT"), "matching advertised/game ports and disabled service ports must not conflict");

    var enabledRcon = codec.Parse("[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(PublicPort=8211,RCONEnabled=True,RCONPort=8211,RESTAPIEnabled=False,RESTAPIPort=8211)");
    var collision = validator.Validate(enabledRcon, "-port=8211", false);
    Assert(collision.Diagnostics.Any(item => item.Code == "PORT_CONFLICT"), "enabled RCON must not share the effective game listener port");

    var advertisedOnly = codec.Parse("[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(PublicPort=9000,RCONEnabled=True,RCONPort=8211,RESTAPIEnabled=False,RESTAPIPort=8212)");
    var defaultListenerCollision = validator.Validate(advertisedOnly, string.Empty, false);
    Assert(defaultListenerCollision.Diagnostics.Any(item => item.Code == "PORT_CONFLICT"), "without -port the game listener remains on 8211 even when PublicPort advertises another port");
    Assert(defaultListenerCollision.Diagnostics.Any(item => item.Code == "PUBLIC_PORT_IS_ADVERTISED_ONLY"), "advertised-only PublicPort semantics must be explained");
}


static void VerifyDocumentedRangePolicy()
{
    var codec = new PalworldSettingsIniCodec();
    var validator = new PalworldConfigurationValidator(PalworldConfigurationMetadata.Create());
    var custom = codec.Parse("[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(AutoSaveSpan=5,AutoResetGuildTimeNoOnlinePlayers=72.000000,GuildPlayerMaxNum=200,DropItemAliveMaxHours=0.000000,DropItemMaxNum=0,BaseCampMaxNumInGuild=51,BaseCampWorkerMaxNum=75,ServerReplicatePawnCullDistance=4999,ServerPlayerMaxNum=999,RCONEnabled=False,RESTAPIEnabled=False)");
    var result = validator.Validate(custom, string.Empty, false);
    Assert(result.Valid, "gameplay tuning ranges are guidance and must not reject a syntactically valid Palworld file");
    Assert(!result.Diagnostics.Any(item => item.Code is "INVALID_INTEGER" or "OUT_OF_RANGE"), "reference ranges must not be reported as hard errors");

    var invalidPort = codec.Parse("[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(PublicPort=70000,RCONEnabled=False,RESTAPIEnabled=False)");
    var portResult = validator.Validate(invalidPort, string.Empty, false);
    Assert(!portResult.Valid, "network port bounds remain hard validation rules");
    Assert(portResult.Diagnostics.Any(item => item.Code == "OUT_OF_RANGE" && item.Path == "PublicPort"), "invalid public ports must be reported");
}

static void VerifyCurrentPalConfCompatibleConfiguration()
{
    const string source = """
        [/Script/Pal.PalGameWorldSettings]
        OptionSettings=(Difficulty=None,RandomizerType=None,RandomizerSeed="",bIsRandomizerPalLevelRandom=True,PlayerStaminaDecreaceRate=0.000000,AutoResetGuildTimeNoOnlinePlayers=72.000000,AutoSaveSpan=30.000000,BaseCampMaxNumInGuild=50,BaseCampWorkerMaxNum=50,ServerPlayerMaxNum=64,RCONEnabled=True,RCONPort=25575,RESTAPIEnabled=True,RESTAPIPort=8212,PublicPort=8211,DenyTechnologyList=(),PhysicsActiveDropItemMaxNum=-1,VoiceChatZeroVolumeDistance=15000.000000)
        """;
    var codec = new PalworldSettingsIniCodec();
    var metadata = PalworldConfigurationMetadata.Create();
    var document = codec.Parse(source);
    var result = new PalworldConfigurationValidator(metadata).Validate(document, string.Empty, false);
    Assert(result.Valid, "current Palworld/pal-conf compatible values must not be rejected");
    Assert(!result.Diagnostics.Any(item => item.Code is "INVALID_INTEGER" or "OUT_OF_RANGE" or "UNKNOWN_SETTING"), "valid current settings must not create false-positive errors or one message per unknown field");
    var response = metadata.ToResponse(result.Settings);
    Assert(response.Fields.Any(item => item.Key == "Difficulty"), "runtime metadata must include preserved future/current fields");
    Assert(response.Fields.Any(item => item.Key == "VoiceChatZeroVolumeDistance"), "runtime metadata must expose newer Palworld fields to the structured editor");
    Assert(metadata.Find("RandomizerSeed")?.ValueType == "string", "RandomizerSeed must support an empty quoted value");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
