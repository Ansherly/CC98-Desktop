using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CC98.Controls.SmartPager;

/// <summary>
///     分页导航组件:首页 / 上一页 / 「当前页/总页数」 / 下一页 / 末页。
///     点击中间的页码弹出「输入目的地」对话框,可直接跳转到指定页。
/// </summary>
/// <remarks>
///     页码对外统一为 0-based(<see cref="SelectedPageIndex" />),界面上按 1-based 展示。
///     API 与 DevWinUI 的 PagerControl 对齐(NumberOfPages / SelectedPageIndex /
///     SelectedIndexChanged),因此可以原地替换。
/// </remarks>
public sealed partial class SmartPager : UserControl
{
    /// <summary>图标按钮在"已到边界"时的整体透明度。</summary>
    private const double DisabledOpacity = 0.35;

    public SmartPager()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    #region 依赖属性

    public static readonly DependencyProperty NumberOfPagesProperty =
        DependencyProperty.Register(
            nameof(NumberOfPages),
            typeof(int),
            typeof(SmartPager),
            new(1, OnPagerPropertyChanged));

    public static readonly DependencyProperty SelectedPageIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedPageIndex),
            typeof(int),
            typeof(SmartPager),
            new(0, OnPagerPropertyChanged));

    /// <summary>总页数。小于 1 时按 1 处理。</summary>
    public int NumberOfPages
    {
        get => (int)GetValue(NumberOfPagesProperty);
        set => SetValue(NumberOfPagesProperty, value);
    }

    /// <summary>
    ///     当前页(0-based)。程序化赋值与点击翻页按钮等价,同样会触发
    ///     <see cref="SelectedIndexChanged" />,宿主可据此加载对应页。
    /// </summary>
    public int SelectedPageIndex
    {
        get => (int)GetValue(SelectedPageIndexProperty);
        set => SetValue(SelectedPageIndexProperty, value);
    }

    #endregion

    /// <summary>当前页变化时触发,参数为新的 0-based 页码。</summary>
    public event EventHandler<int>? SelectedIndexChanged;

    private static void OnPagerPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SmartPager pager) return;

        pager.UpdateVisualState();

        // 只有"当前页"变化才对外通知。
        // 总页数变化不通知:否则宿主在加载完主题信息设置 NumberOfPages 时会被误触发一次加载。
        // 注意 DP 的同值赋值不会进回调,所以"赋一个和当前相同的页码"不会重复通知 —— 这与
        // PagerControl 的行为一致,宿主需要自行处理"目标页恰好就是当前页"的情况。
        if (e.Property == SelectedPageIndexProperty)
            pager.SelectedIndexChanged?.Invoke(pager, pager.SelectedPageIndex);
    }

    #region 显示与状态

    /// <summary>把两个页码属性同步到界面文本与按钮可用状态。</summary>
    private void UpdateVisualState()
    {
        var total = Math.Max(NumberOfPages, 1);
        var current = Math.Clamp(SelectedPageIndex, 0, total - 1);

        PageButton.Content = $"{current + 1}/{total}";

        var atFirst = current <= 0;
        var atLast = current >= total - 1;

        SetButtonEnabled(FirstButton, !atFirst);
        SetButtonEnabled(PreviousButton, !atFirst);
        SetButtonEnabled(NextButton, !atLast);
        SetButtonEnabled(LastButton, !atLast);
    }

    /// <summary>
    ///     设置按钮的可用状态。
    ///     图标的 Foreground 是显式指定的,不会跟随 Button 的禁用态变灰,
    ///     所以这里额外用整体透明度来表达"已经到头了"。
    /// </summary>
    private static void SetButtonEnabled(HyperlinkButton h, bool enabled)
    {
        h.IsEnabled = enabled;
        h.Opacity = enabled ? 1 : DisabledOpacity;
    }

    #endregion

    #region 翻页

    private void FirstButton_Click(object sender, RoutedEventArgs e) => SelectedPageIndex = 0;

    private void PreviousButton_Click(object sender, RoutedEventArgs e) =>
        SelectedPageIndex = Math.Max(SelectedPageIndex - 1, 0);

    private void NextButton_Click(object sender, RoutedEventArgs e) =>
        SelectedPageIndex = Math.Min(SelectedPageIndex + 1, Math.Max(NumberOfPages - 1, 0));

    private void LastButton_Click(object sender, RoutedEventArgs e) =>
        SelectedPageIndex = Math.Max(NumberOfPages - 1, 0);

    #endregion

    #region 跳页对话框

    /// <summary>对话框关闭后要跳转到的目标页(0-based);对话框被取消时不会被使用。</summary>
    private int _pendingPageIndex;

    /// <summary>同一时刻只允许打开一个 ContentDialog,用它拦住连点。</summary>
    private bool _isDialogOpen;

    private async void PageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDialogOpen) return;

        _isDialogOpen = true;
        try
        {
            var total = Math.Max(NumberOfPages, 1);
            var current = Math.Clamp(SelectedPageIndex, 0, total - 1);

            // 预填当前页并全选:用户直接输入数字即可覆盖
            DestinationBox.Text = (current + 1).ToString(CultureInfo.InvariantCulture);
            DestinationTotal.Text = $"/{total}";
            DestinationDialog.XamlRoot = XamlRoot;

            if (await DestinationDialog.ShowAsync() != ContentDialogResult.Primary) return;

            SelectedPageIndex = _pendingPageIndex;
        }
        catch (Exception ex)
        {
            // 页面被导航移除等场景下 ShowAsync 会抛异常,这里兜底避免 async void 崩溃
            System.Diagnostics.Debug.WriteLine($"跳页对话框打开失败: {ex.Message}");
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    /// <summary>
    ///     输入框只接受数字:这次变更里只要出现非数字字符就整体取消,
    ///     字符根本进不去(粘贴同理),而不是先输进去再提示错误。
    ///     注意用的是 BeforeTextChanging 而不是 TextChanging —— 前者在文本应用前触发,可以取消。
    /// </summary>
    private void DestinationBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        // 空串必须放行,否则用户没法清空重输
        foreach (var c in args.NewText)
        {
            if (c is < '0' or > '9')
            {
                args.Cancel = true;
                return;
            }
        }
    }

    private void DestinationDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var total = Math.Max(NumberOfPages, 1);
        var text = DestinationBox.Text.Trim();

        // 输入不是 1..total 的整数时取消关闭,让用户就地改;否则记下目标页,关闭后再跳。
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var page)
            || page < 1
            || page > total)
        {
            args.Cancel = true;
            DestinationBox.SelectAll();
            DestinationBox.Focus(FocusState.Programmatic);
            return;
        }

        _pendingPageIndex = page - 1;
    }

    #endregion
}
