using System.Diagnostics;
using static MemoryCleaner.Native.NativeMethods;

namespace MemoryCleaner.Core;

/// <summary>
/// 清理结果。
/// </summary>
public sealed record CleanResult(
    int ProcessesTouched,
    long BytesFreedEstimate,
    IReadOnlyList<string> Notes);

/// <summary>
/// 清空各进程工作集（EmptyWorkingSet）。
/// 跳过系统关键进程与无权限进程，安全、最常用的内存清理手段。
/// </summary>
internal static class WorkingSetCleaner
{
    // 绝对不能碰的系统关键进程
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System",
        "csrss", "smss", "wininit", "winlogon", "lsass", "lsaiso", "services",
        "svchost", "dwm", "explorer", "SystemSettings", "MemoryCleaner",
    };

    /// <param name="whitelist">
    /// 用户白名单：这些进程的工作集同样不清理。清空工作集会让进程的页面被迫重新
    /// 缺页读回，对游戏等实时程序是可感知的卡顿，因此白名单必须在此同样生效，
    /// 而不能只保护「结束进程」。
    /// </param>
    public static CleanResult Clean(ISet<string>? whitelist = null)
    {
        long beforeAvail = (long)Core.MemoryInfoProvider.GetSnapshot().AvailPhysBytes;
        int touched = 0;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id <= 4) continue; // Idle / System
                string name;
                try { name = proc.ProcessName; }
                catch { continue; }
                if (CriticalProcesses.Contains(name)) continue;
                if (whitelist != null && whitelist.Contains(name)) continue;

                IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA, false, proc.Id);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (EmptyWorkingSet(h))
                        touched++;
                }
                finally
                {
                    CloseHandle(h);
                }
            }
            catch
            {
                // 单个进程失败不影响整体（可能已退出 / 受保护）
            }
            finally
            {
                proc.Dispose();
            }
        }

        long afterAvail = (long)Core.MemoryInfoProvider.GetSnapshot().AvailPhysBytes;
        long freed = Math.Max(0, afterAvail - beforeAvail);

        return new CleanResult(touched, freed, Array.Empty<string>());
    }
}
