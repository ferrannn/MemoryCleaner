using MemoryCleaner.Config;

namespace MemoryCleaner.UI;

/// <summary>
/// 设置窗口：配置清理方式与三种触发器。
/// </summary>
internal sealed class SettingsForm : Form
{
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
        ClientSize = new Size(430, 470);
        Font = new Font("Microsoft YaHei UI", 9f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            AutoScroll = true,
            Padding = new Padding(12),
        };
        Controls.Add(root);

        // ---- 清理方式 ----
        var grpMethod = Group("清理方式");
        chkWorkingSet = Check("清空工作集（Working Set）", config.CleanWorkingSet);
        chkSystemCache = Check("清空系统缓存/待机列表（需管理员）", config.CleanSystemCache);
        chkSystemCache.Enabled = Core.MemoryInfoProvider.IsElevated();
        chkKill = Check("结束高占用进程（默认关闭，谨慎开启）", config.KillHighUsageProcesses);
        grpMethod.Controls.Add(chkWorkingSet);
        grpMethod.Controls.Add(chkSystemCache);
        grpMethod.Controls.Add(chkKill);
        root.Controls.Add(grpMethod);

        // ---- 触发方式 ----
        var grpTrigger = Group("自动清理触发（可组合）");

        chkThreshold = Check("内存占用超过阈值时触发", config.ThresholdEnabled);
        numThreshold = Num(config.ThresholdPercent, 50, 99, "%");
        grpTrigger.Controls.Add(Row(chkThreshold, numThreshold));

        chkInterval = Check("每隔固定时间触发", config.IntervalEnabled);
        numInterval = Num(config.IntervalMinutes, 1, 1440, "分钟");
        grpTrigger.Controls.Add(Row(chkInterval, numInterval));

        chkSchedule = Check("在指定时间点触发", config.ScheduleEnabled);
        grpTrigger.Controls.Add(chkSchedule);
        grpTrigger.Controls.Add(Label("每天时间（HH:mm，多个用逗号分隔）："));
        txtDailyTimes = new TextBox { Text = string.Join(",", config.DailyTimes), Dock = DockStyle.Top };
        grpTrigger.Controls.Add(txtDailyTimes);

        chkWeekly = Check("仅每周指定星期触发", config.WeeklyEnabled);
        grpTrigger.Controls.Add(chkWeekly);
        cmbWeekday = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        cmbWeekday.Items.AddRange(new object[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" });
        cmbWeekday.SelectedIndex = (int)config.WeeklyDay;
        grpTrigger.Controls.Add(cmbWeekday);

        root.Controls.Add(grpTrigger);

        // ---- 高级 ----
        var grpAdv = Group("高级 / 安全");
        grpAdv.Controls.Add(Label("高占用进程阈值（MB）："));
        numKillThreshold = Num(config.KillThresholdMB, 256, 32768, "MB");
        grpAdv.Controls.Add(numKillThreshold);
        grpAdv.Controls.Add(Label("进程白名单（进程名，不含.exe，逗号分隔）："));
        txtWhitelist = new TextBox { Text = string.Join(",", config.ProcessWhitelist), Dock = DockStyle.Top };
        grpAdv.Controls.Add(txtWhitelist);

        chkStartup = Check("开机自启", StartupManager.IsEnabled());
        chkNotify = Check("清理后显示通知", config.ShowNotification);
        grpAdv.Controls.Add(chkStartup);
        grpAdv.Controls.Add(chkNotify);
        grpAdv.Controls.Add(Label("两次清理最小间隔（秒）："));
        numMinInterval = Num(config.MinIntervalSeconds, 10, 3600, "秒");
        grpAdv.Controls.Add(numMinInterval);
        root.Controls.Add(grpAdv);

        // ---- 按钮 ----
        var pnlBtn = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 42 };
        var btnOk = new Button { Text = "保存", DialogResult = DialogResult.OK, Width = 90 };
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90 };
        pnlBtn.Controls.Add(btnOk);
        pnlBtn.Controls.Add(btnCancel);
        Controls.Add(pnlBtn);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        btnOk.Click += (_, _) => { if (Apply()) DialogResult = DialogResult.OK; else DialogResult = DialogResult.None; };
    }

    private bool Apply()
    {
        // 校验时间点
        var times = txtDailyTimes.Text.Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var t in times)
        {
            if (!TimeSpan.TryParse(t, out var ts) || ts.Hours > 23 || ts.Minutes > 59)
            {
                MessageBox.Show($"时间点 \"{t}\" 格式不正确，应为 HH:mm（如 12:30）", "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        _config.ShowNotification = chkNotify.Checked;
        _config.MinIntervalSeconds = (int)numMinInterval.Value;
        _config.RunAtStartup = chkStartup.Checked;
        StartupManager.Set(chkStartup.Checked);

        return true;
    }

    // ---------- 控件辅助 ----------
    private static GroupBox Group(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8),
    };

    private static CheckBox Check(string text, bool on) => new()
    {
        Text = text,
        Checked = on,
        Dock = DockStyle.Top,
        AutoSize = true,
    };

    private static Label Label(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        AutoSize = true,
        Margin = new Padding(0, 6, 0, 2),
    };

    private static NumericUpDown Num(int value, int min, int max, string suffix) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = 90,
    };

    private static Control Row(CheckBox chk, NumericUpDown num)
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 28 };
        chk.Dock = DockStyle.Left;
        chk.Width = 220;
        num.Dock = DockStyle.Left;
        p.Controls.Add(num);
        p.Controls.Add(chk);
        return p;
    }
}
