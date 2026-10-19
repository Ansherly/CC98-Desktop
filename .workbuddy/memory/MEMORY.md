# CC98 Desktop — 项目长期记忆

## 工程速查
- WinUI 3 桌面应用，主工程 `CC98 Desktop.csproj`，解决方案 `CC98.sln`。
- 快速校验：`dotnet build "CC98 Desktop.csproj" -v m --nologo 2>&1 | tail -30`（约 1 分钟，含 restore）。
  基线：0 错误 + **唯一警告 68 条**（nullable / 未使用变量），改完不应新增。
  注意汇总行会显示 `136 个警告`（XAML 与 C# 两趟各报一遍），判断基线要用
  `grep warning | sed 's/ \[D:.*//' | sort -u | wc -l`。
- 桌面 GUI 验证见技能 `winui3-desktop-verify`；纯 C# 逻辑验证见技能 `dotnet-source-probe`。
- 规划书（`*规划书.md` 在仓库根）是本项目的既定沟通格式：方案先落文档，再动代码。

## UBB 解析（`Controls/UbbTextBlock`）
- 结构：`Tokenizer/`（词法）→ `Parser/`（AST）→ `Common/RenderStrategy.cs`（渲染策略表）。
  `Tokenizer/*.cs` + `Parser/*.cs` **不引用 WinUI，可单独编译**（探针工程首选目标）。
- **两种属性**：
  - **主属性**：紧跟标签名，`[img=1]`、`[topic=1029383]`、`[url=https://…]`、`[color=#f00]`。
    键名由标签类型决定，走 `UBBParser.GetAttributeName`：url→`href`、color→`color`、size→`size`、
    font→`font`、code→`language`、quote→`author`、emoji→`code`，其余→`value`。
  - **键值对属性**：逗号分隔，`[audio,title=歌,author=某人]url[/audio]`，**顺序无关**，
    键名即字面文本，落在 `TagNode.Attributes`。
- **逗号边界规则**（`UbbTokenizer.IsCommaBoundary`，2026-10-16 定稿）：
  逗号是属性边界 ⇔ 逗号前是标签名，或逗号之后匹配「键名 空白* =」。
  否则逗号是属性值内部的普通字符（`title=Hello, World,author=A` 的第一个逗号）。
- **引号不是 UBB 语法**——引号只是普通字符，解析层不做去引号处理（用户 2026-10-16 明确）。
- 无名值槽位回退命名 `GetAttributeName(type)` / `value{n}`，用于兼容 `[code,csharp]`、`[tag,a]`。
- 未知标签（`[content,…]`）整体回退为纯文本，属预期行为。
- **地址类标签（img / url / audio / video / upload）取值约定**：主属性优先，取不到回退**子节点全文**。
  统一走 `RenderHelper.ResolveAddress(tagNode, key, allowRelative)`；子节点文本用
  `UbbNodeTextExtensions.CollectText(this UbbNode)`（递归收集，**纯 C#、可单独编译**）
  —— **绝不要再用 `node.FirstChild is TextNode`**，那样 `[img][b]http://…[/b][/img]` 会取不到。
- **audio / video 不支持把 src 写在主属性里**（用户 2026-10-16 明确），别去补这条回退。
- `TopicRenderStrategy`：主题号主属性为空时回退子节点文本；ID 非法时按普通文本渲染子节点。
  注意 `string.All(char.IsDigit)` 对空串返回 **true**，必须先判空，否则会生成 `/topic/` 死链。
## MusicPlayer（`Controls/MusicPlayer`）
- 尺寸（2026-10-16 定稿）：卡片 **480** 宽、圆角 **6**；海报 **52** 圆角 6（只比右侧两行字略高）；
  进度 `Slider` `MinWidth=360`；歌名/作者 `MaxWidth=296`。这几个数**互相咬合**：
  自然宽度 = 32 + max(52+16+296+16+68, 34+10+360+10+34) = 480，所以「有限宽宿主」和
  「无限宽宿主」得到同一个宽度，窄栏还能自动缩。改一个就得把其余的重算一遍。
- **内边距故意不对称**：`Padding="16,16,16,14"`。WinUI 的 `Slider` 布局盒比轨道高得多
  （轨道只占其中 4 DIP，盒底有一段看不见的空白），进度行给了 `Height="20"` + 滑块
  `VerticalAlignment="Center"`；再把状态行的外边距挪到子元素上（否则子元素都折叠时那一格
  还占 8 DIP）。实测「海报上沿→内顶」与「轨道下沿→内底」都是 16.0 DIP。
  **这类"看起来不平衡"的问题必须量像素，不能靠调数字猜。**
- **两个图标按钮在头部行第三列**（右上角，与歌名同一行，`VerticalAlignment="Top"`）：
  下载 + 音量，都是 32px 圆钮。音量条的**浮层**用 `Flyout`
  （`Placement="BottomEdgeAlignedRight"`；用 `Bottom` 会居中到按钮上、一半探出卡片外），
  `FlyoutPresenterStyle` `BasedOn="{StaticResource DefaultFlyoutPresenterStyle}"` 去默认厚内边距。
  浮层里是「静音小按钮 + 音量滑块」。`GlobalMediaPlayer.Volume` 是全局音量。
- **滑块要覆盖 6 个资源键**：`SliderTrackValueFill*`（已填充轨道）+ `SliderThumbBackground*`
  （拇指里那颗圆点）。**`SliderInnerThumbFill` 这个键不存在**——写上去不报错也完全无效，
  别被名字骗了。禁用态 `SliderThumbBackgroundDisabled` 不覆盖，跟随主题变灰正合适。
  定位这类模板键的办法：把候选键同时覆盖成 `Red` / `Blue` 跑一张截图，哪个部位变色就是哪个键。
- 依赖属性：`Src` / `Title` / **`Author`** / **`Poster`**（用户把 `Cover` 改名成了 `Poster`）（+ `LoadMediaCallback`）。
- **海报只有一张 `Image`（`PosterImage`）**，XAML 的 `Source="ms-appx:///Assets/audio_cover.png"`
  就是默认态；`Poster` 有值就换它的 `Source`，为空就换回默认图 —— **不要再叠第二张占位 Image**
  （用户 2026-10-16 指出）。关键原因：`BitmapImage` 是**异步**取图的，
  「地址格式错」在 `new Uri` 就抛（try/catch 拦得住），但「404/断网」只会让图**变空白**，
  必须挂 `ImageFailed` 才能回退（用 `_isDefaultPoster` 标志位防止默认图自身失败时无限回退）。
  旧的双 Image 写法在"给了地址但取不到"时露出的是空白，并没有回退。
- **`Assets/**` 下的文件不需要在 csproj 里登记就会被打包**（实测 `Assets/Emoji/` 254 个 png，
  csproj 0 条引用，AppX 里 254 个）。新资源丢进 `Assets/` 就能用 `ms-appx:///Assets/...` 引用。
  注意 `dotnet build` **不刷新** `bin/.../AppX/` 那份打包布局（只由 VS/F5 或打包目标生成），
  别拿它判断资源有没有被打包。
- 主色用**命名色** `LightSeaGreen`（CC98 传统链接色），悬停/按下取同族的 `DarkCyan` / `Teal`——
  **不要写 `#RRGGBB`**（用户明确要求）。做成 `PlayerAccentBrush` 等资源后用
  `<StaticResource x:Key="ButtonBackground" ResourceKey="PlayerAccentBrush" />` 别名到控件模板资源键；
  主题相关的颜色一律交给 `{ThemeResource}`。
  按钮配色**必须覆盖资源键** `ButtonBackground*` / `ButtonForeground*` / `ButtonBorderBrush*`，
  进度/音量条覆盖 `SliderTrackValueFill*`——只设 `Background` 会被内置 VisualState 盖掉。
- **动画不要在 `UserControl.Resources` 里写 Storyboard**：资源字典有独立命名域，
  `Storyboard.TargetName` 解析不到可视树元素，`Begin()` 会抛异常。用
  `Storyboard.SetTarget(anim, scaleTransform)` + `SetTargetProperty(anim, "ScaleY")` 在代码里挂。
- `InitializeAndPlay` 必须**拆成两段 try**：只管"加载媒体源"的那段失败才算初始化失败；
  起播 + 更新 UI 的异常不能把 `_isInitialized` 打回 false，否则会出现"播着却报错、按钮按不动、进度条不动"。
- 律动条：`Rectangle` 要显式 `VerticalAlignment="Bottom"`（默认 Stretch 会顶对齐）。
- **播放键贴在封面上**（`PlayPauseButton` 52×52 透明按钮盖住两张 Image），整个封面都是热区，
  底排不再有播放键。里面是 `PlayChip` Border：暂停态直径 **24**、居中；播放态 **16**、沿 45° 斜向位移 **15**。
  白环定死 **1.5** DIP（不参与缩放），图标字号 **12** → 8 等比缩（即全部按 52/96 缩）。
  动画在代码里挂：位移用 `TranslateTransform.X/Y`（合成动画）；
  **直径/字号是布局属性，必须 `EnableDependentAnimation=true`**；
  WinUI 3 **没有 `ThicknessAnimation`**（WPF 才有），所以环厚只能靠「描边不动 + 尺寸动」实现。
- 卡片宽度靠**自然宽度**顶到 `MaxWidth`：`ScrollViewer` 之类的宿主会用**无限宽**测量，
  纯靠 `Stretch` + `MaxWidth` 会塌成内容宽。
- 构造函数顺序：`_globalPlayer = GlobalMediaPlayer.Instance;` 必须在 `InitializeComponent()` **之前**
  （XAML 里带初始 `Value` 的滑块会立刻触发 `ValueChanged`）。音量是全局单例的，`GlobalMediaPlayer.Volume`。

## ComposeSheet（主工程）
- `Controls/ComposeSheet/ComposeSheet.xaml(.cs)`：帖子页底部滑出的发帖浮层，
  结构 = 变暗层（黑 42%）+ 按钮行（左关闭/右发送，40px 圆钮，**点击都收起**）+ 亚克力面板（通栏、只圆上两角）。
- `Controls/ComposeSheet/ComposeEditor.xaml(.cs)`：面板里的编辑器 UI（状态栏 + 工具条 + 源码/预览两栏）。
  **用户微调过的视觉基线，别改回去**：两个圆钮都是中性色 `ControlSolidFillColorDefaultBrush`；
  面板用框架的 `AcrylicInAppFillColorDefaultBrush`（不是偏蓝的 `AcrylicCardBrush`）；圆角 6、只有上边框。
  编辑区不铺底色（让亚克力透出），两栏的"纸"用 `ComposePaneBrush`（White / #2D2D2D）。
  亚克力的采样源是依赖属性 **`BackgroundImageSource`（类型 `string`）**，XAML 里直接绑路径
  （如 `{x:Bind AppSettings.Current.ThemePicture}`）；控件自己把 string 转成 BitmapImage，
  支持 ms-appx / http / 绝对 / 相对路径，空则隐藏背景层。
  注意属性叫 `EditorLanguage` 而不是 `Language`（后者撞 `FrameworkElement.Language`）。
- **发送**：`ComposeSheet.Mode`（`EditorMode`：ReplyToPost=0/ReplyToTopic=1/DraftNewTopic=2/EditMyPost=3/EditMyTopic=4）
  + `TopicId`/`PostId`/`ParentId`/`InitialTitle`，`Sent` 事件通知外层刷新。**只有 ReplyToPost 能带 parentId**。
  编辑模式下 `ComposeEditor` 会多出标题行（`TitleRow`/`TitleBox`）；发送成功后才清空（等收起动画结束）。
- ⚠️ **UserControl 上不能定义名为 `Content` 的属性**：XAML 编译器会报无行号的
  `WMC9999: 无法将 XamlType 强制转换为 DirectUIXamlType`。取源码内容用 `ContentText`。
- **编辑逻辑**在 `Controls/ComposeSheet/MarkupEditing.cs`（纯 C#，只算「新文本 + 新选区」，
  `%TEMP%\markup-edit-probe` 有 19 条断言可直接跑）。`ComposeEditor.Format_Click` 按 `Tag` 分派 UBB/MD 两套语法。
- **颜色用 Flyout + `ColorPicker`**（ComposeEditor 现在用的是自带的 `Microsoft.UI.Xaml.Controls.ColorPicker`），
  不要用 ContentDialog：代码 new 的取色器塞进 ContentDialog 会抛 `COMException 0x800F1000`（整个窗口变黑）。
  注意 `SketchPage.xaml` 仍用 `<controls:ColorPicker>`，所以 csproj 里 **toolkit 的 ColorPicker 包不能删**。
- **预览**：`PreviewScroller` + `UbbViewer`/`MdViewer`，300ms 防抖（`DispatcherQueueTimer` 存字段），
  空文本只显示 `PreviewEmpty` 占位，编辑模式不渲染、切回来补一次。
- **媒体插入**：`img/video/audio/upload` 不直接插标签，弹代码构造的 `Flyout`（上传本地文件 / 仅输入标签 /
  自定义 URL），走 `FileUploadService`；粘贴（`EditorBox_Paste`）里剪贴板有文件/位图就先上传再插。
  反馈用 `InfoFlower`。上传上限 `FileUploadService.MaxUploadSizeBytes` = 5 MiB = 5,242,880 字节，
  判据统一走 `IsOverSizeLimit()`。
- **剪贴板大图**：`UploadClipboardBitmapAsync` 是「原样优先 + 缩放降级」——原字节能过就原样传，
  超限才按 100/70/50/35% 重编码（有 alpha 走 PNG）。**别再试 `ImageQuality` 降质量：实测无效**
  （q=1.0/0.6/0.2 编出来字节完全一样）。探针在 `%TEMP%\clipbitmap-probe`。
  ⚠️ 因此编辑器已无法在预览宿主里截图（`UbbTextBlock` → `Primitives` → `Kernel/Services` 拖不动），
  这部分只能 `dotnet build` + 用户自己看。
- `ColorPicker` 有两份（工具包 / WinUI 自带），代码里直接写名字会 CS0104，XAML 里 `controls:` 前缀没这问题。
- 接在 `TopicPage` 的 RootGrid 最后、`Grid.RowSpan="3"`；`writereply_Click` 调 `ComposeOverlay.Show()`
  （原来的 `Frame.Navigate(typeof(SketchPage))` 已替换）。
- 动画在代码里挂 Storyboard：`TranslateTransform.Y` 从 `ActualHeight` 到 0（300ms EaseOut），收起 200ms。
- 亚克力用项目自己的 **`AcrylicCardBrush`**（#66ccff、TintOpacity 0.4）；
  框架的 `AcrylicInAppFillColorDefaultBrush` 几乎不透明，看不出亚克力。
- 坑：`{ThemeResource}` 缺 key 会**抛 XamlParseException**（不是静默 null）→ 预览宿主必须补主工程的自定义资源；
  深色下按钮别用 `ControlFillColorDefaultBrush`（贴深色遮罩看不见），用 `ControlSolidFillColorDefaultBrush`。

## UBB 编辑器浮层（试验中，尚未落盘主工程）
- 目标：在 **TopicPage 上方加一层浮层**做编辑器，替代原来 `Frame.Navigate(typeof(SketchPage))` 的导航方案。
- 源码目前只在 `%TEMP%\ubb-editor-preview\UbbEditorOverlay.xaml(.cs)`，
  HTML 设计稿在桌面 `CC98 UBB编辑器浮层.html`。稳定后再搬进 `Controls/UbbEditor/`。
- **一律复用 SketchPage 的既有做法**（用户 2026-10-17 明确）：
  - 图标用 `ic:SymbolIcon`（FluentIcons.WinUI），**不要手画 Path**；名字照抄 SketchPage。
  - UBB/MD 与 编辑/分屏/预览 都用 `controls:Segmented`（CommunityToolkit.WinUI.Controls.Segmented）。
  - 匿名用 `CheckBox`（不是 ToggleSwitch）。
  - 两栏宽度用 `controls:GridSplitter`（Sizers 包）——官方文档：用 Grid 就用它，别用 ContentSizer/PropertySizer。
  - 工具条按钮用 **`HyperlinkButton`**（不用 AppBarButton），集合 = SketchPage 支持的那套
    （**UBB 没有 H1/H2**）：粗斜下删｜左中右对齐｜引用/代码/链接｜图片/视频/音频/文档/哔哩/分割线/颜色/表情。
    **不要单独的"上传图片"按钮**（图片/视频/音频/文档本身就是上传入口，重复）。
  - **工具条图标统一 16**（按钮 34×34 不动）：`ic:SymbolIcon` 直接 `FontSize="16"`；
    Microsoft 的 `SymbolIcon`（Bold/Italic/Underline）**没有 FontSize**，得套 `<Viewbox Width="16" Height="16">`。
  - 哔哩按钮的图标用仓库里的 **`Assets/bilibili-color.svg`**（`SvgImageSource` + `ms-appx:///Assets/…`），
    不用 `Symbol="Stream"`。SVG 自带品牌粉、不跟主题变，属预期。
  - **不要写 UBB/MD 语法提示**。
- 面板只有两段：标题栏（状态信息 + UBB/MD + 匿名 + 关闭）+ 编辑区（工具条 + 两栏）。
- 表面色（面板/栏/遮罩）走 `ThemeDictionaries` 的 Light/Dark 两份；文字与强调色用主题资源。
- XAML 要点：
  - `Segmented` **不能靠 SelectedIndex**：XAML 用 `IsSelected="True"`，事件读 `e.AddedItems[0]` 的 `Tag`，
    代码回写遍历 `Items` 改 `IsSelected`（带 `_syncing` 防重入），并在 `Loaded` 里强制同步一次。
  - `SegmentedItem` **只能放文字**：同时设 `Icon` + `Content` 时，底部选中胶囊会偏、不居中
    （用户 2026-10-17 截图确认）。要图标就得自己做 item 模板。
  - 自定义 Style 必须 `BasedOn` 默认样式（`DefaultHyperlinkButtonStyle` / `DefaultAppBarButtonStyle`
    都存在），否则模板被顶掉、控件没外观。
  - `HyperlinkButton` 默认 `Foreground` 是**强调色**，工具条里要显式改成 `TextFillColorPrimaryBrush`；
    它也**没有 `Flyout` 属性**（`Flyout` 在 `Button` 上），弹层得用
    `FlyoutBase.AttachedFlyout` + `FlyoutBase.ShowAttachedFlyout(element)`。
  - `Symbol` 有两套枚举：`Bold`/`Italic`/`Underline` 只在 **Microsoft** 的枚举里，`ic:` 那套没有。
  - 分栏收起时**列宽必须给 0**，且 `MinWidth` 也要放开（否则被下限顶回去）。
  - `TextBlock` 没有 `BorderBrush/BorderThickness`，要画分隔线得外面套 `Border`。

## 构建约定
- **一律用 `dotnet build "CC98 Desktop.csproj" -p:Platform=x64`**。工程声明了
  `Platforms x86;x64;ARM64`，VS 编的是 `obj/x64/…`；不带 Platform 会写到 `obj/Debug/…`，
  两棵树的 XAML 生成代码会不同步 —— 表现就是 VS 里报「不存在名称 Xxx」，而 XAML 里明明有。

## 验证约定（用户 2026-10-17 明确）
- **不做 UI 点击测试**：交互由用户自己点（比注入脚本快）。验证手段只有两种——
  纯逻辑跑探针断言；界面跑 `dotnet build` + 静态截图（浅色/深色/各模式）。

## 验证资产
- `%TEMP%\ubb-attr-probe`：UBB 属性解析探针（csproj + Program.cs 断言用例，46 条）。
  改词法/解析层后先跑它，再跑主工程构建。
- `%TEMP%\player-preview`：WinUI 控件预览宿主 + `shot.py`（ctypes PrintWindow 截图，支持
  `shot.py <exe> <out.png> <delay> <mode>`）。`Player.xaml(.cs)` 在该目录里有**自己的副本**，
  依赖文件仍是 `Compile Include` 仓库路径。改动 XAML 后想看图就先跑它。
  音源是本地生成的 `test-tone.wav`（60s），`PREVIEW_MODE=play|playstop|idle` 控制自动播放/暂停并写 `diag.log`。
- `%TEMP%\ubb-editor-preview`：UBB 编辑器浮层的试验工程（同 player-preview 的写法 + `shot.py`）。
  `PREVIEW_MODE=split|edit|preview`、`PREVIEW_LANG=ubb|md`、`PREVIEW_THEME=light|dark`、`PREVIEW_ANON=1`。
  `check_window.py <pid> <out.png>`：把**已经开着的**窗口置前并截图（不同于 shot.py 的"启动即截图"）。
  手动启动要用 `subprocess.Popen(..., creationflags=DETACHED_PROCESS|CREATE_NEW_PROCESS_GROUP)`，
  并先清掉 `PREVIEW_*` 环境变量；`cmd //c start` 在 Git Bash 里会被路径转换搞坏。
- WinUI 坑集（`DispatcherQueueTimer` 不存字段会被 GC、资源字典 Storyboard 名字域、
  `AppWindow.Resize` 收物理像素、无限宽测量）已写进技能 `winui3-desktop-verify`。
