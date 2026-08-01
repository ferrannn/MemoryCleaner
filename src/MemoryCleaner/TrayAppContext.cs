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
    private readonly HotkeyWindow _hotkey;

    public TrayAppContext()
    {
        _config = ConfigStore.Load();
        _history = CleanHistory.Load();
        _scheduler = new CleanScheduler(_config);
        _scheduler.Cleaned += OnCleaned;
        _scheduler.MemoryUpdated += OnMemoryUpdated;

        _hotkey = new HotkeyWindow();
        _hotkey.Pressed += () => _scheduler.CleanNow();
        ApplyHotkey(notifyOnFailure: false);

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
        ApplyHotkey(notifyOnFailure: true);
    }

    /// <summary>
    /// 应用热键设置。注册失败通常是被别的程序占用了，
    /// 此时如实告知并把开关关掉，而不是留一个看着启用、实际无效的状态。
    /// </summary>
    private void ApplyHotkey(bool notifyOnFailure)
    {
        if (_hotkey.Apply(_config.HotkeyEnabled, (Keys)_config.HotkeyValue))
            return;

        string combo = HotkeyWindow.Format((Keys)_config.HotkeyValue);
        _config.HotkeyEnabled = false;
        ConfigStore.Save(_config);

        if (notifyOnFailure)
            MessageBox.Show(
                $"热键 {combo} 注册失败，通常是已被其他程序占用。\n\n已关闭全局热键，请换一个组合键再试。",
                "热键注册失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        // 有新版本。取期望 SHA-256：主路径用 GitHub API 返回的资产 digest
        //（GitHub 服务器计算，发布者无法伪造），digest 缺失时回退到 .sha256
        // 侧车资产。取不到就 fail-closed：绝不下载一个无法校验完整性的 exe。
        var (exe, expectedSha256) = await UpdateChecker.PickUpdateAssetsAsync(release);
        if (exe == null)
        {
            Info("更新", "未找到可下载的程序文件，将打开发布页。");
            OpenUrl(release.HtmlUrl);
            return;
        }

        if (expectedSha256 == null)
        {
            Info("更新", "无法获取官方 SHA-256 校验文件，为保证安全已禁用自动更新。\n\n请到发布页手动下载并校验后使用。");
            OpenUrl(release.HtmlUrl);
            return;
        }

        string notes = string.IsNullOrWhiteSpace(release.Body) ? "" : $"\n\n更新内容：\n{TrimNotes(release.Body)}";
        var r = MessageBox.Show(
            $"发现新版本 {release.TagName}（当前 v{UpdateChecker.CurrentVersion}）。{notes}\n\n是否立即下载并更新？",
            "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (r != DialogResult.Yes) return;

        Info("更新", $"正在下载 {exe.Name}（{exe.Size / 1024.0 / 1024.0:F1} MB），完成后将自动重启…");
        // 交给下载窗口：选源（含实测延迟）、显示进度、可取消。
        // 校验和已在此取到并传下去，窗口只做下载 + 哈希比对 + 自替换。
        using var dlg = new UpdateDownloadForm(exe, expectedSha256, $"{release.TagName}（当前 v{UpdateChecker.CurrentVersion}）");
        dlg.ShowDialog();

        if (dlg.ReadyToApply)
        {
            // 脚本已就绪，退出当前进程让更新生效
            ExitThread();
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
        _hotkey.Dispose(); // 注销热键，否则重启后同一组合键会注册失败
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
