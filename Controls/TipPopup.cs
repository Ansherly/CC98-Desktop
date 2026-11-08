using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CC98.Controls;

/// <summary>
///     一次性上浮提示：显示时从下方滑入并淡入，<see cref="AutoHideDuration" /> 后滑出淡出并自动收起。
/// </summary>
/// <remarks>
///     移植自 winui-kernel 的 <c>Richasy.WinUIKernel.Share.Base.TipPopup</c>。
///     用法：作为页面最外层 Grid 的最后一个子元素（默认样式为 Bottom + Center、Margin 0,0,0,12），
///     给 x:Name 后调用 <see cref="ShowAsync(InfoType)" />，或者直接绑 <see cref="IsOpen" />。
///     显示/隐藏动画用 CommunityToolkit.WinUI.Animations 的 AnimationBuilder 显式播放，
///     隐藏动画播完才真正折叠（模板内的隐式动画在折叠元素上的触发时机不可控，故不用）。
///     本工程启用 NativeAOT(PublishAot)，模板元素一律通过 GetTemplateChild 取，不使用经典 Binding 反射；
///     Text 在模板内用 TemplateBinding 单向绑定。
/// </remarks>
public partial class TipPopup : Control
{
    /// <summary>滑入动画的起始纵向偏移（正数表示位于落点下方）。</summary>
    private const double ShowFromOffset = 16d;

    /// <summary>滑出动画的结束纵向偏移。</summary>
    private const double HideToOffset = 16d;

    private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>提示文本。</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TipPopup), new PropertyMetadata(string.Empty));

    /// <summary>提示级别，决定左侧图标。</summary>
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(InfoType), typeof(TipPopup), new PropertyMetadata(InfoType.Information, OnStatusPropertyChanged));

    /// <summary>是否正在显示。</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(TipPopup), new PropertyMetadata(false, OnIsOpenPropertyChanged));

    /// <summary>自动收起前停留的时长，设为 <see cref="TimeSpan.Zero" /> 表示不自动收起。</summary>
    public static readonly DependencyProperty AutoHideDurationProperty = DependencyProperty.Register(
        nameof(AutoHideDuration), typeof(TimeSpan), typeof(TipPopup), new PropertyMetadata(TimeSpan.FromSeconds(3.5)));

    private FrameworkElement? _root;
    private FrameworkElement? _informationIcon;
    private FrameworkElement? _warningIcon;
    private FrameworkElement? _successIcon;
    private FrameworkElement? _errorIcon;
    private ProgressRing? _loadingRing;

    private CancellationTokenSource? _autoHideCts;
    private bool _isShown;

    public TipPopup()
    {
        DefaultStyleKey = typeof(TipPopup);
        Visibility = Visibility.Collapsed;
        Loaded += OnLoadedHandler;
        Unloaded += OnUnloadedHandler;
    }

    /// <summary>提示文本。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>提示级别。</summary>
    public InfoType Status
    {
        get => (InfoType)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>是否正在显示。可直接绑定到 ViewModel。</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>自动收起前停留的时长。</summary>
    public TimeSpan AutoHideDuration
    {
        get => (TimeSpan)GetValue(AutoHideDurationProperty);
        set => SetValue(AutoHideDurationProperty, value);
    }

    /// <summary>
    ///     显示提示并在 <see cref="AutoHideDuration" /> 后自动收起。
    /// </summary>
    /// <param name="type">提示级别。</param>
    /// <returns>自动收起完成时结束的任务。</returns>
    public async Task ShowAsync(InfoType type = InfoType.Information)
    {
        Status = type;
        IsOpen = true;

        _autoHideCts?.Cancel();
        if (AutoHideDuration <= TimeSpan.Zero)
            return;

        var cts = new CancellationTokenSource();
        _autoHideCts = cts;

        try
        {
            await Task.Delay(AutoHideDuration, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cts.IsCancellationRequested)
            IsOpen = false;
    }

    /// <summary>
    ///     显示提示并在 <see cref="AutoHideDuration" /> 后自动收起。
    /// </summary>
    /// <param name="type">提示级别。</param>
    /// <param name="message">提示文本。</param>
    /// <returns>自动收起完成时结束的任务。</returns>
    public Task ShowAsync(InfoType type, string message)
    {
        Text = message ?? string.Empty;
        return ShowAsync(type);
    }

    /// <summary>显示提示（不等待自动收起，可在同步方法里直接调用）。</summary>
    public void Show(InfoType type, string message) => _ = ShowAsync(type, message);

    /// <summary>立即收起（播放收起动画）。</summary>
    public void Hide() => IsOpen = false;

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _root = GetTemplateChild("PART_Root") as FrameworkElement;
        _informationIcon = GetTemplateChild("PART_InformationIcon") as FrameworkElement;
        _warningIcon = GetTemplateChild("PART_WarningIcon") as FrameworkElement;
        _successIcon = GetTemplateChild("PART_SuccessIcon") as FrameworkElement;
        _errorIcon = GetTemplateChild("PART_ErrorIcon") as FrameworkElement;
        _loadingRing = GetTemplateChild("PART_LoadingRing") as ProgressRing;

        ApplyStatus();

        if (IsOpen)
            _ = PlayShowAsync();
    }

    private static void OnStatusPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => (d as TipPopup)?.ApplyStatus();

    private static void OnIsOpenPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TipPopup instance)
            return;

        if (!(bool)e.NewValue)
            instance._autoHideCts?.Cancel();

        _ = (bool)e.NewValue ? instance.PlayShowAsync() : instance.PlayHideAsync();
    }

    private void OnLoadedHandler(object sender, RoutedEventArgs e)
    {
        if (IsOpen)
            _ = PlayShowAsync();
    }

    private void OnUnloadedHandler(object sender, RoutedEventArgs e)
        => _autoHideCts?.Cancel();

    private async Task PlayShowAsync()
    {
        Visibility = Visibility.Visible;

        // 模板或可视树尚未就绪时先只切可见性，等 Loaded / OnApplyTemplate 再补动画。
        if (_root is null || !IsLoaded || _isShown)
            return;

        _isShown = true;

        await AnimationBuilder.Create()
            .Translation(Axis.Y, from: ShowFromOffset, to: 0d, duration: ShowDuration)
            .Opacity(from: 0d, to: 1d, duration: ShowDuration)
            .StartAsync(_root);
    }

    private async Task PlayHideAsync()
    {
        if (_root is null || !IsLoaded)
        {
            _isShown = false;
            Visibility = Visibility.Collapsed;
            return;
        }

        if (!_isShown)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        _isShown = false;

        try
        {
            await AnimationBuilder.Create()
                .Translation(Axis.Y, from: 0d, to: HideToOffset, duration: HideDuration)
                .Opacity(from: 1d, to: 0d, duration: HideDuration)
                .StartAsync(_root);
        }
        catch (Exception)
        {
            // 元素已被移出可视树等情况下动画会失败，直接收起即可，不影响功能。
        }

        Visibility = Visibility.Collapsed;
    }

    private void ApplyStatus()
    {
        if (_informationIcon is null)
            return;

        Hide(_informationIcon);
        Hide(_warningIcon);
        Hide(_successIcon);
        Hide(_errorIcon);

        if (_loadingRing is not null)
        {
            _loadingRing.Visibility = Visibility.Collapsed;
            _loadingRing.IsActive = false;
        }

        switch (Status)
        {
            case InfoType.Information:
                Show(_informationIcon);
                break;
            case InfoType.Success:
                Show(_successIcon);
                break;
            case InfoType.Warning:
                Show(_warningIcon);
                break;
            case InfoType.Error:
                Show(_errorIcon);
                break;
            case InfoType.Loading:
                if (_loadingRing is not null)
                {
                    _loadingRing.Visibility = Visibility.Visible;
                    _loadingRing.IsActive = true;
                }

                break;
            default:
                break;
        }
    }

    private static void Show(FrameworkElement? element)
    {
        if (element is not null)
            element.Visibility = Visibility.Visible;
    }

    private static void Hide(FrameworkElement? element)
    {
        if (element is not null)
            element.Visibility = Visibility.Collapsed;
    }
}

/// <summary>信息类型。</summary>
public enum InfoType
{
    /// <summary>普通消息。</summary>
    Information,

    /// <summary>错误消息。</summary>
    Error,

    /// <summary>警告消息。</summary>
    Warning,

    /// <summary>成功消息。</summary>
    Success,

    /// <summary>加载中。</summary>
    Loading,
}
