using System.Diagnostics;
using System.Runtime.InteropServices;
using PalOps.Web.Settings;

namespace PalOps.Web.ServerRuntime;

public interface IPalServerMetricsCollector
{
    Task<HostMetricsSnapshot> CollectAsync(
        PalServerProcessSnapshot process,
        bool forceDirectorySize = false,
        CancellationToken cancellationToken = default);
}

public sealed class PalServerMetricsCollector(
    IServerSettingsStore settingsStore,
    ILogger<PalServerMetricsCollector> logger) : IPalServerMetricsCollector
{
    private readonly object _sync = new();
    private ulong? _lastIdle;
    private ulong? _lastKernel;
    private ulong? _lastUser;
    private int? _lastProcessId;
    private DateTimeOffset? _lastProcessAt;
    private TimeSpan? _lastProcessCpu;
    private double? _lastProcessCpuPercent;
    private long? _cachedSaveSize;
    private DateTimeOffset? _saveMeasuredAt;

    public async Task<HostMetricsSnapshot> CollectAsync(
        PalServerProcessSnapshot process,
        bool forceDirectorySize = false,
        CancellationToken cancellationToken = default)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var systemCpu = ReadSystemCpu();
        var (memoryUsed, memoryTotal) = ReadMemory();
        var (processCpu, workingSet, privateMemory) = ReadProcessMetrics(process, capturedAt);

        var settings = await settingsStore.GetAsync(cancellationToken);
        var saveDirectory = settings.SaveGame.WorldDirectory?.Trim() ?? string.Empty;
        long diskFree = 0;
        long diskTotal = 0;

        if (!string.IsNullOrWhiteSpace(saveDirectory))
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(saveDirectory));
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var drive = new DriveInfo(root);
                    diskFree = drive.AvailableFreeSpace;
                    diskTotal = drive.TotalSize;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                logger.LogDebug(ex, "Unable to read disk metrics for {SaveDirectory}.", saveDirectory);
            }

            if (forceDirectorySize || !_saveMeasuredAt.HasValue || capturedAt - _saveMeasuredAt.Value >= TimeSpan.FromMinutes(5))
            {
                var measurement = await MeasureDirectoryAsync(saveDirectory, cancellationToken);
                lock (_sync)
                {
                    _cachedSaveSize = measurement.Bytes;
                    _saveMeasuredAt = capturedAt;
                }
                if (measurement.InaccessibleFileCount > 0)
                    logger.LogDebug("Ignored {Count} inaccessible save files while measuring {SaveDirectory}.",
                        measurement.InaccessibleFileCount, saveDirectory);
            }
        }

        long? cachedSize;
        DateTimeOffset? measuredAt;
        lock (_sync)
        {
            cachedSize = _cachedSaveSize;
            measuredAt = _saveMeasuredAt;
        }

        return new(
            capturedAt,
            systemCpu,
            memoryUsed,
            memoryTotal,
            memoryTotal > 0 ? memoryUsed * 100d / memoryTotal : 0,
            processCpu,
            workingSet,
            privateMemory,
            diskFree,
            diskTotal,
            cachedSize,
            measuredAt);
    }

    private (double? Cpu, long? WorkingSet, long? PrivateMemory) ReadProcessMetrics(
        PalServerProcessSnapshot snapshot,
        DateTimeOffset capturedAt)
    {
        if (!snapshot.ProcessId.HasValue) return (null, null, null);
        try
        {
            using var process = Process.GetProcessById(snapshot.ProcessId.Value);
            process.Refresh();
            var totalCpu = process.TotalProcessorTime;
            double? cpu;
            lock (_sync)
            {
                cpu = _lastProcessId == process.Id ? _lastProcessCpuPercent : null;
                if (_lastProcessId == process.Id && _lastProcessAt.HasValue && _lastProcessCpu.HasValue)
                {
                    var wallMilliseconds = (capturedAt - _lastProcessAt.Value).TotalMilliseconds;
                    if (wallMilliseconds >= 500)
                    {
                        var cpuMilliseconds = Math.Max((totalCpu - _lastProcessCpu.Value).TotalMilliseconds, 0);
                        cpu = Math.Clamp(
                            cpuMilliseconds / wallMilliseconds / Math.Max(Environment.ProcessorCount, 1) * 100d,
                            0d,
                            100d);
                        _lastProcessCpuPercent = cpu;
                        _lastProcessAt = capturedAt;
                        _lastProcessCpu = totalCpu;
                    }
                }
                else
                {
                    _lastProcessId = process.Id;
                    _lastProcessAt = capturedAt;
                    _lastProcessCpu = totalCpu;
                    _lastProcessCpuPercent = null;
                }
            }
            return (cpu, process.WorkingSet64, process.PrivateMemorySize64);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            lock (_sync)
            {
                _lastProcessId = null;
                _lastProcessAt = null;
                _lastProcessCpu = null;
                _lastProcessCpuPercent = null;
            }
            return (null, null, null);
        }
    }

    private double ReadSystemCpu()
    {
        if (!OperatingSystem.IsWindows() || !GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleValue = ToUInt64(idle);
        var kernelValue = ToUInt64(kernel);
        var userValue = ToUInt64(user);
        lock (_sync)
        {
            if (!_lastIdle.HasValue || !_lastKernel.HasValue || !_lastUser.HasValue)
            {
                _lastIdle = idleValue;
                _lastKernel = kernelValue;
                _lastUser = userValue;
                return 0;
            }
            var kernelDelta = kernelValue - _lastKernel.Value;
            var userDelta = userValue - _lastUser.Value;
            var idleDelta = idleValue - _lastIdle.Value;
            _lastIdle = idleValue;
            _lastKernel = kernelValue;
            _lastUser = userValue;
            var total = kernelDelta + userDelta;
            return total == 0 ? 0 : Math.Clamp((total - idleDelta) * 100d / total, 0d, 100d);
        }
    }

    private static (long Used, long Total) ReadMemory()
    {
        if (!OperatingSystem.IsWindows()) return (0, 0);
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0);
        var total = checked((long)status.TotalPhysical);
        var available = checked((long)status.AvailablePhysical);
        return (Math.Max(total - available, 0), total);
    }

    private static async Task<DirectoryMeasurement> MeasureDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return new(null, 0);
        long bytes = 0;
        var inaccessible = 0;
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var child in Directory.EnumerateDirectories(current)) pending.Push(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                inaccessible++;
            }
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { bytes = checked(bytes + new FileInfo(file).Length); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
                    { inaccessible++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                inaccessible++;
            }
            await Task.Yield();
        }
        return new(bytes, inaccessible);
    }

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private sealed record DirectoryMeasurement(long? Bytes, int InaccessibleFileCount);
}
