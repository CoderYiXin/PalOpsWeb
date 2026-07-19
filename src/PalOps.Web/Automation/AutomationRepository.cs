using System.Text.Json;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Automation;

public interface IAutomationRepository
{
    Task<IReadOnlyList<AutomationJob>> ListJobsAsync(CancellationToken cancellationToken = default);
    Task<AutomationJob?> FindJobAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertJobAsync(AutomationJob job, CancellationToken cancellationToken = default);
    Task DeleteJobAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AutomationRunRecord>> ListRunsAsync(string? jobId, int limit, CancellationToken cancellationToken = default);
    Task AppendRunAsync(AutomationRunRecord run, int maximumEntries, CancellationToken cancellationToken = default);
}

public sealed class JsonAutomationRepository : IAutomationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _jobsPath;
    private readonly string _runsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonAutomationRepository(IRuntimePathResolver paths)
    {
        var root = paths.ResolveDataPath("automation");
        Directory.CreateDirectory(root);
        _jobsPath = Path.Combine(root, "jobs.json");
        _runsPath = Path.Combine(root, "runs.ndjson");
    }

    public async Task<IReadOnlyList<AutomationJob>> ListJobsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadJobsWithoutLockAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<AutomationJob?> FindJobAsync(string id, CancellationToken cancellationToken = default)
        => (await ListJobsAsync(cancellationToken)).FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public async Task UpsertJobAsync(AutomationJob job, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var jobs = (await ReadJobsWithoutLockAsync(cancellationToken)).ToList();
            var index = jobs.FindIndex(x => x.Id.Equals(job.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) jobs[index] = job; else jobs.Add(job);
            await WriteJobsWithoutLockAsync(jobs.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteJobAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var jobs = (await ReadJobsWithoutLockAsync(cancellationToken))
                .Where(x => !x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            await WriteJobsWithoutLockAsync(jobs, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<AutomationRunRecord>> ListRunsAsync(string? jobId, int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 1000);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_runsPath)) return [];
            var lines = await File.ReadAllLinesAsync(_runsPath, cancellationToken);
            var records = new List<AutomationRunRecord>(Math.Min(lines.Length, limit));
            for (var index = lines.Length - 1; index >= 0 && records.Count < limit; index--)
            {
                if (string.IsNullOrWhiteSpace(lines[index])) continue;
                try
                {
                    var record = JsonSerializer.Deserialize<AutomationRunRecord>(lines[index], JsonOptions);
                    if (record is null) continue;
                    if (!string.IsNullOrWhiteSpace(jobId) && !record.JobId.Equals(jobId, StringComparison.OrdinalIgnoreCase)) continue;
                    records.Add(record);
                }
                catch (JsonException) { }
            }
            return records;
        }
        finally { _gate.Release(); }
    }

    public async Task AppendRunAsync(AutomationRunRecord run, int maximumEntries, CancellationToken cancellationToken = default)
    {
        maximumEntries = Math.Clamp(maximumEntries, 20, 5000);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_runsPath, JsonSerializer.Serialize(run, JsonOptions) + Environment.NewLine, cancellationToken);
            var lines = await File.ReadAllLinesAsync(_runsPath, cancellationToken);
            if (lines.Length > maximumEntries)
                await File.WriteAllLinesAsync(_runsPath, lines[^maximumEntries..], cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<AutomationJob>> ReadJobsWithoutLockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_jobsPath)) return [];
        await using var stream = new FileStream(_jobsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, true);
        return await JsonSerializer.DeserializeAsync<List<AutomationJob>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteJobsWithoutLockAsync(IReadOnlyList<AutomationJob> jobs, CancellationToken cancellationToken)
    {
        var temporary = _jobsPath + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
        {
            await JsonSerializer.SerializeAsync(stream, jobs, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }
        File.Move(temporary, _jobsPath, true);
    }
}
