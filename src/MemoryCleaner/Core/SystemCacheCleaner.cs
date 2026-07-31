using System.Runtime.InteropServices;
using static MemoryCleaner.Native.NativeMethods;

namespace MemoryCleaner.Core;

/// <summary>
/// 清空系统缓存 / 待机列表（Standby List / Modified Page List）。
/// 需要管理员权限 + 提权（SeIncreaseQuotaWorkingSet / SeProfileSingleProcess / SeLockMemory）。
/// 效果类似 RAMMap 的 "Empty Standby List"。
/// </summary>
internal static class SystemCacheCleaner
{
    public static bool IsSupported => Core.MemoryInfoProvider.IsElevated();

    /// <summary>
    /// 启用所需特权。失败会返回缺少的特权名。
    /// </summary>
    private static bool TryEnablePrivileges(out List<string> missing)
    {
        missing = new List<string>();
        string[] required = { "SeIncreaseQuotaPrivilege", "SeProfileSingleProcessPrivilege", "SeLockMemoryPrivilege" };

        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
        {
            missing.Add("OpenProcessToken");
            return false;
        }

        try
        {
            foreach (var priv in required)
            {
                if (!LookupPrivilegeValue(null, priv, out LUID luid))
                {
                    missing.Add(priv);
                    continue;
                }

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                };

                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)
                    || Marshal.GetLastWin32Error() == 1300 /* ERROR_NOT_ALL_ASSIGNED */)
                {
                    missing.Add(priv);
                }
            }
        }
        finally
        {
            CloseHandle(token);
        }

        return missing.Count == 0;
    }

    /// <param name="gentle">
    /// 温和模式：只清空低优先级待机列表，不动 Modified 列表和完整 Standby 列表。
    /// 完整清空会把前台程序（尤其是游戏）正在依赖的缓存页一并丢弃，使其只能从
    /// 磁盘重读，产生硬缺页与明显卡顿；温和模式释放量较小但几乎无感。
    /// </param>
    public static CleanResult Clean(bool gentle = false)
    {
        var notes = new List<string>();
        if (!IsSupported)
            return new CleanResult(0, 0, new[] { "需要管理员权限" });

        if (!TryEnablePrivileges(out var missing))
            notes.Add($"部分特权获取失败: {string.Join(",", missing)}");

        long beforeAvail = (long)Core.MemoryInfoProvider.GetSnapshot().AvailPhysBytes;

        int ok = 0;
        void Run(MEMORY_LIST_COMMAND cmd, string label)
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(ptr, (int)cmd);
                int status = NtSetSystemInformation((int)SYSTEM_INFORMATION_CLASS.SystemMemoryListInformation, ptr, sizeof(int));
                if (status == 0) ok++;
                else notes.Add($"{label} 失败 (NTSTATUS=0x{status:X8})");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        if (gentle)
        {
            Run(MEMORY_LIST_COMMAND.MemoryPurgeLowPriorityStandbyList, "清空低优先级Standby");
        }
        else
        {
            RunFileCacheFlush();
            Run(MEMORY_LIST_COMMAND.MemoryFlushModifiedList, "清空Modified");
            Run(MEMORY_LIST_COMMAND.MemoryPurgeStandbyList, "清空Standby");
        }

        // 清空系统工作集（系统文件缓存）。把上下限都设成 SIZE_T 最大值是
        // 记录在案的"释放系统缓存"约定，等价于 SetSystemFileCacheSize(-1,-1,0)。
        // 属于激进操作，只在完整模式执行。
        void RunFileCacheFlush()
        {
            var info = new SYSTEM_FILECACHE_INFORMATION
            {
                MinimumWorkingSet = nuint.MaxValue,
                MaximumWorkingSet = nuint.MaxValue,
            };

            int size = Marshal.SizeOf<SYSTEM_FILECACHE_INFORMATION>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                int status = NtSetSystemInformation((int)SYSTEM_INFORMATION_CLASS.SystemFileCacheInformationEx, ptr, size);
                if (status == 0) ok++;
                else notes.Add($"清空系统工作集 失败 (NTSTATUS=0x{status:X8})");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        long afterAvail = (long)Core.MemoryInfoProvider.GetSnapshot().AvailPhysBytes;
        long freed = Math.Max(0, afterAvail - beforeAvail);

        return new CleanResult(ok, freed, notes);
    }
}
