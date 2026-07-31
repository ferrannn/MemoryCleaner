using static MemoryCleaner.Native.NativeMethods;

namespace MemoryCleaner.Core;

/// <summary>
/// 判断当前是否有全屏程序占据屏幕（游戏、播放器、演示模式等）。
///
/// 清理内存会让各进程的页面被迫重新缺页读回，对实时渲染的程序是可感知的卡顿。
/// 自动清理挑在这种时刻动手是最糟的时机，因此需要能识别并让路。
/// </summary>
internal static class ForegroundState
{
    /// <summary>
    /// 当前是否有全屏程序在运行，自动清理应当让路。
    /// 判定失败时返回 false —— 宁可照常清理，也不要因为检测不出来就永远不清。
    /// </summary>
    public static bool IsFullscreenAppRunning()
    {
        try
        {
            // S_OK == 0；失败时 pquns 不可信
            if (SHQueryUserNotificationState(out var state) != 0)
                return false;

            // QUNS_BUSY 覆盖无边框全屏——现代游戏多数走这条路径而非 D3D 独占全屏，
            // 只判断 QUNS_RUNNING_D3D_FULL_SCREEN 会对大部分游戏失效。
            // QUNS_NOT_PRESENT（锁屏 / 屏保）刻意不算：那时没人在用电脑，正是清理的好时机。
            return state is QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
                or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE
                or QUERY_USER_NOTIFICATION_STATE.QUNS_APP;
        }
        catch
        {
            return false;
        }
    }
}
