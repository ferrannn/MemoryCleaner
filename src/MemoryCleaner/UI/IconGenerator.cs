namespace MemoryCleaner.UI;

/// <summary>
/// 动态生成托盘图标：在 16x16 图标上绘制当前内存占用百分比。
/// </summary>
internal static class IconGenerator
{
    public static Icon CreatePercentIcon(uint percent)
    {
        percent = Math.Min(percent, 100);
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            // 背景圆角块，按占用率着色
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
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, fg, new RectangleF(0, 0, 16, 16), sf);
        }
        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    /// <summary>生成默认的应用 .ico 文件（发布用）。</summary>
    public static void SaveAppIcon(string path)
    {
        using var icon = CreatePercentIcon(0);
        using var fs = File.Create(path);
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(50, 120, 200));
            using var font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fg = new SolidBrush(Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("M", font, fg, new RectangleF(0, 0, 32, 32), sf);
        }
        // 简单写 ICO（32x32 PNG 嵌入）
        using var iconBmp = new Bitmap(bmp);
        IntPtr hIcon = iconBmp.GetHicon();
        using var ic = Icon.FromHandle(hIcon);
        using var fs2 = File.Create(path);
        ic.Save(fs2);
    }
}
