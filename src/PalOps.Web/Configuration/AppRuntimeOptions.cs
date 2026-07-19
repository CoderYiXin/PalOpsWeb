namespace PalOps.Web.Configuration;

public sealed class AppRuntimeOptions
{
    public const string SectionName = "PalOps";

    public string DataDirectory { get; set; } = "data";
    public bool AllowPublicServerTargets { get; set; }
    public int MaxPlayersPerGrant { get; set; } = 12;
    public int MaxResourcesPerGrant { get; set; } = 20;
    public long MaxItemUnitsPerGrant { get; set; } = 999_999;
    public int MaxPalsPerGrant { get; set; } = 99;
    public long MaxExperiencePerGrant { get; set; } = 100_000_000;
    public int GrantParallelism { get; set; } = 2;
    public int SessionHours { get; set; } = 8;
    public int RuntimeMonitorIntervalSeconds { get; set; } = 10;
    public int LiveStatusRefreshIntervalSeconds { get; set; } = 10;
    public int RuntimeStartupTimeoutSeconds { get; set; } = 90;
    public int RuntimeShutdownTimeoutSeconds { get; set; } = 120;
    public int RuntimeSaveWaitSeconds { get; set; } = 5;
    public int RuntimeRestartCooldownSeconds { get; set; } = 3;
    public int RuntimeHistoryLimit { get; set; } = 1000;
}
