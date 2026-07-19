using PalOps.Web.Contracts;
using PalOps.Web.SaveGames;
using PalOps.Web.SaveGames.Index;
using PalOps.Web.SaveGames.Binary;
using PalOps.Web.Security;
using PalOps.Web.Settings;

namespace PalOps.Web.Endpoints;

public static class SaveGameEndpoints
{
    public static IEndpointRouteBuilder MapSaveGameEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1").RequireAuthorization().WithTags("Save Index");

        group.MapGet("/save-settings", async (
            IServerSettingsStore settingsStore,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var settings = (await settingsStore.GetAsync(cancellationToken)).SaveGame;
            return Results.Ok(Envelope(context, new SaveSettingsV1Response(
                settings.WorldDirectory,
                settings.AutoIndex,
                settings.StableChecks,
                settings.StableCheckIntervalSeconds,
                settings.PollIntervalSeconds,
                settings.MaximumFileSizeMb)));
        }).RequireAuthorization("Administrator");

        group.MapPost("/save-settings/discover", async (
            IServerSettingsStore settingsStore,
            ISaveSourceResolver resolver,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var settings = (await settingsStore.GetAsync(cancellationToken)).SaveGame;
            var candidates = await resolver.DiscoverAsync(settings, cancellationToken);
            return Results.Ok(Envelope(context, candidates));
        }).RequireAuthorization("Administrator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/save-index/status", (
            ISaveIndexingService service,
            HttpContext context) => Results.Ok(Envelope(context, service.Status)));

        group.MapGet("/save-index/inspect", async (
            IServerSettingsStore settingsStore,
            IPalworldSavDecompressor decompressor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var settings = (await settingsStore.GetAsync(cancellationToken)).SaveGame;
            if (string.IsNullOrWhiteSpace(settings.WorldDirectory))
                throw new ArgumentException("尚未配置世界存档目录。");
            var levelPath = Path.Combine(Path.GetFullPath(settings.WorldDirectory), "Level.sav");
            if (!File.Exists(levelPath))
                throw new FileNotFoundException("世界存档目录中不存在 Level.sav。", levelPath);
            await using var stream = new FileStream(levelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true);
            return Results.Ok(Envelope(context, await decompressor.InspectAsync(stream, cancellationToken)));
        });

        group.MapPost("/save-index/parse", async (
            ISaveIndexingService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var result = await service.TriggerAsync("manual", cancellationToken);
            if (!result.Started)
                return Results.Conflict(new ApiErrorEnvelope(new ApiError(result.Code, result.Message, new { result.JobId }), context.TraceIdentifier));
            return Results.Accepted("/api/v1/save-index/status", Envelope(context, result));
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/save-index/cancel", async (
            ISaveIndexingService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var cancelled = await service.CancelAsync(cancellationToken);
            return cancelled
                ? Results.Ok(Envelope(context, new { cancelled = true }))
                : Results.Conflict(new ApiErrorEnvelope(new ApiError("SAVE_PARSE_NOT_RUNNING", "当前没有可取消的解析任务。"), context.TraceIdentifier));
        }).RequireAuthorization("Operator").AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/save-index/history", async (
            ISaveIndexRepository repository,
            HttpContext context,
            CancellationToken cancellationToken) =>
            Results.Ok(Envelope(context, await repository.GetHistoryAsync(cancellationToken))));

        return endpoints;
    }

    private static ApiResponse<T> Envelope<T>(HttpContext context, T data)
        => new(data, context.TraceIdentifier, []);
}
