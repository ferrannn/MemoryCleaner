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
    private bool _disposed;

    public AppConfig Config { get; private set; }

    /// <summary>暂停自动清理（手动"立即清理"不受影响）。</summary>
    public bool Paused { get; set; }

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
        lock (_cleanLock)
        {
            Config = config;
            foreach (var t in _triggers) t.Reset();
        }
    }

    private void Tick(object? state)
    {
        try
        {
            var snap = _snapshot.Get();
            SafeInvoke(() => MemoryUpdated?.Invoke(snap));

            // 在锁内完成"限频判断 + 触发器求值"，与 UpdateConfig 的 Reset 互斥，消除并发
            ICleanTrigger? fired = null;
            lock (_cleanLock)
            {
                if (_disposed || _isCleaning) return;
                if ((DateTime.Now - _lastClean).TotalSeconds < Config.MinIntervalSeconds) return;
                if (Paused) return;
                // 全屏程序运行时让路（仅自动触发；CleanNow 不受此限制）
                if (Config.SkipWhenFullscreen && ForegroundState.IsFullscreenAppRunning()) return;

                var now = DateTime.Now;
                fired = _triggers.FirstOrDefault(t => t.ShouldFire(now, Config, _snapshot));
            }
            if (fired != null)
                RunClean(fired.Name);
        }
        catch
        {
            // 后台 Timer 回调绝不允许抛出（否则进程直接终止）
        }
    }

    /// <summary>手动立即清理（受 _disposed 防护，不受 Paused 影响）。</summary>
    public void CleanNow()
    {
        lock (_cleanLock)
        {
            if (_disposed) return;
        }
        RunClean("手动");
    }

    private static void SafeInvoke(Action a)
    {
        try { a(); } catch { /* 订阅者异常不向上传播 */ }
    }

    private void RunClean(string triggerName)
    {
        // 防重入 + 限频：先检查再消费触发器，避免吞掉已锁存的触发状态
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
            // 白名单在此同样生效：清空工作集会让进程重新缺页读回，对游戏等实时程序可感知
            var whitelist = cfg.WhitelistSet;

            if (cfg.CleanWorkingSet)
            {
                var r = WorkingSetCleaner.Clean(whitelist);
                touched += r.ProcessesTouched;
                notes.AddRange(r.Notes);
            }

            if (cfg.CleanSystemCache && SystemCacheCleaner.IsSupported)
            {
                var r = SystemCacheCleaner.Clean(cfg.SystemCacheGentle);
                notes.AddRange(r.Notes);
            }

            if (cfg.KillHighUsageProcesses)
            {
                var r = ProcessKiller.Run(cfg.KillThresholdMB * 1024L * 1024L, whitelist, kill: true);
                notes.AddRange(r.Notes);
            }

            long afterAvail = (long)MemoryInfoProvider.GetSnapshot().AvailPhysBytes;
            long freed = Math.Max(0, afterAvail - beforeAvail);
            _lastClean = DateTime.Now;

            SafeInvoke(() => Cleaned?.Invoke(new CleanSummary(triggerName, touched, freed, notes)));
        }
        catch
        {
            // 清理过程异常不致命，下一次 Tick 继续
        }
        finally
        {
            lock (_cleanLock) _isCleaning = false;
        }
    }

    /// <summary>
    /// 停止调度并短暂等待在途清理完成（避免退出时中断 Kill/清理循环）。
    /// </summary>
    public void Dispose()
    {
        lock (_cleanLock) _disposed = true;

        // 停止计时器并等待当前回调返回
        using var done = new ManualResetEvent(false);
        _timer.Dispose(done);
        done.WaitOne(TimeSpan.FromSeconds(3));

        // 再短暂等待进行中的清理收尾
        for (int i = 0; i < 30; i++)
        {
            lock (_cleanLock)
            {
                if (!_isCleaning) return;
            }
            Thread.Sleep(100);
        }
    }
}
