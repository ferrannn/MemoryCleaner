using MemoryCleaner.Config;
using MemoryCleaner.Core;
using MemoryCleaner.Scheduler;
using MemoryCleaner.UI;

namespace MemoryCleaner;

/// <summary>
/// 托盘 ApplicationContext：无主窗口，常驻右下角。
/// </summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly CleanScheduler _scheduler;
    private AppConfig _config;

    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _cacheItem;
    private bool _paused;

    public TrayAppContext()
    {
        _config = ConfigStore.Load();
        _scheduler = new CleanScheduler(_config);
        _scheduler.Cleaned += OnCleaned;
        _scheduler.MemoryUpdated += OnMemoryUpdated;

        var menu = new ContextMenuStrip();
        menu.Items.Add("立即清理", null, (_, _) => _scheduler.CleanNow());
        menu.Items.Add(new ToolStripSeparator());

        _cacheItem = new ToolStripMenuItem("清理系统缓存（需管理员）")
        {
            Checked = _config.CleanSystemCache,
            CheckOnClick = true,
            Enabled = MemoryInfoProvider.IsElevated(),
        };
        if (!MemoryInfoProvider.IsElevated())
            _cacheItem.ToolTipText = "请以管理员身份运行以启用";
        _cacheItem.CheckedChanged += (_, _) =>
        {
            _config.CleanSystemCache = _cacheItem.Checked && MemoryInfoProvider.IsElevated();
            _cacheItem.Checked = _config.CleanSystemCache;
            SaveAndApply();
        };
        menu.Items.Add(_cacheItem);

        _pauseItem = new ToolStripMenuItem("暂停自动清理") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) =>
        {
            _paused = _pauseItem.Checked;
            UpdatePausedText();
        };
        menu.Items.Add(_pauseItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("设置…", null, (_, _) => OpenSettings());
        menu.Items.Add("开机自启", null, (s, _) =>
        {
            var item = (ToolStripMenuItem)s!;
            _config.RunAtStartup = item.Checked;
            StartupManager.Set(item.Checked);
            SaveAndApply();
        }) ;
        var startupItem = (ToolStripMenuItem)menu.Items[menu.Items.Count - 1];
        startupItem.CheckOnClick = true;
        startupItem.Checked = StartupManager.IsEnabled();

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("关于", null, (_, _) => new AboutForm().ShowDialog());
        menu.Items.Add("退出", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = IconGenerator.CreatePercentIcon(0),
            Text = "MemoryCleaner",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => _scheduler.CleanNow();

        // 应用自启配置（注册表可能与配置不一致，以注册表实际为准）
        _config.RunAtStartup = StartupManager.IsEnabled();
    }

    private string BuildTooltip()
    {
        var snap = MemoryInfoProvider.GetSnapshot();
        return $"MemoryCleaner\n内存占用 {snap.LoadPercent}%";
    }

    private void UpdatePausedText()
        => _tray.Text = _paused ? "MemoryCleaner（已暂停）" : BuildTooltip();

    private void OnMemoryUpdated(MemorySnapshot snap)
    {
        // NotifyIcon 的 Icon/Text 赋值是线程安全的（内部走 SendMessage）
        var old = _tray.Icon;
        _tray.Icon = IconGenerator.CreatePercentIcon(snap.LoadPercent);
        if (old != null) DestroyIconSafe(old);
        if (!_paused) _tray.Text = $"MemoryCleaner  内存 {snap.LoadPercent}%";
    }

    private static void DestroyIconSafe(Icon icon)
    {
        try { icon.Dispose(); } catch { }
    }

    private void OnCleaned(CleanSummary s)
    {
        if (!_config.ShowNotification) return;
        string freed = s.BytesFreed >= 1024 * 1024
            ? $"{s.BytesFreed / 1024.0 / 1024.0:F0} MB"
            : $"{s.BytesFreed / 1024.0:F0} KB";
        _tray.BalloonTipTitle = $"已清理（{s.Trigger}）";
        _tray.BalloonTipText = $"释放约 {freed}，触及 {s.WorkingSetTouched} 个进程";
        _tray.ShowBalloonTip(3000);
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_config);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _config = form.Result;
            SaveAndApply();
        }
    }

    private void SaveAndApply()
    {
        ConfigStore.Save(_config);
        _scheduler.UpdateConfig(_config);
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _scheduler.Dispose();
        base.ExitThreadCore();
    }
}
