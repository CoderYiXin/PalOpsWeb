using System.Text.Json;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Platform.Tasks;

public sealed class PlatformTaskRepository : IPlatformTaskRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly int _historyLimit;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<PlatformTaskRepository> _logger;

    public PlatformTaskRepository(
        IRuntimePathResolver paths,
        Microsoft.Extensions.Options.IOptions<PalOps.Web.Configuration.AppRuntimeOptions> options,
        ILogger<PlatformTaskRepository> logger)
    {
        var directory = paths.ResolveDataPath("task-center");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "tasks.json");
        _historyLimit = Math.Clamp(options.Value.TaskCenterHistoryLimit, 100, 10_000);
        _logger = logger;
    }

    public async Task RecoverInterruptedAsync(CancellationToken cancellationToken = default)
    {
        await MutateAsync(document =>
        {
            var now = DateTimeOffset.UtcNow;
            for (var index = 0; index < document.Tasks.Count; index++)
            {
                var task = document.Tasks[index];
                if (task.Status is not (PlatformTaskStatus.Queued or PlatformTaskStatus.Running)) continue;
                document.Tasks[index] = task with
                {
                    Status = PlatformTaskStatus.Interrupted,
                    CompletedAt = now,
                    DurationMilliseconds = task.StartedAt.HasValue
                        ? Math.Max(0, (long)(now - task.StartedAt.Value).TotalMilliseconds)
                        : null,
                    CanCancel = false,
                    CanRetry = false,
                    ErrorCode = "PROCESS_RESTARTED",
                    ErrorDetail = "应用重启时任务尚未结束，已标记为中断。",
                    Message = "应用重启导致任务中断。"
                };
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task UpsertAsync(PlatformTaskRecord record, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var index = document.Tasks.FindIndex(item => item.Id.Equals(record.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) document.Tasks[index] = record;
            else document.Tasks.Add(record);
            var active = document.Tasks
                .Where(static item => !PlatformTaskStatus.IsTerminal(item.Status))
                .OrderByDescending(static item => item.CreatedAt)
                .ToArray();
            var terminal = document.Tasks
                .Where(static item => PlatformTaskStatus.IsTerminal(item.Status))
                .OrderByDescending(static item => item.CreatedAt)
                .Take(Math.Max(0, _historyLimit - active.Length));
            document.Tasks = active.Concat(terminal)
                .OrderByDescending(static item => item.CreatedAt)
                .ToList();
        }, cancellationToken);

    public async Task<PlatformTaskRecord?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var normalized = id.Trim();
        return await ReadAsync(document => document.Tasks.FirstOrDefault(item =>
            item.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)), cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<PlatformTaskRecord>> ListAsync(int limit = 200, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, _historyLimit);
        return ReadAsync<IReadOnlyList<PlatformTaskRecord>>(document => document.Tasks
            .OrderByDescending(static item => item.CreatedAt)
            .Take(take)
            .ToArray(), cancellationToken);
    }

    private async Task<T> ReadAsync<T>(Func<TaskDocument, T> reader, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return reader(await ReadWithoutLockAsync(cancellationToken).ConfigureAwait(false)); }
        finally { _gate.Release(); }
    }

    private async Task MutateAsync(Action<TaskDocument> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadWithoutLockAsync(cancellationToken).ConfigureAwait(false);
            mutation(document);
            await WriteWithoutLockAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<TaskDocument> ReadWithoutLockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new TaskDocument();
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true);
            var document = await JsonSerializer.DeserializeAsync<TaskDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                           ?? new TaskDocument();
            document.Tasks ??= [];
            return document;
        }
        catch (JsonException exception)
        {
            var quarantine = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Move(_path, quarantine, true); }
            catch (Exception moveException) { _logger.LogWarning(moveException, "Failed to quarantine corrupt task-center state."); }
            _logger.LogError(exception, "Task-center state was corrupt and has been reset. Quarantine={Path}", quarantine);
            return new TaskDocument();
        }
    }

    private async Task WriteWithoutLockAsync(TaskDocument document, CancellationToken cancellationToken)
    {
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }

    private sealed class TaskDocument
    {
        public List<PlatformTaskRecord> Tasks { get; set; } = [];
    }
}
