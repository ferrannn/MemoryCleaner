using MemoryCleaner.Config;

namespace MemoryCleaner.UI;

/// <summary>
/// 设置窗口：卡片式布局，固定底部按钮栏。
/// 每张卡片用单列 TableLayoutPanel（行高自适应、列宽充满），
/// 长文本自动换行，绝不溢出/截断。每个选项配主标题 + 灰色副标题说明。
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly Color Accent = Color.FromArgb(46, 117, 222);
    private static readonly Color BgPage = Color.FromArgb(243, 245, 249);
    private static readonly Color BgCard = Color.White;
    private static readonly Color TxtMain = Color.FromArgb(32, 36, 42);
    private static readonly Color TxtSub = Color.FromArgb(120, 128, 138);
    private static readonly Font FTitle = new("Microsoft YaHei UI", 9f);
    private static readonly Font FSub = new("Microsoft YaHei UI", 8f);
    private static readonly Font FCardHead = new("Microsoft YaHei UI", 9.5f, FontStyle.Bold);

    // 卡片内容区宽度（扣除卡片内边距）。窗口 560 - 滚动区内边距 28 - 纵向滚动条 ≈ 515，
    // 卡片总宽须小于此值，否则会多出一条横向滚动条。
    private const int ContentWidth = 476;

    private readonly AppConfig _config;
    public AppConfig Result => _config;

    private readonly CheckBox chkWorkingSet;
    private readonly CheckBox chkSystemCache;
    private readonly CheckBox chkCacheGentle;
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

    private readonly CheckBox chkSkipFullscreen;
    private readonly CheckBox chkHotkey;
    private readonly HotkeyBox boxHotkey;
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
        ClientSize = new Size(560, 720);
        BackColor = BgPage;
        Font = FTitle;

        // ===== 底部固定按钮栏 =====
        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = BgCard };
        var btnOk = MakeButton("保存", Accent, Color.White);
        var btnCancel = MakeButton("取消", Color.FromArgb(233, 236, 240), Color.FromArgb(70, 76, 84));
        bottomBar.Controls.Add(btnOk);
        bottomBar.Controls.Add(btnCancel);
        bottomBar.Resize += (_, _) =>
        {
            btnCancel.Location = new Point(bottomBar.Width - btnCancel.Width - 18, 12);
            btnOk.Location = new Point(btnCancel.Left - btnOk.Width - 10, 12);
        };
        Controls.Add(bottomBar);

        // ===== 滚动内容区 =====
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BgPage, Padding = new Padding(14) };
        Controls.Add(scroll);
        scroll.BringToFront();

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = scroll.ClientSize.Width - 28,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(0),
        };
        scroll.Controls.Add(stack);
        scroll.Resize += (_, _) => stack.Width = scroll.ClientSize.Width - 28;

        // ---------- 卡片：清理方式 ----------
        var cardMethod = Card("清理方式");
        chkWorkingSet = Check("清空工作集（Working Set）", config.CleanWorkingSet);
        AddOpt(cardMethod, chkWorkingSet, "把各进程暂时用不到的物理内存归还给系统。安全无副作用，日常清理首选。白名单进程不受影响。");

        chkSystemCache = Check("清空系统缓存 / 待机列表", config.CleanSystemCache);
        chkSystemCache.Enabled = Core.MemoryInfoProvider.IsElevated();
        AddOpt(cardMethod, chkSystemCache,
            chkSystemCache.Enabled
                ? "清空系统文件缓存与待机内存列表，释放被缓存占用的物理内存。需要管理员权限。"
                : "清空系统文件缓存与待机内存列表。需以管理员身份运行本程序才能启用。");

        chkCacheGentle = Check("温和模式（推荐）", config.SystemCacheGentle);
        chkCacheGentle.Margin = new Padding(20, 0, 0, 2); // 作为上一项的子选项缩进
        AddOpt(cardMethod, chkCacheGentle,
            "只清空低优先级缓存页，保留前台程序正在使用的缓存。释放量略少，但不会造成游戏、"
            + "视频等程序卡顿。关闭后为完整清空，释放更多但可能出现短暂卡顿。");
        chkCacheGentle.Enabled = chkSystemCache.Enabled && chkSystemCache.Checked;
        chkSystemCache.CheckedChanged += (_, _) =>
            chkCacheGentle.Enabled = chkSystemCache.Enabled && chkSystemCache.Checked;

        chkKill = Check("结束高占用进程", config.KillHighUsageProcesses);
        AddOpt(cardMethod, chkKill, "直接结束内存占用超过阈值的进程。默认关闭；关键系统进程与白名单进程绝不结束。");
        chkKill.CheckedChanged += (_, _) =>
        {
            if (chkKill.Checked && !ConfirmEnableKill(this))
                chkKill.Checked = false;
        };
        stack.Controls.Add(cardMethod);

        // ---------- 卡片：自动清理触发 ----------
        var cardTrigger = Card("自动清理触发（可组合）");
        chkThreshold = Check("内存占用超过", config.ThresholdEnabled);
        numThreshold = Num(config.ThresholdPercent, 50, 99);
        AddRow(cardTrigger, Row(chkThreshold, numThreshold, Suffix("% 时自动清理")));

        chkInterval = Check("每隔", config.IntervalEnabled);
        numInterval = Num(config.IntervalMinutes, 1, 1440);
        AddRow(cardTrigger, Row(chkInterval, numInterval, Suffix("分钟清理一次")));

        chkSchedule = Check("在指定时间点清理", config.ScheduleEnabled);
        AddOpt(cardTrigger, chkSchedule, "每天在下面的固定时间触发，可填多个时间点。");
        AddRow(cardTrigger, Caption("每天时间（HH:mm，多个用逗号分隔，如 08:00, 20:30）"));
        txtDailyTimes = new TextBox { Text = string.Join(", ", config.DailyTimes), Width = ContentWidth, Font = FTitle };
        AddRow(cardTrigger, txtDailyTimes);

        chkWeekly = Check("仅每周", config.WeeklyEnabled);
        cmbWeekday = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Font = FTitle };
        cmbWeekday.Items.AddRange(new object[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" });
        cmbWeekday.SelectedIndex = (int)config.WeeklyDay;
        AddRow(cardTrigger, Row(chkWeekly, cmbWeekday, Suffix("的上述时间触发（勾选后按星期限制）")));
        stack.Controls.Add(cardTrigger);

        // ---------- 卡片：高占用进程 ----------
        var cardProc = Card("高占用进程");
        AddRow(cardProc, Caption("单进程工作集超过此值才会被处理（先看清再处理，避免误删）"));
        numKillThreshold = Num(config.KillThresholdMB, 256, 32768);
        AddRow(cardProc, Row(numKillThreshold, Suffix("MB")));
        var btnViewProc = new Button
        {
            Text = "查看当前高占用进程…",
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(233, 241, 253),
            ForeColor = Accent,
            AutoSize = true,
            Font = FTitle,
            Margin = new Padding(0, 8, 0, 4),
            Padding = new Padding(8, 0, 8, 0),
        };
        btnViewProc.FlatAppearance.BorderColor = Accent;
        btnViewProc.Click += (_, _) => { using var f = new ProcessListForm(_config); f.ShowDialog(this); };
        AddRow(cardProc, Row(btnViewProc));
        AddRow(cardProc, Caption("进程白名单（进程名不含 .exe，逗号分隔；既不清理工作集，也不结束）"));
        txtWhitelist = new TextBox { Text = string.Join(", ", config.ProcessWhitelist), Width = ContentWidth, Font = FTitle };
        AddRow(cardProc, txtWhitelist);
        stack.Controls.Add(cardProc);

        // ---------- 卡片：行为 ----------
        var cardBehavior = Card("行为");
        chkSkipFullscreen = Check("玩游戏 / 全屏时不清理（推荐）", config.SkipWhenFullscreen);
        AddOpt(cardBehavior, chkSkipFullscreen,
            "检测到游戏、全屏播放或演示模式时，自动跳过本次清理，避免画面卡顿。"
            + "手动点「立即清理」不受影响。");
        chkHotkey = Check("启用全局热键", config.HotkeyEnabled);
        AddOpt(cardBehavior, chkHotkey,
            "在任何界面按下热键即立即清理一次。点下方输入框后直接按组合键即可修改，Esc 清空。"
            + "建议避开 Ctrl+Alt 系列——中文输入法常年占用该组合。");
        boxHotkey = new HotkeyBox((Keys)config.HotkeyValue) { Width = 180, Font = FTitle };
        AddRow(cardBehavior, Row(boxHotkey));
        boxHotkey.Enabled = chkHotkey.Checked;
        chkHotkey.CheckedChanged += (_, _) => boxHotkey.Enabled = chkHotkey.Checked;

        chkAutoUpdate = Check("启动时自动检查更新", config.CheckUpdateOnStartup);
        AddOpt(cardBehavior, chkAutoUpdate, "程序启动时在后台检查 GitHub 是否有新版本，有则提示一键升级。");
        chkStartup = Check("开机自启", StartupManager.IsEnabled());
        AddOpt(cardBehavior, chkStartup, "登录 Windows 后自动在托盘后台运行本程序。");
        chkNotify = Check("清理后显示通知", config.ShowNotification);
        AddOpt(cardBehavior, chkNotify, "每次自动清理完成后，在托盘弹出气泡显示释放了多少内存。");
        AddRow(cardBehavior, Caption("两次清理的最小间隔（防止清理过于频繁）"));
        numMinInterval = Num(config.MinIntervalSeconds, 10, 3600);
        AddRow(cardBehavior, Row(numMinInterval, Suffix("秒")));
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
        _config.SystemCacheGentle = chkCacheGentle.Checked;
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

        _config.SkipWhenFullscreen = chkSkipFullscreen.Checked;
        // 热键勾选了却没设组合键时自动关掉，避免保存出一个不可能生效的状态
        _config.HotkeyEnabled = chkHotkey.Checked && boxHotkey.Value != Keys.None;
        _config.HotkeyValue = (int)boxHotkey.Value;
        _config.CheckUpdateOnStartup = chkAutoUpdate.Checked;
        _config.ShowNotification = chkNotify.Checked;
        _config.MinIntervalSeconds = (int)numMinInterval.Value;
        _config.RunAtStartup = chkStartup.Checked;
        StartupManager.Set(chkStartup.Checked);

        return true;
    }

    // ===================== 控件构造辅助 =====================

    private static Button MakeButton(string text, Color back, Color fore)
    {
        var b = new Button
        {
            Text = text,
            Width = 100,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = FTitle,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    /// <summary>卡片：白底面板，顶部彩色标题 + 单列自适应表格。</summary>
    private static TableLayoutPanel Card(string title)
    {
        var card = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = BgCard,
            Padding = new Padding(16, 8, 16, 14),
            Margin = new Padding(0, 0, 0, 12),
            Width = ContentWidth + 32, // 卡片总宽（内容 + 左右内边距 32）
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        card.RowCount = 0;

        var header = new Label
        {
            Text = title,
            AutoSize = true,
            Font = FCardHead,
            ForeColor = Accent,
            Margin = new Padding(0, 0, 0, 8),
        };
        AddRow(card, header);
        return card;
    }

    /// <summary>
    /// 开启「结束高占用进程」前的确认。这是全程序唯一不可撤销、且可能导致
    /// 未保存数据丢失的功能，必须让用户明确知情后再启用。
    /// </summary>
    private static bool ConfirmEnableKill(IWin32Window owner)
        => MessageBox.Show(
            owner,
            "「结束高占用进程」会在内存占用超过阈值时直接终止进程，"
            + "被终止的程序不会有保存提示，未保存的内容将丢失。\n\n"
            + "游戏、视频剪辑、虚拟机等程序的正常占用就可能超过阈值。"
            + "建议先在「高占用进程」列表中确认哪些程序会被波及，"
            + "并把需要保护的程序加入白名单。\n\n"
            + "确定要启用吗？",
            "启用前请确认",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>向卡片追加一行控件（行高自适应，按添加顺序排列）。</summary>
    private static void AddRow(TableLayoutPanel card, Control c)
    {
        c.Dock = DockStyle.Top;
        int r = card.RowCount;
        card.RowCount++;
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(c, 0, r);
    }

    /// <summary>一个带说明的设置项：复选框一行 + 缩进灰色副标题一行。</summary>
    private static void AddOpt(TableLayoutPanel card, CheckBox chk, string description)
    {
        chk.Dock = DockStyle.Top;
        chk.AutoSize = true;
        AddRow(card, chk);

        var sub = new Label
        {
            Text = description,
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = TxtSub,
            Font = FSub,
            Padding = new Padding(20, 0, 0, 6),
            MaximumSize = new Size(ContentWidth - 20, 0), // 到宽度自动换行
        };
        AddRow(card, sub);
    }

    /// <summary>一行内水平排列若干控件，空间不足自动换行（防右侧被挤出）。</summary>
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
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

    private static CheckBox Check(string text, bool on) => new()
    {
        Text = text,
        Checked = on,
        AutoSize = true,
        ForeColor = TxtMain,
        Font = FTitle,
        Margin = new Padding(0, 4, 0, 2),
    };

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TxtSub,
        Font = FSub,
        Margin = new Padding(0, 8, 0, 2),
        // 卡片是 AutoSize 的：不限宽的话，长文案会把卡片整体撑宽、
        // 越过滚动区可用宽度，从而冒出一条横向滚动条
        MaximumSize = new Size(ContentWidth, 0),
    };

    private static Label Suffix(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TxtSub,
        Font = FSub,
        Margin = new Padding(0, 6, 0, 0),
    };

    private static NumericUpDown Num(int value, int min, int max) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = 96,
        Font = FTitle,
    };
}
