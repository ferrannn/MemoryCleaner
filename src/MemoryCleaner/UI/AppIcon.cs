namespace MemoryCleaner.UI;

/// <summary>
/// 程序图标（app.ico 已作为 Win32 资源嵌进 exe）。
///
/// 窗体不设 Icon 时，WinForms 会用一个通用默认图标，于是 Alt+Tab、
/// 任务视图、任务栏里看到的都不是本程序的图标。所有窗体统一取这里。
/// </summary>
internal static class AppIcon
{
    private static readonly Lazy<Icon?> Cached = new(Load, isThreadSafe: true);

    public static Icon? Value => Cached.Value;

    /// <summary>给窗体套上程序图标；取不到时保持默认，不影响功能。</summary>
    public static void Apply(Form form)
    {
        var icon = Value;
        if (icon != null) form.Icon = icon;
    }

    private static Icon? Load()
    {
        // 首选内嵌的原始 .ico：含多种尺寸，标题栏 16x16 与 Alt+Tab 32x32 各取所需，不会发虚
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("MemoryCleaner.app.ico");
            if (s != null) return new Icon(s);
        }
        catch
        {
            // 落到下面的兜底方案
        }

        // 兜底：从 exe 的 Win32 图标资源里取（只有单一尺寸）
        try
        {
            // 单文件发布下 Assembly.Location 为空，ProcessPath 才指向真正的 exe
            string? exe = Environment.ProcessPath;
            return string.IsNullOrEmpty(exe) ? null : Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            return null;
        }
    }
}
