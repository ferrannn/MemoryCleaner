using System.Diagnostics;
using static MemoryCleaner.Native.NativeMethods;

namespace MemoryCleaner.Core;

/// <summary>
/// 结束内存占用过高的进程。
/// 默认【仅提示不结束】，需用户显式开启；白名单与系统关键进程绝不结束。
/// </summary>
internal static class ProcessKiller
{
    private static readonly HashSet<string> NeverKill = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System",
        "csrss", "smss", "wininit", "winlogon", "lsass", "lsaiso", "services",
        "svchost", "dwm", "explorer", "MemoryCleaner",
    };

    /// <summary>单个进程的工作集大小（字节），读取失败返回 -1。</summary>
    private static long GetWorkingSetBytes(Process proc)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
        if (h == IntPtr.Zero) return -1;
        try
        {
            return GetProcessMemoryInfo(h, out PROCESS_MEMORY_COUNTERS c, (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>())
                ? (long)(ulong)c.WorkingSetSize
                : -1;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>
    /// 找出工作集超过 <paramref name="thresholdBytes"/> 且不在白名单的进程。
    /// </summary>
    /// <param name="kill">为 true 才真正结束，否则仅统计（提示模式）。</param>
    public static CleanResult Run(long thresholdBytes, ISet<string> whitelist, bool kill)
    {
        var notes = new List<string>();
        int affected = 0;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id <= 4) continue;
                string name;
                try { name = proc.ProcessName; }
                catch { continue; }

                if (NeverKill.Contains(name) || whitelist.Contains(name)) continue;

                long ws = GetWorkingSetBytes(proc);
                if (ws < 0 || ws < thresholdBytes) continue;

                if (!kill)
                {
                    notes.Add($"{name}(PID {proc.Id}) 占用 {ws / 1024 / 1024}MB（提示，未结束）");
                    affected++;
                    continue;
                }

                proc.Kill(entireProcessTree: false);
                proc.WaitForExit(3000);
                notes.Add($"已结束 {name}(PID {proc.Id}) 释放约 {ws / 1024 / 1024}MB");
                affected++;
            }
            catch (Exception ex)
            {
                notes.Add($"{proc.SafeName()} 结束失败: {ex.Message}");
            }
            finally
            {
                proc.Dispose();
            }
        }

        return new CleanResult(affected, 0, notes);
    }
}

internal static class ProcessExtensions
{
    public static string SafeName(this Process p)
    {
        try { return p.ProcessName; } catch { return $"PID {p.Id}"; }
    }
}
