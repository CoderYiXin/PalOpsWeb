using System.Net;
using System.Text;
using PalOps.Web.External;
using PalOps.Web.Platform.Readiness;
using PalOps.Web.Versioning;

await VerifyReleaseClientAsync();
await VerifyPlatformServiceAsync();
await VerifyPalDefenderServiceAsync();
Console.WriteLine("PalOps versioning verifier passed.");

static async Task VerifyReleaseClientAsync()
{
    var payload = """
        {
          "tag_name": "v1.2.0",
          "name": "PalOps Web v1.2.0",
          "html_url": "https://github.com/CoderYiXin/PalOpsWeb/releases/tag/v1.2.0",
          "published_at": "2026-07-20T00:00:00Z",
          "body": "Release notes",
          "draft": false,
          "prerelease": false
        }
        """;
    using var httpClient = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, payload)))
    {
        BaseAddress = new Uri("https://api.github.com/")
    };
    var client = new PalOpsReleaseClient(httpClient, new StubVersionProvider("1.1.0"));
    var release = await client.GetLatestStableAsync();
    Assert(release?.TagName == "v1.2.0", "latest Release tag must be parsed");
    Assert(release?.HtmlUrl.EndsWith("/v1.2.0", StringComparison.Ordinal) == true, "approved GitHub URL must be retained");

    var hostile = payload.Replace(
        "https://github.com/CoderYiXin/PalOpsWeb/releases/tag/v1.2.0",
        "https://example.invalid/download");
    using var hostileHttp = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, hostile)))
    {
        BaseAddress = new Uri("https://api.github.com/")
    };
    var hostileRelease = await new PalOpsReleaseClient(hostileHttp, new StubVersionProvider("1.1.0")).GetLatestStableAsync();
    Assert(hostileRelease?.HtmlUrl == string.Empty, "unapproved Release URLs must be removed");

    using var fallbackHttp = new HttpClient(new StubHandler(request =>
    {
        if (request.RequestUri?.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) == true)
            return Json(HttpStatusCode.Forbidden, "{}");
        if (request.RequestUri?.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) == true)
            return Redirect("https://github.com/CoderYiXin/PalOpsWeb/releases/tag/v1.3.1");
        return Json(HttpStatusCode.NotFound, "{}");
    }))
    {
        BaseAddress = new Uri("https://api.github.com/")
    };
    var fallbackRelease = await new PalOpsReleaseClient(fallbackHttp, new StubVersionProvider("1.3.0")).GetLatestStableAsync();
    Assert(fallbackRelease?.TagName == "v1.3.1", "PalOps must resolve the latest tag through the GitHub web fallback");
    Assert(fallbackRelease?.Source == ReleaseSource.GitHubWeb, "PalOps fallback must report the GitHub web source");

    using var defenderFallbackHttp = new HttpClient(new StubHandler(request =>
    {
        if (request.RequestUri?.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) == true)
            return Json(HttpStatusCode.Forbidden, "{}");
        if (request.RequestUri?.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) == true)
            return Redirect("https://github.com/Ultimeit/PalDefender/releases/tag/v1.8.3");
        return Json(HttpStatusCode.NotFound, "{}");
    }))
    {
        BaseAddress = new Uri("https://api.github.com/")
    };
    var defenderFallback = await new PalDefenderReleaseClient(defenderFallbackHttp, new StubVersionProvider("1.3.0")).GetLatestStableAsync();
    Assert(defenderFallback?.TagName == "v1.8.3", "PalDefender must resolve the latest stable tag through the GitHub web fallback");
    Assert(defenderFallback?.Source == ReleaseSource.GitHubWeb, "PalDefender fallback must report the GitHub web source");
}

static async Task VerifyPlatformServiceAsync()
{
    var pausedClient = new CountingReleaseClient(_ => Task.FromResult<PalOpsReleaseInfo?>(Release("v1.2.0")));
    var paused = await CreateService("1.1.0", pausedClient).CheckAsync(false);
    Assert(pausedClient.Calls == 1, "Automatic checks must call GitHub before setup");
    Assert(paused.ComparisonStatus == PlatformVersionComparisonStatuses.UpdateAvailable, "PalOps remote version comparison must work before setup");

    var update = await CreateService("1.1.0", Release("v1.2.0")).CheckAsync(false);
    Assert(update.ComparisonStatus == PlatformVersionComparisonStatuses.UpdateAvailable, "1.2.0 must update 1.1.0");
    Assert(update.UpdateAvailable, "update-available must set UpdateAvailable");

    var ahead = await CreateService("1.2.0", Release("v1.1.0")).CheckAsync(false);
    Assert(ahead.ComparisonStatus == PlatformVersionComparisonStatuses.Ahead, "newer local build must be ahead");

    var equal = await CreateService("1.1.0+sha.abc", Release("v1.1.0")).CheckAsync(false);
    Assert(equal.ComparisonStatus == PlatformVersionComparisonStatuses.UpToDate, "build metadata must not change comparison");

    var unavailableClient = new CountingReleaseClient(_ => throw new HttpRequestException("offline"));
    var unavailable = await CreateService("1.1.0", unavailableClient).CheckAsync(false);
    Assert(unavailable.ComparisonStatus == PlatformVersionComparisonStatuses.Unavailable, "HTTP failure must degrade to unavailable");
    Assert(unavailable.CurrentVersion == "1.1.0", "local version must survive GitHub failure");

    var interruptedClient = new CountingReleaseClient(_ => throw new IOException("stream interrupted"));
    var interrupted = await CreateService("1.1.0", interruptedClient).CheckAsync(false);
    Assert(interrupted.ComparisonStatus == PlatformVersionComparisonStatuses.Unavailable, "I/O failure must degrade to unavailable");

    var cooldownClient = new CountingReleaseClient(_ => Task.FromResult<PalOpsReleaseInfo?>(Release("v1.2.0")));
    var cooldownClock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-20T00:00:00Z"));
    var cooldownService = CreateService("1.1.0", cooldownClient, cooldownClock);
    await cooldownService.CheckAsync(true);
    await cooldownService.CheckAsync(true);
    Assert(cooldownClient.Calls == 1, "manual checks inside 10 seconds must reuse cache");

    var concurrentClient = new CountingReleaseClient(async cancellationToken =>
    {
        await Task.Delay(50, cancellationToken);
        return Release("v1.2.0");
    });
    var concurrentService = CreateService("1.1.0", concurrentClient);
    await Task.WhenAll(concurrentService.CheckAsync(false), concurrentService.CheckAsync(false));
    Assert(concurrentClient.Calls == 1, "concurrent first loads must issue one GitHub request");

    var events = new RecordingPublisher();
    var eventClock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-20T00:00:00Z"));
    var eventService = CreateService("1.1.0", Release("v1.2.0"), eventClock, events);
    await eventService.CheckAsync(true);
    eventClock.Advance(TimeSpan.FromSeconds(11));
    await eventService.CheckAsync(true);
    Assert(events.Events.Count(item => item.EventType == "palops.update-available") == 1, "one Release tag must publish once per process");
}


static async Task VerifyPalDefenderServiceAsync()
{
    var unconfiguredLocal = new CountingPalDefenderClient("{\"version\":\"1.0.0\"}");
    var unconfiguredRemote = new CountingPalDefenderReleaseClient(_ => Task.FromResult<PalDefenderReleaseInfo?>(DefenderRelease("v1.2.0")));
    var unconfiguredService = CreatePalDefenderService(
        unconfiguredRemote,
        unconfiguredLocal,
        new StubOperationalReadinessGate(OperationalCapability.None));

    var unconfigured = await unconfiguredService.CheckAsync(false);
    Assert(unconfiguredRemote.Calls == 1, "Unconfigured PalDefender must still check the GitHub release");
    Assert(unconfiguredLocal.Calls == 0, "Unconfigured PalDefender must skip the local version endpoint");
    Assert(unconfigured.LatestAvailable, "Unconfigured PalDefender must expose the remote GitHub version");
    Assert(!unconfigured.CurrentAvailable, "Unconfigured PalDefender must not invent a local version");
    Assert(!unconfigured.ComparisonAvailable, "Version comparison requires a configured local PalDefender version");

    var interruptedLocal = new CountingPalDefenderClient("{\"version\":\"1.0.0\"}");
    var interruptedRemote = new CountingPalDefenderReleaseClient(_ => throw new IOException("stream interrupted"));
    var interruptedService = CreatePalDefenderService(
        interruptedRemote,
        interruptedLocal,
        new StubOperationalReadinessGate(OperationalCapability.None));
    var interrupted = await interruptedService.CheckAsync(false);
    Assert(interruptedRemote.Calls == 1, "PalDefender remote checks must attempt GitHub before setup");
    Assert(interruptedLocal.Calls == 0, "PalDefender remote failures must not trigger the unconfigured local endpoint");
    Assert(!interrupted.LatestAvailable, "Interrupted PalDefender GitHub responses must degrade to an unavailable remote version");

    var configuredLocal = new CountingPalDefenderClient("{\"version\":\"1.0.0\"}");
    var configuredRemote = new CountingPalDefenderReleaseClient(_ => Task.FromResult<PalDefenderReleaseInfo?>(DefenderRelease("v1.2.0")));
    var configuredService = CreatePalDefenderService(
        configuredRemote,
        configuredLocal,
        new StubOperationalReadinessGate(OperationalCapability.PalDefender));

    var configured = await configuredService.CheckAsync(false);
    Assert(configuredRemote.Calls == 1, "Configured PalDefender must check the GitHub release");
    Assert(configuredLocal.Calls == 1, "Configured PalDefender must read the local version endpoint");
    Assert(configured.ComparisonAvailable && configured.UpdateAvailable, "Configured PalDefender must compare local and remote versions");
}

static PalDefenderVersionService CreatePalDefenderService(
    IPalDefenderReleaseClient releaseClient,
    IPalDefenderApiClient localClient,
    IOperationalReadinessGate readinessGate) =>
    new(
        releaseClient,
        localClient,
        new RecordingPublisher(),
        new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<PalDefenderVersionService>.Instance,
        readinessGate);

static PalDefenderReleaseInfo DefenderRelease(string tag) => new(
    tag,
    $"PalDefender {tag}",
    $"https://github.com/Ultimeit/PalDefender/releases/tag/{tag}",
    DateTimeOffset.Parse("2026-07-20T00:00:00Z"),
    "Release notes");

static PlatformVersionService CreateService(
    string currentVersion,
    PalOpsReleaseInfo release,
    MutableTimeProvider? timeProvider = null,
    RecordingPublisher? publisher = null) =>
    CreateService(
        currentVersion,
        new CountingReleaseClient(_ => Task.FromResult<PalOpsReleaseInfo?>(release)),
        timeProvider,
        publisher);

static PlatformVersionService CreateService(
    string currentVersion,
    IPalOpsReleaseClient releaseClient,
    MutableTimeProvider? timeProvider = null,
    RecordingPublisher? publisher = null) =>
    new(
        new StubVersionProvider(currentVersion),
        releaseClient,
        publisher ?? new RecordingPublisher(),
        new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
        timeProvider ?? new MutableTimeProvider(DateTimeOffset.Parse("2026-07-20T00:00:00Z")),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<PlatformVersionService>.Instance);

static PalOpsReleaseInfo Release(string tag) => new(
    tag,
    $"PalOps Web {tag}",
    $"https://github.com/CoderYiXin/PalOpsWeb/releases/tag/{tag}",
    DateTimeOffset.Parse("2026-07-20T00:00:00Z"),
    "Release notes");

static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json")
};

static HttpResponseMessage Redirect(string location)
{
    var response = new HttpResponseMessage(HttpStatusCode.Found);
    response.Headers.Location = new Uri(location);
    return response;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responseFactory(request));
}

sealed class StubVersionProvider(string currentVersion) : IApplicationVersionProvider
{
    public ApplicationVersionInfo Get()
    {
        var display = ApplicationVersionProvider.NormalizeDisplayVersion(currentVersion);
        return new("PalOps.Web", display, display, ".NET 10", "Test OS", "X64", DateTimeOffset.UnixEpoch);
    }
}

sealed class CountingReleaseClient(
    Func<CancellationToken, Task<PalOpsReleaseInfo?>> action) : IPalOpsReleaseClient
{
    public int Calls { get; private set; }
    public Task<PalOpsReleaseInfo?> GetLatestStableAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return action(cancellationToken);
    }
}

sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan value) => now += value;
}

sealed class StubOperationalReadinessGate(OperationalCapability available) : IOperationalReadinessGate
{
    public Task<OperationalReadinessSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperationalReadinessSnapshot(available, DateTimeOffset.UtcNow));

    public Task<OperationalReadinessSnapshot> WaitUntilReadyAsync(
        string? workerName,
        OperationalCapability allOf = OperationalCapability.None,
        OperationalCapability anyOf = OperationalCapability.None,
        CancellationToken cancellationToken = default) =>
        GetSnapshotAsync(cancellationToken);
}

sealed class RecordingPublisher : PalOps.Web.Events.IPalOpsEventPublisher
{
    public List<PalOps.Web.Events.PalOpsEvent> Events { get; } = [];
    public ValueTask PublishAsync(PalOps.Web.Events.PalOpsEvent palOpsEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(palOpsEvent);
        return ValueTask.CompletedTask;
    }
}


sealed class CountingPalDefenderReleaseClient(
    Func<CancellationToken, Task<PalDefenderReleaseInfo?>> action) : IPalDefenderReleaseClient
{
    public int Calls { get; private set; }

    public Task<PalDefenderReleaseInfo?> GetLatestStableAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return action(cancellationToken);
    }
}

sealed class CountingPalDefenderClient(string version) : IPalDefenderApiClient
{
    public int Calls { get; private set; }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(version);
    }

    public Task<string> GetVersionAsync(
        PalOps.Web.Settings.PalDefenderConnection connection,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(version);
    }

    public Task<IReadOnlyList<PalDefenderPlayer>> GetKnownPlayersAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task GiveItemsAsync(
        string playerIdentifier,
        IReadOnlyList<ExternalItemGrant> items,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task GivePalsAsync(
        string playerIdentifier,
        IReadOnlyList<ExternalPalGrant> pals,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task GiveProgressionAsync(
        string playerIdentifier,
        ExternalProgressionGrant progression,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task LearnTechnologyAsync(
        string playerIdentifier,
        string technologyId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task BroadcastAsync(string message, bool alert, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task SendPlayerMessageAsync(
        IReadOnlyList<string> playerIdentifiers,
        string sendType,
        string message,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task KickAsync(
        string playerIdentifier,
        string? reason,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task ReloadConfigAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
