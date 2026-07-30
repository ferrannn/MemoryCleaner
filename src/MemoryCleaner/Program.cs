using MemoryCleaner.UI;

namespace MemoryCleaner;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
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
