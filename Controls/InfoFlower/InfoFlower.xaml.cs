using System;
using CC98.Objects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Controls;

public sealed partial class InfoFlower : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            "Text",
            typeof(string),
            typeof(InfoFlower),
            new(string.Empty, OnTextChanged));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            "Glyph",
            typeof(string),
            typeof(InfoFlower),
            new(string.Empty, OnGlyphChanged));

    public InfoFlower()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    private string GetGlyphFromStatus(FlowStatus status)
    {
        return status switch
        {
            FlowStatus.Warning => "\uE7BA",
            FlowStatus.Success => "\uE930",
            FlowStatus.Fail => "\uEA39",
            FlowStatus.Info => "\uE946",
            _ => "\uE779"
        };
    }

    public void Play(string glyph, string message)
    {
        FlowIcon.Glyph = glyph;
        FlowInfo.Text = message;
        FlowerTransform.TranslateY = 0;
        Flower.Opacity = 0;
        Flower.Visibility = Visibility.Visible;
        // 先退订再订阅:动画未完成就再次 Play 时避免叠加重复订阅
        FlowerAnimation.Completed -= OnAnimationCompleted;
        FlowerAnimation.Completed += OnAnimationCompleted;
        FlowerAnimation.Begin();
    }

    public void Play(FlowStatus status, string message)
    {
        FlowIcon.Glyph = GetGlyphFromStatus(status);
        FlowInfo.Text = message;
        FlowerTransform.TranslateY = 0;
        Flower.Opacity = 0;
        Flower.Visibility = Visibility.Visible;
        FlowerAnimation.Completed -= OnAnimationCompleted;
        FlowerAnimation.Completed += OnAnimationCompleted;
        FlowerAnimation.Begin();
    }

    private void OnAnimationCompleted(object sender, object e)
    {
        Flower.Visibility = Visibility.Collapsed;
        FlowerAnimation.Completed -= OnAnimationCompleted;
        FlowerAnimation.Stop();

        // 触发完成事件
        AnimationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (InfoFlower)d;
        if (control.FlowInfo != null)
        {
            control.FlowInfo.Text = (string)e.NewValue;

            // 根据文本内容调整可见性
            control.FlowInfo.Visibility =
                string.IsNullOrEmpty((string)e.NewValue) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (InfoFlower)d;
        control.FlowIcon?.Glyph = (string)e.NewValue;
    }

    // 动画完成事件
    public event EventHandler AnimationCompleted;
}