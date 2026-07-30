using MemoryCleaner.Config;

namespace MemoryCleaner.UI;

/// <summary>
/// 设置窗口：分区卡片式布局，固定底部按钮栏，杜绝遮挡/截断。
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly Color Accent = Color.FromArgb(50, 120, 200);
    private static readonly Color BgPage = Color.FromArgb(245, 246, 248);
    private static readonly Color BgCard = Color.White;

    private readonly AppConfig _config;
    public AppConfig Result => _config;

    private readonly CheckBox chkWorkingSet;
    private readonly CheckBox chkSystemCache;
    private readonly CheckBox chkKill;

    private readonly CheckBox chkThreshold;
    private readonly NumericUpDown numThreshold;
    private readonly CheckBox chkInterval;
    private readonly NumericUpDown numInterval;
    private readonly CheckBox chkSchedule;
    private readonly TextBox txtDailyTimes;
    private readonly CheckBox chkWeekly;
    private readonly ComboBox cmbWeekday;

    private readonly NumericUpDown numKillThreshold;
    private readonly TextBox txtWhitelist;

    private readonly CheckBox chkAutoUpdate;
    private readonly CheckBox chkStartup;
    private readonly CheckBox chkNotify;
    private readonly NumericUpDown numMinInterval;

    public SettingsForm(AppConfig config)
    {
        _config = config;

        Text = "MemoryCleaner 设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 640);
        BackColor = BgPage;
        Font = new Font("Microsoft YaHei UI", 9f);

        // ===== 底部固定按钮栏（先加，Dock=Bottom，保证永远可见）=====
        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = BgCard };
        var btnOk = new Button
        {
            Text = "保存",
            Width = 100,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        btnOk.FlatAppearance.BorderSize = 0;
        var btnCancel = new Button
        {
            Text = "取消",
            Width = 100,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(230, 230, 230),
            ForeColor = Color.FromArgb(60, 60, 60),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        btnCancel.FlatAppearance.BorderSize = 0;

        bottomBar.Controls.Add(btnOk);
        bottomBar.Controls.Add(btnCancel);
        bottomBar.Resize += (_, _) =>
        {
            btnCancel.Location = new Point(bottomBar.Width - btnCancel.Width - 16, 10);
            btnOk.Location = new Point(btnCancel.Left - btnOk.Width - 10, 10);
        };
        Controls.Add(bottomBar);

        // ===== 滚动内容区 =====
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BgPage, Padding = new Padding(14) };
        Controls.Add(scroll);
        scroll.BringToFront(); // 让内容填充 bottomBar 之上

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = scroll.ClientSize.Width - 30,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        scroll.Controls.Add(stack);
        scroll.Resize += (_, _) => stack.Width = scroll.ClientSize.Width - 30;

        int CardWidth() => stack.Width;

        // ---------- 卡片：清理方式 ----------
        var cardMethod = Card("清理方式", CardWidth());
        chkWorkingSet = Check("清空工作集（Working Set）— 安全、最常用", config.CleanWorkingSet);
        chkSystemCache = Check("清空系统缓存 / 待机列表（需管理员）", config.CleanSystemCache);
        chkSystemCache.Enabled = Core.MemoryInfoProvider.IsElevated();
        chkKill = Check("结束高占用进程（默认关闭，谨慎）", config.KillHighUsageProcesses);
        AddRows(cardMethod, chkWorkingSet, chkSystemCache, chkKill);
        stack.Controls.Add(cardMethod);

        // ---------- 卡片：自动清理触发 ----------
        var cardTrigger = Card("自动清理触发（可组合）", CardWidth());
        chkThreshold = Check("内存占用超过", config.ThresholdEnabled);
        numThreshold = Num(config.ThresholdPercent, 50, 99);
        cardTrigger.Controls.Add(RowWith(chkThreshold, numThreshold, "%"));

        chkInterval = Check("每隔", config.IntervalEnabled);
        numInterval = Num(config.IntervalMinutes, 1, 1440);
        cardTrigger.Controls.Add(RowWith(chkInterval, numInterval, "分钟清理一次"));

        chkSchedule = Check("在指定时间点触发", config.ScheduleEnabled);
        cardTrigger.Controls.Add(InlineRow(chkSchedule));
        cardTrigger.Controls.Add(Caption("每天时间（HH:mm，多个用逗号分隔）"));
        txtDailyTimes = new TextBox { Text = string.Join(", ", config.DailyTimes), Width = 320 };
        cardTrigger.Controls.Add(txtDailyTimes);

        chkWeekly = Check("仅每周指定星期触发", config.WeeklyEnabled);
        cmbWeekday = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        cmbWeekday.Items.AddRange(new object[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" });
        cmbWeekday.SelectedIndex = (int)config.WeeklyDay;
        cardTrigger.Controls.Add(RowWith(chkWeekly, cmbWeekday, ""));
        stack.Controls.Add(cardTrigger);

        // ---------- 卡片：高占用进程 ----------
        var cardProc = Card("高占用进程（先看清再处理，避免误删）", CardWidth());
        cardProc.Controls.Add(Caption("单进程工作集超过此值才会被处理："));
        numKillThreshold = Num(config.KillThresholdMB, 256, 32768);
        cardProc.Controls.Add(InlineRow(numKillThreshold, "MB"));
        var btnViewProc = new Button
        {
            Text = "查看当前高占用进程…",
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(235, 242, 250),
            ForeColor = Accent,
            AutoSize = true,
        };
        btnViewProc.FlatAppearance.BorderColor = Accent;
        btnViewProc.Click += (_, _) => { using var f = new ProcessListForm(_config); f.ShowDialog(this); };
        cardProc.Controls.Add(btnViewProc);
        cardProc.Controls.Add(Caption("进程白名单（进程名不含 .exe，逗号分隔，绝不结束）"));
        txtWhitelist = new TextBox { Text = string.Join(", ", config.ProcessWhitelist), Width = 320 };
        cardProc.Controls.Add(txtWhitelist);
        stack.Controls.Add(cardProc);

        // ---------- 卡片：行为 ----------
        var cardBehavior = Card("行为", CardWidth());
        chkAutoUpdate = Check("启动时自动检查更新", config.CheckUpdateOnStartup);
        chkStartup = Check("开机自启", StartupManager.IsEnabled());
        chkNotify = Check("清理后显示通知", config.ShowNotification);
        AddRows(cardBehavior, chkAutoUpdate, chkStartup, chkNotify);
        cardBehavior.Controls.Add(Caption("两次清理最小间隔（秒）"));
        numMinInterval = Num(config.MinIntervalSeconds, 10, 3600);
        cardBehavior.Controls.Add(InlineRow(numMinInterval, "秒"));
        stack.Controls.Add(cardBehavior);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        btnOk.Click += (_, _) => DialogResult = Apply() ? DialogResult.OK : DialogResult.None;
        btnCancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
    }

    private bool Apply()
    {
        var times = txtDailyTimes.Text
            .Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] formats = { @"h\:mm", @"hh\:mm", @"H\:mm", @"HH\:mm" };
        foreach (var t in times)
        {
            if (!TimeSpan.TryParseExact(t, formats, null, out _))
            {
                MessageBox.Show(this, $"时间点 \"{t}\" 格式不正确，应为 HH:mm（如 12:30）",
                    "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        _config.CleanWorkingSet = chkWorkingSet.Checked;
        _config.CleanSystemCache = chkSystemCache.Checked;
        _config.KillHighUsageProcesses = chkKill.Checked;

        _config.ThresholdEnabled = chkThreshold.Checked;
        _config.ThresholdPercent = (int)numThreshold.Value;
        _config.IntervalEnabled = chkInterval.Checked;
        _config.IntervalMinutes = (int)numInterval.Value;
        _config.ScheduleEnabled = chkSchedule.Checked;
        _config.DailyTimes = times.Select(t => TimeSpan.Parse(t).ToString(@"hh\:mm")).ToList();
        _config.WeeklyEnabled = chkWeekly.Checked;
        _config.WeeklyDay = (DayOfWeek)cmbWeekday.SelectedIndex;

        _config.KillThresholdMB = (int)numKillThreshold.Value;
        _config.ProcessWhitelist = txtWhitelist.Text
            .Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _config.CheckUpdateOnStartup = chkAutoUpdate.Checked;
        _config.ShowNotification = chkNotify.Checked;
        _config.MinIntervalSeconds = (int)numMinInterval.Value;
        _config.RunAtStartup = chkStartup.Checked;
        StartupManager.Set(chkStartup.Checked);

        return true;
    }

    // ===================== 控件构造辅助 =====================

    private static GroupBox Card(string title, int width) => new()
    {
        Text = "  " + title + "  ",
        Width = width,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = BgCard,
        ForeColor = Color.FromArgb(40, 40, 40),
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
        Padding = new Padding(14, 10, 14, 14),
        Margin = new Padding(0, 0, 0, 12),
    };

    private static CheckBox Check(string text, bool on) => new()
    {
        Text = text,
        Checked = on,
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 9f),
        Margin = new Padding(0, 4, 0, 4),
    };

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.FromArgb(110, 110, 110),
        Font = new Font("Microsoft YaHei UI", 8.5f),
        Margin = new Padding(0, 8, 0, 2),
    };

    private static NumericUpDown Num(int value, int min, int max) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = 90,
        Font = new Font("Microsoft YaHei UI", 9f),
    };

    /// <summary>把若干控件放到同一行（复选框 + 数值框 + 后缀文字）。</summary>
    private static FlowLayoutPanel InlineRow(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 3, 0, 3),
        };
        foreach (var c in controls)
        {
            c.Margin = new Padding(0, 0, 8, 0);
            if (c is Label l) l.Margin = new Padding(0, 6, 0, 0);
            row.Controls.Add(c);
        }
        return row;
    }

    private static FlowLayoutPanel RowWith(Control a, Control b, string suffix)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 3, 0, 3),
        };
        a.Margin = new Padding(0, 0, 8, 0);
        b.Margin = new Padding(0, 0, 8, 0);
        row.Controls.Add(a);
        row.Controls.Add(b);
        if (!string.IsNullOrEmpty(suffix))
        {
            var lbl = new Label
            {
                Text = suffix,
                AutoSize = true,
                ForeColor = Color.FromArgb(110, 110, 110),
                Margin = new Padding(0, 6, 0, 0),
            };
            row.Controls.Add(lbl);
        }
        return row;
    }

    private static FlowLayoutPanel InlineRow(Control a, string suffix)
        => RowWith(a, new Label { Text = suffix, AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110), Margin = new Padding(0, 6, 0, 0) }, "");

    private static void AddRows(GroupBox card, params Control[] rows)
    {
        foreach (var r in rows) card.Controls.Add(r);
    }
}
