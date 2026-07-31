namespace MemoryCleaner.UI;

/// <summary>
/// 捕获组合键的只读输入框：聚焦后按下组合键即记录，不接受文本输入。
/// 只认「至少一个修饰键 + 一个普通键」的组合，避免注册出会抢占普通按键的热键。
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    private Keys _value;

    public HotkeyBox(Keys initial)
    {
        ReadOnly = true;
        Cursor = Cursors.Hand;
        TextAlign = HorizontalAlignment.Center;
        BackColor = Color.White;
        Value = initial;
    }

    public Keys Value
    {
        get => _value;
        set
        {
            _value = value;
            Text = HotkeyWindow.Format(value);
        }
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Text = "请按下组合键…";
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Text = HotkeyWindow.Format(_value);
    }

    // 方向键、Tab 等会被上层当作导航键吞掉，必须声明为「我要处理」
    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        var key = e.KeyCode;

        // Esc 清空，允许用户取消热键
        if (key == Keys.Escape)
        {
            Value = Keys.None;
            return;
        }

        // 单独按修饰键时不算数，等它和普通键一起按下
        if (key is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin)
            return;

        var mods = Keys.None;
        if (e.Control) mods |= Keys.Control;
        if (e.Alt) mods |= Keys.Alt;
        if (e.Shift) mods |= Keys.Shift;

        // 没有修饰键的裸键会抢占全局输入，直接拒绝
        if (mods == Keys.None) return;

        _value = mods | key;
        Text = HotkeyWindow.Format(_value);
    }
}
