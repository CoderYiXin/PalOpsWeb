using Microsoft.Extensions.FileProviders;
using System.Threading.RateLimiting;
using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using PalOps.Web.Audit;
using PalOps.Web.AdvancedOperations;
using PalOps.Web.Backups;
using PalOps.Web.Automation;
using PalOps.Web.Map;
using PalOps.Web.Health;
using PalOps.Web.Catalog;
using PalOps.Web.Configuration;
using PalOps.Web.Endpoints;
using PalOps.Web.External;
using PalOps.Web.Versioning;
using PalOps.Web.Realtime;
using PalOps.Web.Events;
using PalOps.Web.Grants;
using PalOps.Web.Infrastructure;
using PalOps.Web.Management;
using PalOps.Web.Logging;
using PalOps.Web.Players;
using PalOps.Web.Rcon;
using PalOps.Web.Security;
using PalOps.Web.Settings;
using PalOps.Web.SaveGames;
using PalOps.Web.SaveGames.Binary;
using PalOps.Web.SaveGames.Diff;
using PalOps.Web.SaveGames.Index;
using PalOps.Web.SaveGames.Projection;
using PalOps.Web.SaveGames.RawData;
using PalOps.Web.ServerRuntime;
using PalOps.Web.Notifications;
using PalOps.Web.Notifications.Providers;
using PalOps.Web.PalDefender.Configuration;
using PalOps.Web.PalworldConfiguration;
using PalOps.Web.PlayerDiscipline;
using PalOps.Web.PluginManagement;
using PalOps.Web.Maintenance;
using PalOps.Web.Statistics;

var builder = WebApplication.CreateBuilder(args);

// The Windows EventLog provider can throw for ordinary service accounts that
// cannot create/open an event source. PalOps owns a bounded file logger below,
// so keep logging independent from machine-wide Event Log permissions.
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
    options.IncludeScopes = false;
});

builder.Services.Configure<AppRuntimeOptions>(builder.Configuration.GetSection(AppRuntimeOptions.SectionName));
var runtimeOptions = builder.Configuration.GetSection(AppRuntimeOptions.SectionName).Get<AppRuntimeOptions>() ?? new AppRuntimeOptions();
var dataDirectory = Path.IsPathRooted(runtimeOptions.DataDirectory)
    ? runtimeOptions.DataDirectory
    : Path.Combine(builder.Environment.ContentRootPath, runtimeOptions.DataDirectory);
var keyDirectory = Path.Combine(dataDirectory, "keys");
Directory.CreateDirectory(keyDirectory);

builder.Services.AddDataProtection()
    .SetApplicationName("PalOps.Web")
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "PalOps.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(Math.Clamp(runtimeOptions.SessionHours, 1, 24));
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OwnerOnly", policy => policy.RequireRole(PalOpsRoles.Owner));
    options.AddPolicy("Administrator", policy => policy.RequireRole(PalOpsRoles.Owner, PalOpsRoles.Administrator));
    options.AddPolicy("Operator", policy => policy.RequireRole(PalOpsRoles.Owner, PalOpsRoles.Administrator, PalOpsRoles.Operator));
    options.AddPolicy("Auditor", policy => policy.RequireRole(PalOpsRoles.Owner, PalOpsRoles.Administrator, PalOpsRoles.Auditor));
});
builder.Services.AddMemoryCache();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = 64 * 1024;
});
builder.Services.AddSingleton<IPalOpsEventBus, PalOpsEventBus>();
builder.Services.AddSingleton<IPalOpsEventPublisher>(services => services.GetRequiredService<IPalOpsEventBus>());
builder.Services.AddSingleton<IRealtimeConnectionRegistry, RealtimeConnectionRegistry>();
builder.Services.AddSingleton<IWebhookDestinationValidator, WebhookDestinationValidator>();
builder.Services.AddSingleton<IWebhookTemplateRenderer, WebhookTemplateRenderer>();
builder.Services.AddSingleton<IWebhookProvider, GenericJsonWebhookProvider>();
builder.Services.AddSingleton<IWebhookProvider, WeComWebhookProvider>();
builder.Services.AddSingleton<IWebhookProvider, DingTalkWebhookProvider>();
builder.Services.AddSingleton<IWebhookProvider, FeishuWebhookProvider>();
builder.Services.AddSingleton<IWebhookProvider, DiscordWebhookProvider>();
builder.Services.AddSingleton<IWebhookProvider, SlackWebhookProvider>();
builder.Services.AddSingleton<IWebhookProvider, TelegramWebhookProvider>();
builder.Services.AddSingleton<IWebhookProviderRegistry, WebhookProviderRegistry>();
builder.Services.AddSingleton<IWebhookChannelStore, WebhookChannelStore>();
builder.Services.AddSingleton<IWebhookHistoryStore, WebhookHistoryStore>();
builder.Services.AddSingleton<INotificationAlertPolicyService, NotificationAlertPolicyService>();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "PalOps.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        static _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("integration", context => RateLimitPartition.GetFixedWindowLimiter(
        "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        static _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IAuthStateStore, AuthStateStore>();
builder.Services.AddSingleton<IUserAccountStore, JsonUserAccountStore>();
builder.Services.AddSingleton<ILoginAttemptTracker, LoginAttemptTracker>();
builder.Services.AddSingleton<IPrivateNetworkValidator, PrivateNetworkValidator>();
builder.Services.AddSingleton<IRuntimePathResolver, RuntimePathResolver>();
builder.Services.AddSingleton<IStorageInitializationService, StorageInitializationService>();
builder.Services.AddSingleton<FileSystemLoggerProvider>();
builder.Services.AddSingleton<ISystemLogStore>(services => services.GetRequiredService<FileSystemLoggerProvider>());
builder.Services.AddSingleton<ILoggerProvider>(services => services.GetRequiredService<FileSystemLoggerProvider>());
builder.Services.AddHostedService<FileSystemLoggerProvider>(services => services.GetRequiredService<FileSystemLoggerProvider>());
builder.Services.AddSingleton<IServerSettingsStore, ServerSettingsStore>();
builder.Services.AddHostedService<StartupDiagnosticsHostedService>();
builder.Services.AddSingleton<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<IBackupRepository, JsonBackupRepository>();
builder.Services.AddSingleton<IBackupService, BackupService>();
builder.Services.AddSingleton<IAutomationRepository, JsonAutomationRepository>();
builder.Services.AddSingleton<IAutomationExecutionService, AutomationExecutionService>();
builder.Services.AddSingleton<ICustomMapMarkerRepository, JsonCustomMapMarkerRepository>();
builder.Services.AddSingleton<ICatalogService, CatalogService>();
builder.Services.AddSingleton<IGameNameLookup, GameNameLookup>();
builder.Services.AddSingleton<IRconClient, RconClient>();
builder.Services.AddSingleton<IRconCapabilityService, RconCapabilityService>();
builder.Services.AddSingleton<IPalServerRuntimeConfigurationStore, PalServerRuntimeConfigurationStore>();
builder.Services.AddSingleton<IPalServerDiscoveryService, PalServerDiscoveryService>();
builder.Services.AddSingleton<IWindowsProcessTree, WindowsProcessTree>();
builder.Services.AddSingleton<IPalServerProcessLocator, PalServerProcessLocator>();
builder.Services.AddSingleton<IPalServerProcessController, PalServerProcessController>();
builder.Services.AddSingleton<IPalServerShutdownService, PalServerShutdownService>();
builder.Services.AddSingleton<IPalServerMetricsCollector, PalServerMetricsCollector>();
builder.Services.AddSingleton<IPalServerLiveStatusCollector, PalServerLiveStatusCollector>();
builder.Services.AddSingleton<IServerOperationHistoryStore, ServerOperationHistoryStore>();
builder.Services.AddSingleton<IPalServerRuntimeCoordinator, PalServerRuntimeCoordinator>();
builder.Services.AddSingleton<IStatisticsRepository, JsonlStatisticsRepository>();
builder.Services.AddSingleton<IStatisticsStateStore, StatisticsStateStore>();
builder.Services.AddSingleton<StatisticsRecorder>();
builder.Services.AddSingleton<IStatisticsRecorder>(services => services.GetRequiredService<StatisticsRecorder>());
builder.Services.AddSingleton<IStatisticsQueryService, StatisticsQueryService>();
builder.Services.AddSingleton<IPlayerDisciplineRepository, JsonPlayerDisciplineRepository>();
builder.Services.AddSingleton<IPalDefenderAccessControlReader, PalDefenderAccessControlReader>();
builder.Services.AddSingleton<IPalDefenderAccessControlWriter, PalDefenderAccessControlWriter>();
builder.Services.AddSingleton<IPlayerDisciplineService, PlayerDisciplineService>();
builder.Services.AddHostedService<TemporaryBanExpiryService>();
builder.Services.AddHostedService<PlayerIdentitySyncService>();
builder.Services.AddSingleton<IMaintenanceRepository, JsonMaintenanceRepository>();
builder.Services.AddSingleton<MaintenanceValidator>();
builder.Services.AddSingleton<CrashGuardEvaluator>();
builder.Services.AddSingleton<IMaintenanceScriptRunner, MaintenanceScriptRunner>();
builder.Services.AddSingleton<IMaintenanceActivityGate, MaintenanceActivityGate>();
builder.Services.AddSingleton<IServerOperationWaiter, ServerOperationWaiter>();
builder.Services.AddSingleton<IMaintenanceExecutionService, MaintenanceExecutionService>();
builder.Services.AddHostedService<CrashGuardService>();
builder.Services.AddHostedService<PalServerRuntimeMonitorService>();
builder.Services.AddHostedService<MaintenanceSchedulerService>();
builder.Services.AddHostedService<RealtimeSnapshotDispatcherService>();
builder.Services.AddHostedService<PlayerPresenceMonitorService>();
builder.Services.AddHostedService<StatisticsCollectorService>();
builder.Services.AddSingleton<ISavePathGuard, SavePathGuard>();
builder.Services.AddSingleton<ISaveSourceResolver, SaveSourceResolver>();
builder.Services.AddSingleton<IStableSaveSnapshotService, StableSaveSnapshotService>();
builder.Services.AddSingleton<IPalworldOozDecoder, PalworldOozDecoder>();
builder.Services.AddSingleton<IPalworldSavDecompressor, PalworldSavDecompressor>();
builder.Services.AddSingleton<IGvasParser>(_ => new GvasParser());
builder.Services.AddSingleton<IPalworldRawDataDecoder, PalworldRawDataDecoder>();
builder.Services.AddSingleton<IPlayerSaveProjector, PlayerSaveProjector>();
builder.Services.AddSingleton<IGuildBaseReconciliationService, GuildBaseReconciliationService>();
builder.Services.AddSingleton<IWorldSaveProjector, WorldSaveProjector>();
builder.Services.AddSingleton<ISaveChangeSnapshotProjector, SaveChangeSnapshotProjector>();
builder.Services.AddSingleton<ISaveChangeSnapshotRepository>(services =>
{
    var environment = services.GetRequiredService<IHostEnvironment>();
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppRuntimeOptions>>().Value;
    var root = Path.IsPathRooted(options.DataDirectory)
        ? options.DataDirectory
        : Path.Combine(environment.ContentRootPath, options.DataDirectory);
    return new JsonSaveChangeSnapshotRepository(Path.Combine(root, "save-diff"));
});
builder.Services.AddSingleton<ISaveIndexRepository>(services =>
{
    var environment = services.GetRequiredService<IHostEnvironment>();
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppRuntimeOptions>>().Value;
    var root = Path.IsPathRooted(options.DataDirectory)
        ? options.DataDirectory
        : Path.Combine(environment.ContentRootPath, options.DataDirectory);
    return new JsonSaveIndexRepository(Path.Combine(root, "save-index"));
});
builder.Services.AddSingleton<ISaveIndexingService, SaveIndexingService>();
builder.Services.AddSingleton<ISaveDiffService, SaveDiffService>();
builder.Services.AddSingleton<ISaveDiffReportWriter, SaveDiffReportWriter>();
builder.Services.AddHostedService<SaveIndexMonitorService>();
builder.Services.AddHostedService<SaveDiffBackfillService>();
builder.Services.AddHostedService<AutomationSchedulerService>();
builder.Services.AddSingleton<SystemHealthService>();
builder.Services.AddSingleton<ISystemHealthService>(services => services.GetRequiredService<SystemHealthService>());
builder.Services.AddHostedService<SystemHealthService>(services => services.GetRequiredService<SystemHealthService>());
builder.Services.AddSingleton<IPlayerAggregationCache, PlayerAggregationCache>();
builder.Services.AddScoped<IPlayerAggregationService, PlayerAggregationService>();
builder.Services.AddScoped<IPlayerIndexQueryService, PlayerIndexQueryService>();
builder.Services.AddScoped<IGrantValidator, GrantValidator>();
builder.Services.AddScoped<IBulkGrantService, BulkGrantService>();
builder.Services.AddScoped<IPlayerQuickActionService, PlayerQuickActionService>();
builder.Services.AddTransient<CsrfValidationFilter>();
builder.Services.AddSingleton<WebhookDispatcherService>();
builder.Services.AddSingleton<IWebhookDeliveryService>(services => services.GetRequiredService<WebhookDispatcherService>());
builder.Services.AddHostedService<WebhookDispatcherService>(services => services.GetRequiredService<WebhookDispatcherService>());

builder.Services.AddSingleton<IApplicationVersionProvider, ApplicationVersionProvider>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHttpClient("webhooks")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    });
builder.Services.AddHttpClient<IPalworldApiClient, PalworldApiClient>(client => client.Timeout = TimeSpan.FromSeconds(12));
builder.Services.AddHttpClient<IPalDefenderApiClient, PalDefenderApiClient>(client => client.Timeout = TimeSpan.FromSeconds(12));
builder.Services.AddHttpClient<IPalDefenderReleaseClient, PalDefenderReleaseClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.Timeout = TimeSpan.FromSeconds(12);
});
builder.Services.AddHttpClient<IPalOpsReleaseClient, PalOpsReleaseClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.Timeout = TimeSpan.FromSeconds(12);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
});
builder.Services.AddHttpClient<IPluginReleaseClient, PluginReleaseClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
});
builder.Services.AddSingleton<IPlatformVersionService, PlatformVersionService>();
builder.Services.AddSingleton<IPalDefenderVersionService, PalDefenderVersionService>();
builder.Services.AddSingleton<IPalDefenderConfigurationPathResolver, PalDefenderConfigurationPathResolver>();
builder.Services.AddSingleton<IPalDefenderConfigurationValidator, PalDefenderConfigurationValidator>();
builder.Services.AddSingleton<IPalDefenderConfigurationService, PalDefenderConfigurationService>();
builder.Services.AddSingleton(PalworldConfigurationMetadata.Create());
builder.Services.AddSingleton<PalworldSettingsIniCodec>();
builder.Services.AddSingleton<PalworldConfigurationValidator>();
builder.Services.AddSingleton<IPalworldConfigurationPathResolver, PalworldConfigurationPathResolver>();
builder.Services.AddSingleton<IPalworldConfigurationService, PalworldConfigurationService>();
builder.Services.AddSingleton<IPluginManagementPathResolver, PluginManagementPathResolver>();
builder.Services.AddSingleton<IPluginManagementRepository, PluginManagementRepository>();
builder.Services.AddSingleton<IPluginInventoryScanner, PluginInventoryScanner>();
builder.Services.AddSingleton<IPluginPackageService, PluginPackageService>();

builder.Services.AddSingleton<IAdvancedOperationsRepository, JsonAdvancedOperationsRepository>();
builder.Services.AddSingleton<AdvancedOperationsValidator>();
builder.Services.AddSingleton<IAdvancedOperationsReadinessService, AdvancedOperationsReadinessService>();
builder.Services.AddSingleton<IDiagnosticCenterService, DiagnosticCenterService>();
builder.Services.AddSingleton<IIncidentCenterService, IncidentCenterService>();
builder.Services.AddSingleton<IPlayerInsightsService, PlayerInsightsService>();
builder.Services.AddSingleton<IWorldGovernanceService, WorldGovernanceService>();
builder.Services.AddSingleton<IDisasterRecoveryService, DisasterRecoveryService>();
builder.Services.AddSingleton<IUpdateCenterService, UpdateCenterService>();
builder.Services.AddSingleton<IConfigurationVersionService, ConfigurationVersionService>();
builder.Services.AddSingleton<IOperationsPlaybookService, OperationsPlaybookService>();
builder.Services.AddSingleton<ISecurityCenterService, SecurityCenterService>();
builder.Services.AddSingleton<IIntegrationCenterService, IntegrationCenterService>();
builder.Services.AddHostedService<AdvancedOperationsMonitorService>();

var app = builder.Build();

await app.Services.GetRequiredService<IAuthStateStore>().EnsureBootstrapPasswordAsync();
await app.Services.GetRequiredService<IUserAccountStore>().EnsureOwnerFromLegacyAsync();

app.UseMiddleware<ApiExceptionMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; worker-src 'self' blob:; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseRateLimiter();
app.UseMiddleware<IntegrationApiTokenMiddleware>();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.Context.Request.Path.StartsWithSegments("/map/data")
            || context.Context.Request.Path.StartsWithSegments("/map/tiles"))
        {
            context.Context.Response.Headers["Cache-Control"] = "public,max-age=86400";
        }
    }
});
var catalogAssetsPath = Path.Combine(app.Environment.ContentRootPath, "StaticCatalog");
if (Directory.Exists(catalogAssetsPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(catalogAssetsPath),
        RequestPath = "/catalog",
        OnPrepareResponse = context =>
        {
            context.Context.Response.Headers["Cache-Control"] = "public,max-age=2592000,immutable";
        }
    });
}
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapSettingsEndpoints();
app.MapPlayerEndpoints();
app.MapPlayerV1Endpoints();
app.MapSaveGameEndpoints();
app.MapSystemEndpoints();
app.MapPlatformVersionEndpoints();
app.MapGuildEndpoints();
app.MapMapEndpoints();
app.MapGrantEndpoints();
app.MapCatalogEndpoints();
app.MapManagementEndpoints();
app.MapPalDefenderVersionEndpoints();
app.MapPalDefenderConfigurationEndpoints();
app.MapPalworldConfigurationEndpoints();
app.MapNotificationEndpoints();
app.MapRconEndpoints();
app.MapServerRuntimeEndpoints();
app.MapBackupEndpoints();
app.MapAutomationEndpoints();
app.MapMaintenanceEndpoints();
app.MapStatisticsEndpoints();
app.MapSaveDiffEndpoints();
app.MapPlayerDisciplineEndpoints();
app.MapPluginManagementEndpoints();
app.MapSystemLogEndpoints();
app.MapUserEndpoints();
app.MapAuditEndpoints();
app.MapAdvancedOperationsEndpoints();
app.MapHub<PalOpsHub>("/hubs/palops").RequireAuthorization();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow }));
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
