using MemoryCleaner.Core;

namespace MemoryCleaner.UI;

/// <summary>
/// 内存占用迷你曲线控件：自绘最近 N 个采样点的 sparkline，
/// 背景网格 + 折线 + 当前值标签。用于托盘菜单顶部。
/// </summary>
internal sealed class MemorySparkline : Control
{
    private const int MaxPoints = 60; // 最近 60 个采样（约 5 分钟，按 5s/次）
    private readonly Queue<uint> _points = new();
    private readonly object _lock = new();

    public MemorySparkline()
    {
        DoubleBuffered = true;
        Height = 44;
        Width = 240;
        BackColor = Color.White;
    }

    public void AddSample(uint percent)
    {
        lock (_lock)
        {
            _points.Enqueue(percent);
            while (_points.Count > MaxPoints) _points.Dequeue();
        }
        if (Visible) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        uint[] pts;
        lock (_lock) pts = _points.ToArray();

        var rect = new Rectangle(6, 4, Width - 12, Height - 8);
        if (pts.Length < 2)
        {
            using var f0 = new Font("Microsoft YaHei UI", 8f);
            using var b0 = new SolidBrush(Color.FromArgb(150, 150, 150));
            g.DrawString("内存采集中…", f0, b0, rect.Left + 4, rect.Top + rect.Height / 2 - 8);
            return;
        }

        // 网格线（25/50/75%）
        using (var gridPen = new Pen(Color.FromArgb(235, 235, 235)))
        {
            for (int i = 1; i <= 3; i++)
            {
                int y = rect.Bottom - rect.Height * i / 4;
                g.DrawLine(gridPen, rect.Left, y, rect.Right, y);
            }
        }

        // 折线（最新在右）
        float stepX = (float)rect.Width / (MaxPoints - 1);
        PointF[] line = new PointF[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            float x = rect.Right - (pts.Length - 1 - i) * stepX;
            float y = rect.Bottom - rect.Height * Math.Clamp(pts[i], 0, 100) / 100f;
            line[i] = new PointF(x, y);
        }
        uint last = pts[^1];
        var lineColor = last >= 90 ? Color.FromArgb(220, 60, 60)
                      : last >= 75 ? Color.FromArgb(230, 150, 40)
                      : Color.FromArgb(50, 120, 200);
        using (var pen = new Pen(lineColor, 1.6f))
            g.DrawLines(pen, line);

        // 当前值标签
        using var f = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold);
        using var b = new SolidBrush(lineColor);
        g.DrawString($"{last}%", f, b, rect.Left, rect.Top - 1);
    }
}
