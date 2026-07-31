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

    // 手改 config.json 可能写成 8:00，UI 保存的则是 08:00，两种都要认
    private static readonly string[] TimeFormats = { @"h\:mm", @"hh\:mm" };

    /// <summary>上次触发时刻；MinValue 表示尚未建立基线。</summary>
    private DateTime _lastFire = DateTime.MinValue;

    public bool ShouldFire(DateTime now, AppConfig cfg, MemorySnapshotProvider snapshot)
    {
        if (!cfg.ScheduleEnabled) return false;

        if (_lastFire == DateTime.MinValue)
        {
            // 首次求值只登记基线，避免把启动前就已过去的时间点补触发一次
            _lastFire = now;
            return false;
        }

        // 每周模式：只在指定星期几触发
        if (cfg.WeeklyEnabled && now.DayOfWeek != cfg.WeeklyDay) return false;

        foreach (var text in cfg.DailyTimes)
        {
            if (!TimeSpan.TryParseExact(text.Trim(), TimeFormats, null, out var t)) continue;

            // 判定「今天这个时间点是否已过、且晚于上次触发」，而非精确匹配到分钟。
            // 精确匹配一旦错过那一分钟就整天丢失，而错过是常态：系统休眠唤醒、
            // CPU 繁忙导致 Tick 抖动、全屏程序运行期间跳过，都会错过。
            var todayAt = now.Date + t;
            if (todayAt <= now && todayAt > _lastFire)
            {
                _lastFire = now;
                return true;
            }
        }
        return false;
    }

    public void Reset() => _lastFire = DateTime.MinValue;
}
