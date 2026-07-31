using System.Text.Json.Serialization;

namespace MemoryCleaner.Config;

/// <summary>
/// 应用配置（全部可热重载）。
/// </summary>
public sealed class AppConfig
{
    // ---------- 清理方式开关 ----------
    public bool CleanWorkingSet { get; set; } = true;
    public bool CleanSystemCache { get; set; } = false;       // 需管理员
    /// <summary>
    /// 系统缓存的温和模式：只清空低优先级待机页面，保留前台程序正在依赖的缓存。
    /// 默认开启——完整清空会让游戏等程序的页面被迫从磁盘重读，造成明显卡顿。
    /// </summary>
    public bool SystemCacheGentle { get; set; } = true;
    public bool KillHighUsageProcesses { get; set; } = false; // 默认关闭

    // ---------- 阈值触发 ----------
    public bool ThresholdEnabled { get; set; } = true;
    public int ThresholdPercent { get; set; } = 80;           // 内存占用超过此百分比触发

    // ---------- 固定间隔触发 ----------
    public bool IntervalEnabled { get; set; } = false;
    public int IntervalMinutes { get; set; } = 30;

    // ---------- 每日/每周时间点触发 ----------
    public bool ScheduleEnabled { get; set; } = false;
    /// <summary>每天的时间点，HH:mm 列表。</summary>
    public List<string> DailyTimes { get; set; } = new() { "12:00" };
    /// <summary>每周触发：true=按 Weekday 触发（此时 DailyTimes 作为当天时间）。</summary>
    public bool WeeklyEnabled { get; set; } = false;
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Sunday;

    // ---------- 高占用进程清理参数 ----------
    // 结束进程不可撤销，默认值取得保守：只拦真正失控/泄漏的进程。
    // 游戏、创意软件、IDE 正常占用 2～6 GB，阈值过低会造成误杀。
    public int KillThresholdMB { get; set; } = 8192;          // 单进程工作集超过此值才处理
    public List<string> ProcessWhitelist { get; set; } = new(); // 用户白名单（进程名，不含 .exe）

    // ---------- 行为 ----------
    /// <summary>
    /// 检测到全屏程序（游戏 / 播放器 / 演示模式）时跳过自动清理。
    /// 清理会让进程页面重新缺页读回，在实时渲染时是可感知的卡顿。
    /// 只影响自动触发；手动点「立即清理」始终照常执行。
    /// </summary>
    public bool SkipWhenFullscreen { get; set; } = true;
    public bool RunAtStartup { get; set; } = false;
    public bool ShowNotification { get; set; } = true;
    public bool CheckUpdateOnStartup { get; set; } = true;   // 启动时检查更新
    public int MinIntervalSeconds { get; set; } = 60;         // 两次清理最小间隔（防重入/防过度）

    [JsonIgnore]
    public HashSet<string> WhitelistSet => new(ProcessWhitelist, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 钳制所有数值到安全范围，防止手动编辑 config.json 造成失控清理
    /// （如 ThresholdPercent=0 或 MinIntervalSeconds=0 导致每个 Tick 都全量清理）。
    /// </summary>
    public void Sanitize()
    {
        ThresholdPercent = Math.Clamp(ThresholdPercent, 50, 99);
        IntervalMinutes = Math.Clamp(IntervalMinutes, 1, 1440);
        KillThresholdMB = Math.Clamp(KillThresholdMB, 256, 32768);
        MinIntervalSeconds = Math.Clamp(MinIntervalSeconds, 10, 3600);

        // 过滤非法时间点
        DailyTimes = DailyTimes
            .Where(t => TimeSpan.TryParseExact(t, new[] { @"h\:mm", @"hh\:mm" }, null, out _))
            .DefaultIfEmpty("12:00")
            .ToList();
    }
}
