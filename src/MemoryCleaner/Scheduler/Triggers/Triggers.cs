using MemoryCleaner.Config;

namespace MemoryCleaner.Scheduler.Triggers;

/// <summary>固定间隔触发。</summary>
internal sealed class IntervalTrigger : ICleanTrigger
{
    public string Name => "固定间隔";
    private DateTime _lastFire = DateTime.MinValue;

    public bool ShouldFire(DateTime now, AppConfig cfg, MemorySnapshotProvider snapshot)
    {
        if (!cfg.IntervalEnabled) return false;
        if (_lastFire == DateTime.MinValue)
        {
            _lastFire = now; // 首次只登记，避免启动即触发
            return false;
        }
        if ((now - _lastFire).TotalMinutes >= cfg.IntervalMinutes)
        {
            _lastFire = now;
            return true;
        }
        return false;
    }

    public void Reset() => _lastFire = DateTime.MinValue;
}

/// <summary>内存占用阈值触发。</summary>
internal sealed class ThresholdTrigger : ICleanTrigger
{
    public string Name => "阈值触发";

    public bool ShouldFire(DateTime now, AppConfig cfg, MemorySnapshotProvider snapshot)
    {
        if (!cfg.ThresholdEnabled) return false;
        return snapshot.Get().LoadPercent >= cfg.ThresholdPercent;
    }
}

/// <summary>每日/每周时间点触发。</summary>
internal sealed class CronTimeTrigger : ICleanTrigger
{
    public string Name => "定时点";
    private DateTime _lastFireDate = DateTime.MinValue;

    public bool ShouldFire(DateTime now, AppConfig cfg, MemorySnapshotProvider snapshot)
    {
        if (!cfg.ScheduleEnabled) return false;

        // 每周模式：只在指定星期几触发
        if (cfg.WeeklyEnabled && now.DayOfWeek != cfg.WeeklyDay) return false;

        // 同一天只触发一次（任一匹配时间点命中即可）
        if (_lastFireDate.Date == now.Date) return false;

        string hhmm = now.ToString("HH:mm");
        if (cfg.DailyTimes.Contains(hhmm))
        {
            _lastFireDate = now;
            return true;
        }
        return false;
    }

    public void Reset() => _lastFireDate = DateTime.MinValue;
}
