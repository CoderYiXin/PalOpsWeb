using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PalOps.Web.ServerRuntime;

public interface IWindowsProcessTree
{
    int? GetParentProcessId(int processId);
    bool IsProcessAlive(int processId);
    void Kill(int processId);
}

public sealed class WindowsProcessTree : IWindowsProcessTree
{
    private const uint SnapshotProcesses = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public int? GetParentProcessId(int processId)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandleValue) return null;
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry)) return null;
            do
            {
                if (entry.ProcessId == (uint)processId)
                    return entry.ParentProcessId == 0 ? null : checked((int)entry.ParentProcessId);
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));
            return null;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }


    public bool IsProcessAlive(int processId)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Kill(int processId)
    {
        if (!OperatingSystem.IsWindows())
            throw new PalServerRuntimeException(409, "PALSERVER_WINDOWS_REQUIRED", "强制停止仅支持 Windows。");
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(15_000))
                throw new PalServerRuntimeException(504, "PALSERVER_FORCE_STOP_TIMEOUT", "进程树未在规定时间内退出。", processId.ToString());
        }
        catch (ArgumentException)
        {
            // The process exited between identity verification and termination.
        }
        catch (Win32Exception ex)
        {
            throw new PalServerRuntimeException(500, "PALSERVER_FORCE_STOP_FAILED", "Windows 无法终止 PalServer 进程树。", ex.Message, null, ex);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint ThreadCount;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }
}
