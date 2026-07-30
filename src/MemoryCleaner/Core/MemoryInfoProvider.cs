using MemoryCleaner.Native;
using static MemoryCleaner.Native.NativeMethods;

namespace MemoryCleaner.Core;

/// <summary>
/// 内存快照信息。
/// </summary>
public readonly record struct MemorySnapshot(
    uint LoadPercent,
    ulong TotalPhysBytes,
    ulong AvailPhysBytes)
{
    public ulong UsedPhysBytes => TotalPhysBytes - AvailPhysBytes;
}

/// <summary>
/// 读取系统内存占用。纯静态、无状态。
/// </summary>
internal static class MemoryInfoProvider
{
    public static MemorySnapshot GetSnapshot()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
            return new MemorySnapshot(0, 0, 0);

        return new MemorySnapshot(status.dwMemoryLoad, status.ullTotalPhys, status.ullAvailPhys);
    }

    public static bool IsElevated() => IsUserAnAdmin();
}
