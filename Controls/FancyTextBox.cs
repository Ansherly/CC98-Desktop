using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CC98.Controls;

/// <summary>
///     带自定义外框的文本输入框。
/// </summary>
/// <remarks>
///     用于替代「<see cref="Border" />（TextBoxBorderStyle）套无边线 <see cref="TextBox" />（TextBoxBlankStyle）」
///     的组合写法：去掉 TextBox 默认的底部边线等样式，边框整体由外层 Border 绘制，
///     <see cref="Control.BorderBrush" /> 等外观属性可直接在控件上自定义。
///     注意：本工程启用 NativeAOT(PublishAot)，经典 Binding 依赖反射解析属性路径，不可用；
///     Text 的双向同步由 DP 变更回调 + TextChanged 事件手工完成。
/// </remarks>
public class FancyTextBox : Control
{
    private TextBox? _innerTextBox;

    public FancyTextBox()
    {
        DefaultStyleKey = typeof(FancyTextBox);
    }

    /// <summary>输入框文本。与内部 TextBox 双向同步。</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(FancyTextBox),
        new PropertyMetadata(string.Empty, OnTextPropertyChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>占位提示文本（同 TextBox.PlaceholderText）。</summary>
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(FancyTextBox), new PropertyMetadata(string.Empty));

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (FancyTextBox)d;
        if (box._innerTextBox == null) return;

        var newText = (string?)e.NewValue ?? string.Empty;
        // 相等性判断防止外部赋值 -> 内部赋值 -> 回写 DP 的循环
        if (!string.Equals(box._innerTextBox.Text, newText))
            box._innerTextBox.Text = newText;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _innerTextBox = GetTemplateChild("PART_TextBox") as TextBox;
        if (_innerTextBox == null) return;

        // 初始同步 + 用户输入回写 DP(相等性判断同上,防循环)
        _innerTextBox.Text = Text;
        _innerTextBox.TextChanged += (_, _) => Text = _innerTextBox.Text;
    }
}
