using MemoryCleaner.Core;

namespace MemoryCleaner.UI;

/// <summary>
/// 更新下载窗口：选择下载源（含实测延迟）、显示进度、可取消。
///
/// 原先下载 68 MB 全程没有任何反馈，用户只能干等；且 GitHub 在部分网络下
/// 直连极慢甚至不通，所以把"选源"和"看进度"放在同一个界面里。
/// </summary>
internal sealed class UpdateDownloadForm : Form
{
    private static readonly Color Accent = Color.FromArgb(46, 117, 222);

    private readonly UpdateChecker.ReleaseAsset _asset;
    private readonly string? _expectedSha256;    // null = 完整性不可用，禁用下载
    private readonly ComboBox _cmbSource;
    private readonly ProgressBar _bar;
    private readonly Label _status;
    private readonly Button _btnStart;
    private readonly Button _btnCancel;

    private readonly int[] _latency;          // 与 DownloadMirrors.All 一一对应，0=尚未测出
    private readonly CancellationTokenSource _probeCts = new();
    private CancellationTokenSource? _cts;
    private bool _downloading;
    private bool _userPickedSource;           // 用户手动选过源，测速完成后就不再自动改动

    /// <summary>下载并写好自替换脚本后为 true，调用方应退出进程让更新生效。</summary>
    public bool ReadyToApply { get; private set; }

    public UpdateDownloadForm(UpdateChecker.ReleaseAsset asset, string? expectedSha256, string versionText)
    {
        _asset = asset;
        _expectedSha256 = expectedSha256;
        _latency = new int[DownloadMirrors.All.Length];

        Text = "下载更新";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AppIcon.Apply(this);
        ClientSize = new Size(460, 250);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9f);

        var title = new Label
        {
            Text = $"{versionText}    {FormatBytes(asset.Size)}",
            Location = new Point(20, 18),
            Size = new Size(420, 22),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
        };
        Controls.Add(title);

        Controls.Add(new Label
        {
            Text = "下载源（括号内为实测延迟，正在测速…）",
            Location = new Point(20, 52),
            Size = new Size(420, 20),
            ForeColor = Color.FromArgb(120, 128, 138),
            Font = new Font("Microsoft YaHei UI", 8f),
            Name = "lblHint",
        });

        _cmbSource = new WheelSafeComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(20, 74),
            Width = 420,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        foreach (var m in DownloadMirrors.All) _cmbSource.Items.Add(Describe(m, 0));
        _cmbSource.SelectedIndex = 0;
        // 只有用户亲自操作下拉框才算数：UpdateItem 内部的还原不应被当成手动选择
        _cmbSource.SelectionChangeCommitted += (_, _) => _userPickedSource = true;
        Controls.Add(_cmbSource);

        _bar = new ProgressBar
        {
            Location = new Point(20, 122),
            Size = new Size(420, 20),
            Style = ProgressBarStyle.Continuous,
        };
        Controls.Add(_bar);

        _status = new Label
        {
            Location = new Point(20, 148),
            Size = new Size(420, 40),
            ForeColor = Color.FromArgb(100, 108, 118),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Text = "选择下载源后点「开始下载」。",
        };
        Controls.Add(_status);

        _btnStart = MakeButton("开始下载", Accent, Color.White, new Point(240, 200));
        _btnStart.Click += async (_, _) => await StartAsync();
        Controls.Add(_btnStart);

        _btnCancel = MakeButton("取消", Color.FromArgb(233, 236, 240), Color.FromArgb(70, 76, 84), new Point(348, 200));
        _btnCancel.Click += (_, _) =>
        {
            if (_downloading) _cts?.Cancel();
            else { DialogResult = DialogResult.Cancel; Close(); }
        };
        Controls.Add(_btnCancel);

        // 完整性不可用（拿不到 .sha256）时禁止下载：绝不下载一个无法校验的 exe
        if (_expectedSha256 == null)
        {
            _btnStart.Enabled = false;
            _cmbSource.Enabled = false;
            _status.Text = "无法获取官方 SHA-256 校验文件，自动更新已禁用。请到发布页手动下载。";
        }

        Shown += (_, _) => _ = ProbeAllAsync();
    }

    // ===================== 延迟探测 =====================

    /// <summary>并发探测全部源；每测出一个就立刻刷新对应条目，不等全部完成。</summary>
    private async Task ProbeAllAsync()
    {
        var tasks = DownloadMirrors.All.Select(async (m, i) =>
        {
            int ms = await DownloadMirrors.MeasureAsync(m, _asset.DownloadUrl, _probeCts.Token);
            _latency[i] = ms;
            OnUiThread(() => UpdateItem(i));
        });

        await Task.WhenAll(tasks);

        OnUiThread(() =>
        {
            if (Controls["lblHint"] is Label hint)
                hint.Text = "下载源（括号内为实测延迟，已测速完成）";
            // 用户已经自己选过就别再动，测速结果只作参考
            if (!_userPickedSource) SelectFastest();
        });
    }

    /// <summary>
    /// 派发到 UI 线程执行。窗口可能在测速返回前就被关掉，
    /// 此时句柄已销毁，BeginInvoke 会抛异常——而这里是 fire-and-forget，
    /// 抛出去无人接管，必须就地吞掉。
    /// </summary>
    private void OnUiThread(Action action)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(action);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void UpdateItem(int index)
    {
        if (index < 0 || index >= _cmbSource.Items.Count) return;
        int selected = _cmbSource.SelectedIndex;
        _cmbSource.Items[index] = Describe(DownloadMirrors.All[index], _latency[index]);
        // 改写条目会重置选中项，改完还原
        if (selected >= 0 && selected < _cmbSource.Items.Count) _cmbSource.SelectedIndex = selected;
    }

    /// <summary>自动选中延迟最低的可用源；全不可用时保持直连不动。</summary>
    private void SelectFastest()
    {
        int best = -1, bestMs = int.MaxValue;
        for (int i = 0; i < _latency.Length; i++)
        {
            if (_latency[i] <= 0) continue;
            if (_latency[i] < bestMs) { bestMs = _latency[i]; best = i; }
        }
        if (best >= 0) _cmbSource.SelectedIndex = best;
    }

    private static string Describe(DownloadMirror m, int ms) => ms switch
    {
        0 => $"{m.Name}（测速中…）",
        DownloadMirrors.Unreachable => $"{m.Name}（不可用）",
        _ => $"{m.Name}（{ms} ms）",
    };

    // ===================== 下载 =====================

    private async Task StartAsync()
    {
        if (_downloading) return;

        // 防御纵深：完整性不可用绝不下发（UI 已禁用按钮，此处再兜一层）
        if (_expectedSha256 == null)
        {
            _status.Text = "无法获取官方 SHA-256 校验文件，自动更新已禁用。请到发布页手动下载。";
            return;
        }

        int idx = _cmbSource.SelectedIndex;
        if (idx < 0) return;
        var mirror = DownloadMirrors.All[idx];

        if (_latency[idx] == DownloadMirrors.Unreachable &&
            MessageBox.Show(this,
                $"「{mirror.Name}」实测不可用，仍要用它下载吗？",
                "下载源不可用", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _downloading = true;
        _cts?.Dispose();          // 上一次失败重试留下的
        _cts = new CancellationTokenSource();
        _cmbSource.Enabled = false;
        _btnStart.Enabled = false;
        _btnCancel.Text = "停止";
        _bar.Value = 0;
        _status.Text = "正在连接…";

        var progress = new Progress<UpdateChecker.DownloadProgress>(p =>
        {
            int pct = p.Percent;
            if (pct >= 0) _bar.Value = Math.Clamp(pct, 0, 100);
            else _bar.Style = ProgressBarStyle.Marquee; // 服务端没给总长度

            _status.Text = pct >= 0
                ? $"{FormatBytes(p.Downloaded)} / {FormatBytes(p.Total)}   ({pct}%)\n"
                  + $"速度 {FormatBytes((long)p.BytesPerSecond)}/s   剩余约 {Eta(p)}"
                : $"已下载 {FormatBytes(p.Downloaded)}   速度 {FormatBytes((long)p.BytesPerSecond)}/s";
        });

        // 按钮禁用态已保证此处非 null（见构造函数完整性判断）
        var outcome = await UpdateChecker.DownloadAndApplyAsync(_asset, _expectedSha256!, mirror, progress, _cts.Token);

        _downloading = false;
        if (outcome.Success)
        {
            ReadyToApply = true;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        // 失败：允许换个源重来，不必重开窗口
        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Value = 0;
        _cmbSource.Enabled = true;
        _btnStart.Enabled = true;
        _btnCancel.Text = "取消";
        _status.Text = $"下载失败：{outcome.Error}\n可换一个下载源重试。";
    }

    // ===================== 杂项 =====================

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 下载中直接关窗会留下半截 .new 文件，先取消
        if (_downloading) _cts?.Cancel();
        // 测速也一并停掉，否则关窗后还要空跑到超时
        _probeCts.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _probeCts.Dispose();
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string Eta(UpdateChecker.DownloadProgress p)
    {
        if (p.BytesPerSecond <= 1 || p.Total <= 0) return "—";
        double secs = (p.Total - p.Downloaded) / p.BytesPerSecond;
        if (secs < 1) return "不到 1 秒";
        if (secs < 60) return $"{secs:F0} 秒";
        return $"{secs / 60:F0} 分 {secs % 60:F0} 秒";
    }

    private static string FormatBytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{b / (double)(1L << 10):F0} KB",
        _ => $"{b} B",
    };

    private static Button MakeButton(string text, Color bg, Color fg, Point at)
    {
        var b = new Button
        {
            Text = text,
            Location = at,
            Size = new Size(92, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = fg,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
