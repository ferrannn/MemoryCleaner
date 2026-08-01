using MemoryCleaner.Config;
using MemoryCleaner.Core;
using MemoryCleaner.Scheduler;
using MemoryCleaner.Scheduler.Triggers;
using Xunit;

namespace MemoryCleaner.Core.Tests;

/// <summary>
/// 调度器触发器纯逻辑测试：阈值 / 固定间隔 / 定时点。
/// 不依赖 UI / 网络 / 真实内存快照。
/// </summary>
public class TriggerTests
{
    private static AppConfig NewConfig() => new();

    private static MemorySnapshotProvider Snapshot(uint loadPercent) =>
        new(() => new MemorySnapshot(loadPercent, 100, (uint)(100 - loadPercent)));

    // ---------- ThresholdTrigger（阈值触发） ----------

    [Fact]
    public void Threshold_Enabled_OverThreshold_Fires()
    {
        var cfg = NewConfig();
        cfg.ThresholdEnabled = true;
        cfg.ThresholdPercent = 80;

        var t = new ThresholdTrigger();
        Assert.True(t.ShouldFire(DateTime.Now, cfg, Snapshot(90)));
    }

    [Fact]
    public void Threshold_Enabled_BelowThreshold_NotFire()
    {
        var cfg = NewConfig();
        cfg.ThresholdEnabled = true;
        cfg.ThresholdPercent = 80;

        var t = new ThresholdTrigger();
        Assert.False(t.ShouldFire(DateTime.Now, cfg, Snapshot(50)));
    }

    [Fact]
    public void Threshold_Disabled_NeverFires()
    {
        var cfg = NewConfig();
        cfg.ThresholdEnabled = false;
        cfg.ThresholdPercent = 80;

        var t = new ThresholdTrigger();
        Assert.False(t.ShouldFire(DateTime.Now, cfg, Snapshot(99)));
    }

    // ---------- IntervalTrigger（固定间隔触发） ----------

    [Fact]
    public void Interval_FirstEvaluation_OnlyRegistersBaseline_NotFire()
    {
        var cfg = NewConfig();
        cfg.IntervalEnabled = true;
        cfg.IntervalMinutes = 30;

        var t = new IntervalTrigger();
        // 首次求值只登记基线，避免启动即触发
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 1, 12, 0, 0), cfg, Snapshot(50)));
    }

    [Fact]
    public void Interval_BeforeElapsed_NotFire()
    {
        var cfg = NewConfig();
        cfg.IntervalEnabled = true;
        cfg.IntervalMinutes = 30;

        var t = new IntervalTrigger();
        t.ShouldFire(new DateTime(2026, 8, 1, 12, 0, 0), cfg, Snapshot(50)); // 登记基线
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 1, 12, 29, 0), cfg, Snapshot(50))); // 29 分钟后
    }

    [Fact]
    public void Interval_AfterElapsed_Fires()
    {
        var cfg = NewConfig();
        cfg.IntervalEnabled = true;
        cfg.IntervalMinutes = 30;

        var t = new IntervalTrigger();
        t.ShouldFire(new DateTime(2026, 8, 1, 12, 0, 0), cfg, Snapshot(50)); // 登记基线
        Assert.True(t.ShouldFire(new DateTime(2026, 8, 1, 12, 30, 0), cfg, Snapshot(50))); // 正好 30 分钟
    }

    [Fact]
    public void Interval_Disabled_NeverFires()
    {
        var cfg = NewConfig();
        cfg.IntervalEnabled = false;
        cfg.IntervalMinutes = 30;

        var t = new IntervalTrigger();
        t.ShouldFire(new DateTime(2026, 8, 1, 12, 0, 0), cfg, Snapshot(50));
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 1, 13, 0, 0), cfg, Snapshot(50)));
    }

    [Fact]
    public void Interval_Reset_RebuildsBaseline()
    {
        var cfg = NewConfig();
        cfg.IntervalEnabled = true;
        cfg.IntervalMinutes = 30;

        var t = new IntervalTrigger();
        t.ShouldFire(new DateTime(2026, 8, 1, 12, 0, 0), cfg, Snapshot(50)); // 登记基线
        t.Reset();

        // Reset 后首次求值重新登记基线，不再触发
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 1, 13, 0, 0), cfg, Snapshot(50)));
    }

    // ---------- CronTimeTrigger（定时点触发） ----------

    [Fact]
    public void Cron_FirstEvaluation_RegistersBaseline_NotFire()
    {
        var cfg = NewConfig();
        cfg.ScheduleEnabled = true;
        cfg.DailyTimes = new List<string> { "12:00" };

        var t = new CronTimeTrigger();
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 1, 11, 0, 0), cfg, Snapshot(50)));
    }

    [Fact]
    public void Cron_AtScheduledTime_Fires()
    {
        var cfg = NewConfig();
        cfg.ScheduleEnabled = true;
        cfg.DailyTimes = new List<string> { "12:00" };

        var t = new CronTimeTrigger();
        var baseTime = new DateTime(2026, 8, 1, 11, 59, 0);
        t.ShouldFire(baseTime, cfg, Snapshot(50)); // 登记基线

        // 12:00 后（含 12:00:30）触发
        Assert.True(t.ShouldFire(new DateTime(2026, 8, 1, 12, 0, 30), cfg, Snapshot(50)));
    }

    [Fact]
    public void Cron_BeforeScheduledTime_NotFire()
    {
        var cfg = NewConfig();
        cfg.ScheduleEnabled = true;
        cfg.DailyTimes = new List<string> { "12:00" };

        var t = new CronTimeTrigger();
        var baseTime = new DateTime(2026, 8, 1, 10, 0, 0);
        t.ShouldFire(baseTime, cfg, Snapshot(50)); // 登记基线

        Assert.False(t.ShouldFire(new DateTime(2026, 8, 1, 11, 59, 59), cfg, Snapshot(50)));
    }

    [Fact]
    public void Cron_MissedWindow_LateFires_Once()
    {
        var cfg = NewConfig();
        cfg.ScheduleEnabled = true;
        cfg.DailyTimes = new List<string> { "12:00" };

        var t = new CronTimeTrigger();
        var baseTime = new DateTime(2026, 8, 1, 11, 59, 0);
        t.ShouldFire(baseTime, cfg, Snapshot(50)); // 登记基线

        // 错过 12:00 整（12:30 才求值），今天的时间点已过且晚于上次触发 → 补触发
        Assert.True(t.ShouldFire(new DateTime(2026, 8, 1, 12, 30, 0), cfg, Snapshot(50)));
        // 同一天再次求值 → 不再触发（上次触发已更新）
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 1, 12, 45, 0), cfg, Snapshot(50)));
    }

    [Fact]
    public void Cron_MultipleTimes_EachFiresOnce()
    {
        var cfg = NewConfig();
        cfg.ScheduleEnabled = true;
        cfg.DailyTimes = new List<string> { "09:00", "18:00" };

        var t = new CronTimeTrigger();
        t.ShouldFire(new DateTime(2026, 8, 1, 8, 0, 0), cfg, Snapshot(50)); // 登记基线

        Assert.True(t.ShouldFire(new DateTime(2026, 8, 1, 9, 0, 30), cfg, Snapshot(50))); // 09:00
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 1, 10, 0, 0), cfg, Snapshot(50)));  // 09:00 不重发
        Assert.True(t.ShouldFire(new DateTime(2026, 8, 1, 18, 0, 30), cfg, Snapshot(50))); // 18:00
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 1, 19, 0, 0), cfg, Snapshot(50))); // 18:00 不重发
    }

    [Fact]
    public void Cron_Weekly_OnlyOnConfiguredDay()
    {
        var cfg = NewConfig();
        cfg.ScheduleEnabled = true;
        cfg.WeeklyEnabled = true;
        cfg.WeeklyDay = DayOfWeek.Sunday;
        cfg.DailyTimes = new List<string> { "12:00" };

        var t = new CronTimeTrigger();
        // 基线设在周六（2026-08-01 是周六）11:00，早于后续所有求值时刻
        t.ShouldFire(new DateTime(2026, 8, 1, 11, 0, 0), cfg, Snapshot(50));

        // 周日（2026-08-02）到点 → 触发（指定星期）
        Assert.True(t.ShouldFire(new DateTime(2026, 8, 2, 12, 30, 0), cfg, Snapshot(50)));
        // 周日再求值 → 不重发
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 2, 13, 0, 0), cfg, Snapshot(50)));

        // 周一（2026-08-03）到点 → 不触发（非指定星期）
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 3, 12, 30, 0), cfg, Snapshot(50)));
    }

    [Fact]
    public void Cron_Disabled_NeverFires()
    {
        var cfg = NewConfig();
        cfg.ScheduleEnabled = false;
        cfg.DailyTimes = new List<string> { "12:00" };

        var t = new CronTimeTrigger();
        t.ShouldFire(new DateTime(2026, 8, 1, 11, 0, 0), cfg, Snapshot(50));
        Assert.False(t.ShouldFire(new DateTime(2026, 8, 1, 12, 30, 0), cfg, Snapshot(50)));
    }

    [Fact]
    public void Cron_TimeFormat_HourNoPadding_Parses()
    {
        // 手改 config.json 可能写成 8:00（不带前导零），两种格式都要认
        var cfg = NewConfig();
        cfg.ScheduleEnabled = true;
        cfg.DailyTimes = new List<string> { "8:00" };

        var t = new CronTimeTrigger();
        t.ShouldFire(new DateTime(2026, 8, 1, 7, 0, 0), cfg, Snapshot(50)); // 登记基线

        Assert.True(t.ShouldFire(new DateTime(2026, 8, 1, 8, 30, 0), cfg, Snapshot(50)));
    }
}
