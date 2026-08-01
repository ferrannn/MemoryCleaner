using MemoryCleaner.UI;

namespace MemoryCleaner;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        // 全局异常兜底必须先于任何窗体/线程挂接，保证托盘程序不静默崩溃：
        // 用户看不到窗口，崩溃无感知。日志写入数据目录，便于事后排查。
        Application.ThreadException += (s, e) =>
        {
            CrashLog.Write(e.Exception, "ThreadException");
            // handler 存在即视为已处理，UI 线程异常不会终止进程；仅记录
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            CrashLog.Write(e.ExceptionObject as Exception ?? new Exception("Unknown fatal error"), "UnhandledException");
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            CrashLog.Write(e.Exception, "UnobservedTask");
            e.SetObserved(); // fire-and-forget 任务异常记录后标记已观察，防止进程终止
        };

        // 单实例
        _mutex = new Mutex(true, @"Global\MemoryCleaner_SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new TrayAppContext());

        GC.KeepAlive(_mutex);
    }
}

/// <summary>
/// 崩溃日志：写入数据目录 error.log（追加），自带时间戳与异常链。
/// 绝不抛出——日志写失败只吞掉，避免在异常处理里二次崩溃。
/// </summary>
internal static class CrashLog
{
    private static readonly object Lock = new();

    public static void Write(Exception ex, string source)
    {
        try
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}\n\n";
            lock (Lock)
            {
                var path = Config.AppPaths.Combine("error.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.AppendAllText(path, entry);
            }
        }
        catch
        {
            // 日志写失败不致命，绝不在异常处理里再抛
        }
    }
}
