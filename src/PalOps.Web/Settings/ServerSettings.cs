namespace PalOps.Web.Settings;

public sealed record PalworldConnection(string BaseUrl, string UserName, string Password);
public sealed record PalDefenderConnection(string BaseUrl, string Token);
public sealed record RconConnection(string Host, int Port, string Password, bool Base64, int TimeoutSeconds);

/// <summary>
/// Read-only Palworld save indexing settings. PalOps never writes to the game save directory.
/// </summary>
public sealed record SaveGameSettings(
    string WorldDirectory,
    bool AutoIndex,
    int StableChecks,
    int StableCheckIntervalSeconds,
    int PollIntervalSeconds,
    int MaximumFileSizeMb);

public sealed record BackupSettings(
    string Directory,
    int RetentionCount,
    int CompressionLevel,
    bool ExecuteSaveFirst,
    bool RestoreEnabled);

public sealed record AutomationSettings(
    bool Enabled,
    int PollIntervalSeconds,
    int MaximumHistoryEntries);

public sealed record ServerSettings(
    PalworldConnection Palworld,
    PalDefenderConnection PalDefender,
    RconConnection Rcon,
    SaveGameSettings SaveGame,
    BackupSettings Backup,
    AutomationSettings Automation);
