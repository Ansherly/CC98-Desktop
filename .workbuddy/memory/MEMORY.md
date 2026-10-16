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
- 已知未做：`MusicPlayer` 没有 Author 展示位（`author` 属性解析出来了但没人用）。

## 验证资产
- `%TEMP%\ubb-attr-probe`：UBB 属性解析探针（csproj + Program.cs 断言用例，36 条）。
  改词法/解析层后先跑它，再跑主工程构建。
