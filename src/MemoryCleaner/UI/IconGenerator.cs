namespace MemoryCleaner.UI;

/// <summary>
/// 动态生成托盘图标：在 16x16 图标上绘制当前内存占用百分比。
/// </summary>
internal static class IconGenerator
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>生成图标（调用方负责 Dispose；Dispose 时一并销毁原生句柄）。</summary>
    public static Icon CreatePercentIcon(uint percent)
    {
        percent = Math.Min(percent, 100);
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            Color bg = percent switch
            {
                >= 90 => Color.FromArgb(220, 60, 60),
                >= 75 => Color.FromArgb(230, 160, 40),
                _ => Color.FromArgb(60, 160, 90),
            };
            using var bgBrush = new SolidBrush(bg);
            g.FillRectangle(bgBrush, 0, 0, 16, 16);

            string text = percent >= 100 ? "99" : percent.ToString();
            float fontSize = text.Length >= 2 ? 7f : 9f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fg = new SolidBrush(Color.White);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, fg, new RectangleF(0, 0, 16, 16), sf);
        }
        IntPtr hIcon = bmp.GetHicon();
        // Clone 出独立图标后销毁原生句柄，避免 GDI 句柄泄漏
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }
}
