namespace MemoryCleaner.UI;

/// <summary>
/// 把滚轮事件转交给外层滚动容器。
///
/// NumericUpDown 与 ComboBox 默认会用滚轮改自己的值：用户只想往下翻页，
/// 鼠标恰好掠过输入框就把数值改了，且毫无提示。对「清理阈值」这种设置项
/// 来说这是会造成误配置的行为，因此一律禁掉，改为滚动页面。
/// </summary>
internal static class WheelForwarder
{
    public static void ToScrollParent(Control self, MouseEventArgs e)
    {
        // 标记已处理，阻止控件自身的默认行为（改值 / 切换选项）
        if (e is HandledMouseEventArgs h) h.Handled = true;

        var p = self.Parent;
        while (p != null && p is not ScrollableControl { AutoScroll: true })
            p = p.Parent;

        if (p is not ScrollableControl target) return;

        var v = target.VerticalScroll;
        if (!v.Visible) return;

        int want = v.Value - e.Delta; // Delta 向上为正，滚动值方向相反
        v.Value = Math.Max(v.Minimum, Math.Min(v.Maximum, want));
        target.PerformLayout();
    }
}

/// <summary>滚轮不改值、只滚动页面的数值输入框。</summary>
internal sealed class WheelSafeNumericUpDown : NumericUpDown
{
    protected override void OnMouseWheel(MouseEventArgs e) => WheelForwarder.ToScrollParent(this, e);
}

/// <summary>滚轮不切换选项、只滚动页面的下拉框。</summary>
internal sealed class WheelSafeComboBox : ComboBox
{
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // 展开状态下滚轮应当正常浏览列表项，此时不拦截
        if (DroppedDown)
        {
            base.OnMouseWheel(e);
            return;
        }
        WheelForwarder.ToScrollParent(this, e);
    }
}
