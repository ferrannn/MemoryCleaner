namespace MemoryCleaner.Config;

/// <summary>
/// 数据目录解析。配置与历史都存这里。
///
/// 默认写 %AppData%\MemoryCleaner。若 exe 同目录存在 <see cref="PortableMarker"/>，
/// 则改为便携模式，把数据写在 exe 旁边——U 盘携带、免安装分发时不留痕迹。
/// </summary>
internal static class AppPaths
{
    /// <summary>放在 exe 同目录即可启用便携模式的标记文件名。</summary>
    public const string PortableMarker = "portable.txt";

    /// <summary>数据目录（已确保可写）。</summary>
    public static string DataDir { get; }

    /// <summary>当前是否运行在便携模式。</summary>
    public static bool IsPortable { get; }

    /// <summary>
    /// 请求了便携模式但目录不可写（如装在 Program Files），已回退到 %AppData%。
    /// 用于在界面上如实告知，而不是让用户以为便携生效了。
    /// </summary>
    public static bool PortableFallback { get; }

    static AppPaths()
    {
        string roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MemoryCleaner");

        string? exeDir = GetExeDirectory();
        bool wants = exeDir != null && File.Exists(Path.Combine(exeDir, PortableMarker));

        if (wants && IsWritable(exeDir!))
        {
            DataDir = exeDir!;
            IsPortable = true;
            PortableFallback = false;
        }
        else
        {
            DataDir = roaming;
            IsPortable = false;
            PortableFallback = wants; // 想便携但写不进去
        }
    }

    public static string Combine(string fileName) => Path.Combine(DataDir, fileName);

    private static string? GetExeDirectory()
    {
        try
        {
            // 单文件发布下 Assembly.Location 为空，ProcessPath 才是真正的 exe 路径
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                return Path.GetDirectoryName(exe);
            return AppContext.BaseDirectory;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 实测能否写入。装在 Program Files 时目录存在但不可写，
    /// 仅判断目录存在会导致配置静默丢失。
    /// </summary>
    private static bool IsWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".write-probe-{Environment.ProcessId}");
            using (var fs = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None, 1,
                                           FileOptions.DeleteOnClose))
            {
                fs.WriteByte(0);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
