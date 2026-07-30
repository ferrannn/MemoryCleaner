using MemoryCleaner.Config;
using MemoryCleaner.Core;
using MemoryCleaner.Scheduler.Triggers;

namespace MemoryCleaner.Scheduler;

/// <summary>
/// 清理结果汇总。
/// </summary>
public sealed record CleanSummary(
    string Trigger,
    int WorkingSetTouched,
    long BytesFreed,
    IReadOnlyList<string> Notes);

/// <summary>
/// 统一清理调度引擎：周期性 Tick，综合所有触发器，防重入、限频。
/// </summary>
public sealed class CleanScheduler : IDisposable
{
    private readonly List<ICleanTrigger> _triggers = new()
    {
        new ThresholdTrigger(),
        new IntervalTrigger(),
        new CronTimeTrigger(),
    };

    private readonly MemorySnapshotProvider _snapshot = new(MemoryInfoProvider.GetSnapshot);
    private readonly System.Threading.Timer _timer;
    private readonly object _cleanLock = new();
    private DateTime _lastClean = DateTime.MinValue;
    private bool _isCleaning;

    public AppConfig Config { get; private set; }

    /// <summary>清理完成后回调（在后台线程触发，UI 需 Invoke）。</summary>
    public event Action<CleanSummary>? Cleaned;
    /// <summary>每次 Tick 后回调当前内存占用，用于刷新托盘图标（后台线程）。</summary>
    public event Action<MemorySnapshot>? MemoryUpdated;

    public CleanScheduler(AppConfig config)
    {
        Config = config;
        _timer = new System.Threading.Timer(Tick, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
    }

    public void UpdateConfig(AppConfig config)
    {
        Config = config;
        foreach (var t in _triggers) t.Reset();
    }

    private void Tick(object? state)
    {
        var snap = _snapshot.Get();
        MemoryUpdated?.Invoke(snap);

        var now = DateTime.Now;
        var fired = _triggers.FirstOrDefault(t => t.ShouldFire(now, Config, _snapshot));
        if (fired != null)
            RunClean(fired.Name);
    }

    /// <summary>手动立即清理。</summary>
    public void CleanNow() => RunClean("手动");

    private void RunClean(string triggerName)
    {
        // 防重入
        lock (_cleanLock)
        {
            if (_isCleaning) return;
            if ((DateTime.Now - _lastClean).TotalSeconds < Config.MinIntervalSeconds) return;
            _isCleaning = true;
        }

        try
        {
            long beforeAvail = (long)MemoryInfoProvider.GetSnapshot().AvailPhysBytes;
            var notes = new List<string>();
            int touched = 0;

            var cfg = Config;
            if (cfg.CleanWorkingSet)
            {
                var r = WorkingSetCleaner.Clean();
                touched += r.ProcessesTouched;
                notes.AddRange(r.Notes);
            }

            if (cfg.CleanSystemCache && SystemCacheCleaner.IsSupported)
            {
                var r = SystemCacheCleaner.Clean();
                notes.AddRange(r.Notes);
            }

            if (cfg.KillHighUsageProcesses)
            {
                var r = ProcessKiller.Run(cfg.KillThresholdMB * 1024L * 1024L, cfg.WhitelistSet, kill: true);
                notes.AddRange(r.Notes);
            }

            long afterAvail = (long)MemoryInfoProvider.GetSnapshot().AvailPhysBytes;
            long freed = Math.Max(0, afterAvail - beforeAvail);
            _lastClean = DateTime.Now;

            Cleaned?.Invoke(new CleanSummary(triggerName, touched, freed, notes));
        }
        finally
        {
            lock (_cleanLock) _isCleaning = false;
        }
    }

    public void Dispose() => _timer.Dispose();
}
