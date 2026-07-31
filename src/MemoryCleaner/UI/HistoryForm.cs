using MemoryCleaner.Core;

namespace MemoryCleaner.UI;

/// <summary>
/// 清理历史查看窗体：列出最近清理记录（时间/触发/释放/触及进程数），含累计统计与清空。
/// </summary>
internal sealed class HistoryForm : Form
{
    private static readonly Color Accent = Color.FromArgb(50, 120, 200);

    private readonly CleanHistory _history;
    private readonly DataGridView _grid;
    private readonly Label _summary;

    public HistoryForm(CleanHistory history)
    {
        _history = history;

        Text = "清理历史";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 460);
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(245, 246, 248);

        _summary = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            ForeColor = Color.FromArgb(60, 60, 60),
        };

        var btnClear = new Button
        {
            Text = "清空历史",
            Width = 96,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(235, 235, 235),
            ForeColor = Color.FromArgb(60, 60, 60),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        btnClear.FlatAppearance.BorderSize = 0;
        btnClear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "确定清空全部清理历史？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _history.Clear();
                Reload();
            }
        };

        var topBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.White };
        topBar.Controls.Add(_summary);
        topBar.Controls.Add(btnClear);
        topBar.Resize += (_, _) => btnClear.Location = new Point(topBar.Width - btnClear.Width - 12, 5);
        // summary 让出按钮空间
        _summary.Width = topBar.Width - btnClear.Width - 24;

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(245, 246, 248),
                ForeColor = Color.FromArgb(60, 60, 60),
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            },
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "时间", FillWeight = 32 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "触发方式", FillWeight = 26 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "释放", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "触及进程", FillWeight = 20 });

        Controls.Add(_grid);
        Controls.Add(topBar);
        topBar.BringToFront();

        Reload();
    }

    private void Reload()
    {
        _grid.Rows.Clear();
        foreach (var r in _history.Snapshot())
        {
            string freed = FormatBytes(r.BytesFreed);
            _grid.Rows.Add(r.Time.ToString("MM-dd HH:mm:ss"), r.Trigger, freed, r.ProcessesTouched);
        }
        var total = _history.TotalFreed();
        int count = _history.Snapshot().Count;
        _summary.Text = $"共 {count} 次清理，累计释放约 {FormatBytes(total)}";
    }

    private static string FormatBytes(long b)
    {
        if (b >= 1024L * 1024 * 1024) return $"{b / 1024.0 / 1024 / 1024:F2} GB";
        if (b >= 1024L * 1024) return $"{b / 1024.0 / 1024:F1} MB";
        if (b >= 1024L) return $"{b / 1024.0:F0} KB";
        return $"{b} B";
    }
}
