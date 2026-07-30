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
        ClientSize = new Size(360, 170);
        Font = new Font("Microsoft YaHei UI", 9f);

        var lbl = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "MemoryCleaner v1.0.0\n\n轻量级自定义定时内存清理工具\n\n支持：工作集 / 系统缓存 / 高占用进程\n触发：阈值 / 固定间隔 / 定时点\n\nMIT License",
        };
        Controls.Add(lbl);
    }
}
