using System.Text.Json;

namespace MemoryCleaner.Core;

/// <summary>
/// 单次清理的历史记录。
/// </summary>
public sealed record CleanRecord(
    DateTime Time,
    string Trigger,
    long BytesFreed,
    int ProcessesTouched);

/// <summary>
/// 清理历史：环形缓冲保存在内存，附带落盘到数据目录下的 history.json
/// （位置见 <see cref="Config.AppPaths"/>，便携模式下在 exe 旁边），
/// 上限 MaxEntries 条，超出丢最旧。读写均容错（损坏即重置）。
/// </summary>
public sealed class CleanHistory
{
    private const int MaxEntries = 200;
    private static readonly string Path_ = Config.AppPaths.Combine("history.json");
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = false };

    private readonly List<CleanRecord> _records = new();
    private readonly object _lock = new();

    public static CleanHistory Load()
    {
        var h = new CleanHistory();
        try
        {
            if (File.Exists(Path_))
            {
                var list = JsonSerializer.Deserialize<List<CleanRecord>>(File.ReadAllText(Path_), Opts);
                if (list != null) h._records.AddRange(list.TakeLast(MaxEntries));
            }
        }
        catch { /* 损坏则从空开始 */ }
        return h;
    }

    public void Add(CleanRecord r)
    {
        lock (_lock)
        {
            _records.Add(r);
            if (_records.Count > MaxEntries)
                _records.RemoveRange(0, _records.Count - MaxEntries);
        }
        Persist();
    }

    /// <summary>返回最新在前的只读快照。</summary>
    public IReadOnlyList<CleanRecord> Snapshot()
    {
        lock (_lock)
        {
            var copy = _records.ToList();
            copy.Reverse();
            return copy;
        }
    }

    public void Clear()
    {
        lock (_lock) _records.Clear();
        Persist();
    }

    /// <summary>累计释放字节数。</summary>
    public long TotalFreed()
    {
        lock (_lock) return _records.Sum(r => r.BytesFreed);
    }

    private void Persist()
    {
        try
        {
            List<CleanRecord> copy;
            lock (_lock) copy = _records.ToList();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(copy, Opts));
        }
        catch { /* 写失败不致命 */ }
    }
}
