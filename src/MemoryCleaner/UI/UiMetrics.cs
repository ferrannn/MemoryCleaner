namespace MemoryCleaner.UI;

/// <summary>
/// 界面共用尺寸。集中一处，避免各窗体各写各的宽度而参差不齐。
/// </summary>
internal static class UiMetrics
{
    /// <summary>
    /// 主窗口统一宽度（设置 / 高占用进程 / 清理历史）。
    /// 三者常被连续打开，宽度不一致时来回切换会有明显的跳动感。
    /// </summary>
    public const int WindowWidth = 660;
}
