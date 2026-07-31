using MemoryCleaner.Config;

namespace MemoryCleaner.UI;

internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "关于 MemoryCleaner";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AppIcon.Apply(this);
        ClientSize = new Size(480, 300); // 高度稍后按内容实算，见构造末尾
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        // 用 TableLayoutPanel 显式分行。Dock 的叠放次序依赖控件添加顺序，
        // 容易出现后加的控件盖住先加的，这里不冒这个险。
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16, 12, 16, 12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // 简介按内容自适应：用百分比高度时，行放不下就会把文字直接裁掉
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 简介
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 数据目录
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));  // 按钮：固定高度，AutoSize 对 Dock=Fill 的按钮算不准
        Controls.Add(layout);

        // 版本号从程序集读取，避免和 csproj 里的版本各写一份而失同步
        var v = Core.UpdateChecker.CurrentVersion;

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 4, 0, 4),
            Text = $"MemoryCleaner v{v.Major}.{v.Minor}.{v.Build}\n\n"
                 + "轻量级自定义定时内存清理工具\n\n"
                 + "支持：工作集 / 系统缓存 / 高占用进程\n"
                 + "触发：阈值 / 固定间隔 / 定时点\n\n"
                 + "MIT License",
        }, 0, 0);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 8),
            ForeColor = Color.FromArgb(110, 110, 110),
            Font = new Font("Microsoft YaHei UI", 8f),
            Text = DataDirDescription(),
        }, 0, 1);

        var btnOpen = new Button
        {
            Text = "打开数据目录",
            Dock = DockStyle.Fill,
            Height = 34,
            Margin = new Padding(0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(235, 242, 250),
            ForeColor = Color.FromArgb(50, 120, 200),
        };
        btnOpen.FlatAppearance.BorderSize = 0;
        btnOpen.Click += (_, _) => OpenDataDir();
        layout.Controls.Add(btnOpen, 0, 2);

        // 窗口高度按实际内容确定：数据目录路径长度因用户而异（便携模式下
        // 还会变成程序所在路径），写死高度必然在某些机器上裁掉底部按钮。
        ClientSize = new Size(ClientSize.Width, layout.PreferredSize.Height);
    }

    private static void OpenDataDir()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.DataDir,
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打不开不致命
        }
    }

    private static string DataDirDescription()
    {
        if (AppPaths.IsPortable)
            return $"便携模式：配置与历史存放在程序目录\n{AppPaths.DataDir}";

        if (AppPaths.PortableFallback)
            return $"已放置 {AppPaths.PortableMarker}，但程序目录不可写"
                 + $"（如安装在 Program Files），已回退到：\n{AppPaths.DataDir}";

        return $"配置与历史存放在：\n{AppPaths.DataDir}\n"
             + $"在程序目录放置 {AppPaths.PortableMarker} 可启用便携模式";
    }
}
