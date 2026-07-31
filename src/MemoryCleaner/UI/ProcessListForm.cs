using MemoryCleaner.Config;
using MemoryCleaner.Core;

namespace MemoryCleaner.UI;

/// <summary>
/// 高占用进程列表：显示内存占用最高的进程及其状态，
/// 可勾选加入白名单（绝不结束），让用户"先看清再处理"。
/// </summary>
internal sealed class ProcessListForm : Form
{
    private static readonly Color Accent = Color.FromArgb(50, 120, 200);

    private readonly AppConfig _config;
    private readonly DataGridView _grid;
    private readonly Label _summary;
    private readonly HashSet<string> _whitelist;

    public ProcessListForm(AppConfig config)
    {
        _config = config;
        _whitelist = new HashSet<string>(config.ProcessWhitelist, StringComparer.OrdinalIgnoreCase);

        Text = "高占用进程";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 520);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(245, 246, 248);

        // 摘要文字较长，单行放不下会被截断，固定分两行显示
        _summary = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            ForeColor = Color.FromArgb(90, 90, 90),
        };
        Controls.Add(_summary);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Accent,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            },
        };
        Controls.Add(_grid);
        _grid.BringToFront();

        // 各列权重：状态列需容下最长的“可清理/可结束”，故占比高于进程名之外的其余列
        var colCheck = new DataGridViewCheckBoxColumn { HeaderText = "白名单", Width = 60, FillWeight = 16 };
        var colName = new DataGridViewTextBoxColumn { HeaderText = "进程名", ReadOnly = true, FillWeight = 30 };
        var colPid = new DataGridViewTextBoxColumn { HeaderText = "PID", ReadOnly = true, FillWeight = 14 };
        var colMem = new DataGridViewTextBoxColumn { HeaderText = "内存", ReadOnly = true, FillWeight = 16 };
        var colState = new DataGridViewTextBoxColumn { HeaderText = "状态", ReadOnly = true, FillWeight = 26 };
        _grid.Columns.AddRange(colCheck, colName, colPid, colMem, colState);

        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var name = (string)_grid.Rows[e.RowIndex].Cells[1].Value;
            bool inWhitelist = (bool)(_grid.Rows[e.RowIndex].Cells[0].Value ?? false);
            if (inWhitelist) _whitelist.Add(name); else _whitelist.Remove(name);
            _grid.Rows[e.RowIndex].Cells[4].Value = StateText(name);
        };
        // 让 CheckBox 单击即提交
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.White };
        var btnRefresh = FlatButton("刷新", Color.FromArgb(235, 242, 250), Accent);
        var btnClose = FlatButton("完成", Accent, Color.White);
        bottom.Controls.Add(btnRefresh);
        bottom.Controls.Add(btnClose);
        bottom.Resize += (_, _) =>
        {
            btnClose.Location = new Point(bottom.Width - btnClose.Width - 16, 9);
            btnRefresh.Location = new Point(btnClose.Left - btnRefresh.Width - 10, 9);
        };
        btnRefresh.Click += (_, _) => LoadData();
        btnClose.Click += (_, _) => { SaveWhitelist(); Close(); };
        Controls.Add(bottom);

        LoadData();
    }

    private string StateText(string name)
    {
        if (NeverKillNames.Contains(name)) return "系统保护";
        if (_whitelist.Contains(name)) return "白名单";
        return "可清理/可结束";
    }

    // 与 ProcessKiller.NeverKill 同步的关键进程表（只读副本）
    private static readonly HashSet<string> NeverKillNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System",
        "csrss", "smss", "wininit", "winlogon", "lsass", "lsaiso", "services",
        "svchost", "dwm", "explorer", "MemoryCleaner",
    };

    private void LoadData()
    {
        _grid.Rows.Clear();
        var top = ProcessKiller.GetTopProcesses(_whitelist, 40);
        long threshold = _config.KillThresholdMB * 1024L * 1024L;
        int overThreshold = 0;

        foreach (var p in top)
        {
            string state = StateText(p.Name);
            int idx = _grid.Rows.Add(
                _whitelist.Contains(p.Name),
                p.Name,
                p.Pid,
                FormatMB(p.WorkingSetBytes),
                state);
            var row = _grid.Rows[idx];

            // 受保护行禁止勾选
            if (p.IsProtected)
            {
                row.Cells[0].ReadOnly = true;
                row.DefaultCellStyle.ForeColor = Color.FromArgb(150, 150, 150);
            }
            // 超过结束阈值且可结束 → 高亮提示
            if (p.WorkingSetBytes >= threshold && !p.IsProtected && !p.IsWhitelisted)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 240, 235);
                overThreshold++;
            }
        }

        _summary.Text = $"共 {top.Count} 个进程，{overThreshold} 个超过结束阈值（{_config.KillThresholdMB} MB）。{Environment.NewLine}勾选“白名单”可保护进程不被结束。";
    }

    private void SaveWhitelist()
    {
        _config.ProcessWhitelist = _whitelist.OrderBy(x => x).ToList();
        ConfigStore.Save(_config);
    }

    private static string FormatMB(long bytes) => $"{bytes / 1024.0 / 1024.0:F0} MB";

    private static Button FlatButton(string text, Color bg, Color fg)
    {
        var b = new Button
        {
            Text = text,
            Width = 96,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = fg,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
