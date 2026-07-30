using MemoryCleaner.Config;

namespace MemoryCleaner.Scheduler;

/// <summary>
/// 触发器接口：给定当前时刻与配置，判断是否应触发一次清理。
/// 触发器自身维护内部状态（如上次触发时间）。
/// </summary>
internal interface ICleanTrigger
{
    string Name { get; }
    /// <summary>本次 Tick 是否应触发。</summary>
    bool ShouldFire(DateTime now, AppConfig cfg, MemorySnapshotProvider snapshot);
    void Reset() { }
}

/// <summary>提供内存快照，便于触发器判断阈值（包装成可替换的委托以便测试）。</summary>
internal sealed class MemorySnapshotProvider
{
    private readonly Func<Core.MemorySnapshot> _provider;
    public MemorySnapshotProvider(Func<Core.MemorySnapshot> provider) => _provider = provider;
    public Core.MemorySnapshot Get() => _provider();
}
