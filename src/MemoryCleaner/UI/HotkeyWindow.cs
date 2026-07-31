using static MemoryCleaner.Native.NativeMethods;

namespace MemoryCleaner.UI;

/// <summary>
/// 全局热键的宿主。
///
/// RegisterHotKey 需要一个窗口句柄来接收 WM_HOTKEY，而托盘程序基于
/// ApplicationContext 运行、本身没有窗口，因此这里自建一个不可见的
/// 消息窗口专门收热键消息。
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0xC1EA;

    private bool _registered;

    /// <summary>热键被按下。</summary>
    public event Action? Pressed;

    public HotkeyWindow()
    {
        // 消息专用窗口：不显示、不进任务栏、不参与 Z 序
        CreateHandle(new CreateParams { Caption = "MemoryCleanerHotkey" });
    }

    /// <summary>
    /// 重新注册热键。<paramref name="keys"/> 为 <see cref="Keys"/> 组合值
    /// （含 Control/Alt/Shift 修饰位）。
    /// </summary>
    /// <returns>true 表示注册成功；false 表示热键被其他程序占用或组合非法。</returns>
    public bool Apply(bool enabled, Keys keys)
    {
        Unregister();

        if (!enabled) return true;

        uint mods = 0;
        if ((keys & Keys.Control) == Keys.Control) mods |= MOD_CONTROL;
        if ((keys & Keys.Alt) == Keys.Alt) mods |= MOD_ALT;
        if ((keys & Keys.Shift) == Keys.Shift) mods |= MOD_SHIFT;

        uint vk = (uint)(keys & Keys.KeyCode);

        // 不带修饰键会抢占普通按键，坚决不注册
        if (mods == 0 || vk == 0) return false;

        _registered = RegisterHotKey(Handle, HotkeyId, mods | MOD_NOREPEAT, vk);
        return _registered;
    }

    private void Unregister()
    {
        if (!_registered) return;
        try { UnregisterHotKey(Handle, HotkeyId); }
        catch { /* 句柄已失效时忽略 */ }
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            try { Pressed?.Invoke(); }
            catch { /* 订阅者异常不得冒泡进消息循环 */ }
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    /// <summary>把 <see cref="Keys"/> 组合值格式化成 "Ctrl+Alt+M" 形式。</summary>
    public static string Format(Keys keys)
    {
        var key = keys & Keys.KeyCode;
        if (key == Keys.None) return "（未设置）";

        var parts = new List<string>(4);
        if ((keys & Keys.Control) == Keys.Control) parts.Add("Ctrl");
        if ((keys & Keys.Alt) == Keys.Alt) parts.Add("Alt");
        if ((keys & Keys.Shift) == Keys.Shift) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
