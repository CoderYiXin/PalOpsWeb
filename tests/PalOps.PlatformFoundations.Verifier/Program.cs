using Microsoft.Extensions.Caching.Memory;
using PalOps.Web.Contracts;
using PalOps.Web.Platform.Caching;
using PalOps.Web.Platform.Tasks;

await VerifyCacheStampedeProtectionAsync();
VerifyCacheInvalidation();
VerifyResponseEnvelope();
VerifyTaskStatuses();
Console.WriteLine("PalOps platform foundations verifier passed.");

static async Task VerifyCacheStampedeProtectionAsync()
{
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new PlatformMemoryCache(memory);
    var executions = 0;
    var tasks = Enumerable.Range(0, 20).Select(_ => cache.GetOrCreateAsync(
        "players:online",
        TimeSpan.FromMinutes(1),
        async token =>
        {
            Interlocked.Increment(ref executions);
            await Task.Delay(10, token);
            return 42;
        },
        ["players"]));
    var values = await Task.WhenAll(tasks);
    Assert(values.All(value => value == 42), "cache factory returned inconsistent values");
    Assert(executions == 1, "cache stampede protection must execute a shared factory once");
    Assert(cache.GetSnapshot().FactoryExecutions == 1, "cache telemetry must count factory executions");
}

static void VerifyCacheInvalidation()
{
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new PlatformMemoryCache(memory);
    cache.Set("settings:summary", "ready", TimeSpan.FromMinutes(1), ["settings"]);
    Assert(cache.TryGet<string>("settings:summary", out var value) && value == "ready", "cache entry must be readable");
    Assert(cache.RemoveByTag("settings") == 1, "tag invalidation must remove the matching entry");
    Assert(!cache.TryGet<string>("settings:summary", out _), "invalidated cache entry must not remain readable");
}

static void VerifyResponseEnvelope()
{
    var success = new ApiResponse<int>(7, "request-1", []);
    var error = new ApiErrorEnvelope(new ApiError("TEST", "failed"), "request-2");
    Assert(success.Success && success.Message == "OK" && success.Data == 7, "success envelope metadata is invalid");
    Assert(!error.Success && error.Message == "failed" && error.Error.Code == "TEST", "error envelope metadata is invalid");
}

static void VerifyTaskStatuses()
{
    Assert(!PlatformTaskStatus.IsTerminal(PlatformTaskStatus.Running), "running task must not be terminal");
    Assert(PlatformTaskStatus.IsTerminal(PlatformTaskStatus.Completed), "completed task must be terminal");
    Assert(PlatformTaskStatus.IsTerminal(PlatformTaskStatus.TimedOut), "timed-out task must be terminal");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
