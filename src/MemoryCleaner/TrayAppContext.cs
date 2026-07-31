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

    private readonly CleanHistory _history;
    private readonly MemorySparkline _sparkline;

    public TrayAppContext()
    {
        _config = ConfigStore.Load();
        _history = CleanHistory.Load();
        _scheduler = new CleanScheduler(_config);
        _scheduler.Cleaned += OnCleaned;
        _scheduler.MemoryUpdated += OnMemoryUpdated;

        var menu = new ContextMenuStrip();

        // 顶部：内存占用迷你曲线（自绘控件宿主）
        _sparkline = new MemorySparkline();
        var sparkHost = new ToolStripControlHost(_sparkline)
        {
            AutoSize = false,
            Size = new Size(240, 48),
            Margin = new Padding(4, 4, 4, 2),
        };
        menu.Items.Add(sparkHost);
        menu.Items.Add(new ToolStripSeparator());

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
            _scheduler.Paused = _paused;
            UpdatePausedText();
        };
        menu.Items.Add(_pauseItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("高占用进程…", null, (_, _) => { using var f = new ProcessListForm(_config); f.ShowDialog(); });
        menu.Items.Add("清理历史…", null, (_, _) => { using var f = new HistoryForm(_history); f.ShowDialog(); });
        menu.Items.Add("设置…", null, (_, _) => OpenSettings());
        menu.Items.Add("检查更新…", null, async (_, _) => await CheckForUpdateAsync(manual: true));
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
        menu.Items.Add("关于", null, (_, _) => { using var f = new AboutForm(); f.ShowDialog(); });
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

        // 启动时自动检查更新（后台，不阻塞）
        if (_config.CheckUpdateOnStartup)
            _ = CheckForUpdateAsync(manual: false);
    }

    private string BuildTooltip()
    {
        var snap = MemoryInfoProvider.GetSnapshot();
        return $"MemoryCleaner\n内存占用 {snap.LoadPercent}%";
    }

    private void UpdatePausedText()
        => _tray.Text = _paused ? "MemoryCleaner（已暂停）" : BuildTooltip();

    private uint _lastShownPercent = uint.MaxValue;

    private void OnMemoryUpdated(MemorySnapshot snap)
    {
        _sparkline.AddSample(snap.LoadPercent);

        // 仅在百分比变化时重建图标，减少 GDI 分配
        if (snap.LoadPercent != _lastShownPercent)
        {
            var old = _tray.Icon;
            _tray.Icon = IconGenerator.CreatePercentIcon(snap.LoadPercent);
            old?.Dispose();
            _lastShownPercent = snap.LoadPercent;
        }
        if (!_paused) _tray.Text = $"MemoryCleaner  内存 {snap.LoadPercent}%";
    }

    private void OnCleaned(CleanSummary s)
    {
        _history.Add(new CleanRecord(DateTime.Now, s.Trigger, s.BytesFreed, s.WorkingSetTouched));

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

    // ===================== 自动更新 =====================

    private async Task CheckForUpdateAsync(bool manual)
    {
        var release = await UpdateChecker.GetLatestReleaseAsync();
        if (release == null)
        {
            if (manual) Info("检查更新", "无法连接 GitHub，请稍后再试。");
            return;
        }

        if (!UpdateChecker.IsNewer(release, out var remote))
        {
            if (manual) Info("检查更新", $"当前已是最新版本 v{UpdateChecker.CurrentVersion}。");
            return;
        }

        // 有新版本
        string notes = string.IsNullOrWhiteSpace(release.Body) ? "" : $"\n\n更新内容：\n{TrimNotes(release.Body)}";
        var r = MessageBox.Show(
            $"发现新版本 {release.TagName}（当前 v{UpdateChecker.CurrentVersion}）。{notes}\n\n是否立即下载并更新？",
            "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (r != DialogResult.Yes) return;

        var asset = UpdateChecker.PickAsset(release);
        if (asset == null)
        {
            Info("更新", "未找到可下载的程序文件，将打开发布页。");
            OpenUrl(release.HtmlUrl);
            return;
        }

        Info("更新", $"正在下载 {asset.Name}（{asset.Size / 1024.0 / 1024.0:F1} MB），完成后将自动重启…");
        var outcome = await UpdateChecker.DownloadAndApplyAsync(asset);
        if (outcome.Success)
        {
            // 脚本已就绪，退出当前进程让更新生效
            ExitThread();
        }
        else
        {
            Info("更新", $"下载失败：{outcome.Error ?? "未知原因"}\n将打开发布页供手动下载。");
            OpenUrl(release.HtmlUrl);
        }
    }

    private static string TrimNotes(string body)
        => body.Length > 300 ? body[..300] + "…" : body;

    private static void Info(string title, string text)
        => MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static void OpenUrl(string url)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    protected override void ExitThreadCore()
    {
        // 先停调度器（等待在途回调/清理收尾），再销毁托盘，避免回调访问已释放的 NotifyIcon
        _scheduler.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
