# B 站「无需登录」视频直链可行性调研

> 依据：[Richasy/bili-kernel](https://github.com/Richasy/bili-kernel)（main 分支源码通读）+ 2026-11-07 本机实测（全部请求不含任何登录 Cookie / access_key）。
> 可复现脚本：[tools/bili-anon-playurl-probe.py](tools/bili-anon-playurl-probe.py)

## 一、结论

**有，而且不止一条路。但存在明确天花板：**

| 场景 | 未登录能拿到 | 说明 |
| --- | --- | --- |
| 普通投稿视频 · DASH | **最高 480P**（360P/480P 两条轨道 × 3 种编码） | `accept_quality` 里照样列着 4K/1080P+，但 `dash.video` 数组里根本没有这些轨道 |
| 普通投稿视频 · html5 通道 | **720P 单文件 mp4** | 匿名能拿到的最高画质，单条链接、无需拼接 DASH |
| gRPC 播放接口（App 端） | 480P flv/durl | 与 REST 同等受限 |
| 直播 | 原画 flv / hls 直链 | 未登录可正常取流 |
| 免费番剧 | 480P 整集 | 时长完整 |
| 大会员 / 付费番剧、电影 | **只有 60 秒试看片**（6.2 MB mp4，`is_preview=1`，`dash` 为空） | 正片拿不到 |
| 1080P / 1080P+ / 4K / HDR / 杜比 | **无** | 必须登录（`access_key`） |

一句话：**未登录可以拿到"能播"的直链，但画质被锁在 480P（DASH）/ 720P（mp4），且付费内容只剩 60 秒试看。**

## 二、bili-kernel 里"登录"到底卡在哪

协议层它**从不拦你**，卡住的是代码里的 4 处空引用。

### 1. 认证器本身允许匿名

- `BiliAuthenticator.AuthorizeGrpcRequest(request, requireToken: false)` —— 默认不要求令牌，没有 token 时 `access_key` 为空、`authorization` 头干脆不加（`src/BiliKernel.Core/Authenticator/BiliAuthenticator.cs:65-74, 84`）。
- 全仓库 19 处 `RequireToken` 全是 `= false` 或缺省，**没有一处 `RequireToken = true`**；需要用户身份的接口（点赞、投币、收藏、历史、动态、消息）不靠代码抛出，而是靠服务器返回未登录。
- appkey / secret 硬编码在 `src/BiliKernel.Core/Authenticator/BiliAuthenticator.Extension.cs:17-20`（App：`27eb53fc9058f8c3`），签名不需要任何用户凭证。
- 默认 `ApiType = BiliApiType.App`（`src/BiliKernel.Core/Authenticator/BiliAuthorizeExecutionSettings.cs:17`），即 iOS appkey 签名 + 空 `access_key`。

### 2. 播放相关方法逐条核对

| 方法 | 位置 | 协议层 | 代码层（未登录时） |
| --- | --- | --- | --- |
| `GetVideoPageDetailWithGrpcAsync` | `PlayerClient.cs:40` | 不需要 | 正常 |
| `GetVideoPageDetailWithRestAsync` | `:76` | 不需要（仅 buvid） | 正常 |
| `GetVideoPlayDetailWithGrpcAsync` | `:128` | 不需要 | 正常（实测匿名返回 flv480） |
| `GetVideoPlayDetailWithRestAsync` | `:100` | 不需要 | **NullReferenceException**：`:117` 直接 `_tokenResolver.GetToken().UserId` |
| `GetPgcPlayDetailAsync` | `:221` | 不需要 | 正常 |
| `GetLivePlayDetailAsync` | `:170` | 不需要（`RequireCookie`） | 正常 |
| `EnterLiveRoomAsync` / 弹幕 | `:405` / `DanmakuClient.cs:175` | 不需要 | **NullReferenceException**（同一写法） |

> 所以"bili-kernel 必须登录才能播放"是代码 bug，不是接口限制。把 `_tokenResolver.GetToken().UserId` 改成 `_tokenResolver?.GetToken()?.UserId ?? 0`，并且不注册 token / cookie resolver（`BiliAuthenticator` 的构造函数允许两者为 `null`），匿名链路就能跑通。

## 三、实测：各条匿名取流路线

样片 `BV1mP4E6iEzQ`（aid=117574578736357, cid=43407247932），首次指纹来自 `GET /x/frontend/finger/spi`（`b_3`/`b_4` → `buvid3`/`buvid4`），`nav.isLogin = false`。

| # | 路线 | 请求形态 | 返回 | 匿名画质 |
| --- | --- | --- | --- | --- |
| A | REST + appkey 签名 | `GET api.bilibili.com/x/player/playurl?avid&cid&qn=112&fnval=4048&fourk=1&appkey=27eb53fc9058f8c3&mobi_app=iphone&platform=ios&ts&sign=md5` | `code=0`，DASH | **480P / 360P** |
| B | REST + wbi 签名 | `GET /x/player/wbi/playurl`（`w_rid` + `wts`，带 `buvid3`） | `code=0`，DASH | **480P / 360P** |
| C | REST 完全不签名 | `GET /x/player/playurl?avid&cid&qn=112&fnval=4048&platform=pc` | `code=0` | 480P / 360P（说明当前 playurl **未校验签名**，别依赖，随时会变） |
| D | html5 单文件 | `GET /x/player/playurl?avid&cid&qn=64&fnval=1&platform=html5&high_quality=1` | `code=0`，`quality=64`、`format=mp4720`、`durl=1`、283.9 MB | **720P mp4 单文件** |
| E | gRPC PlayView | `POST grpc.biliapi.net/bilibili.app.playurl.v1.PlayURL/PlayView`（iOS UA + `x-bili-*` + `buvid`，无 `authorization`） | HTTP 200，`grpc-status` 空，quality=32 | 480P（flv480，3 段 durl） |
| F | 直播 | `GET api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo` | `code=0`，6 路流 | 原画 flv / hls |
| G | 免费番剧 | `GET api.bilibili.com/pgc/player/web/playurl?ep_id&cid&qn=112&fnval=4048&module=bangumi` | `code=0`，`is_preview=0` | 480P 整集（dash duration＝整集时长） |
| H | 大会员番剧（如 `season_id=47912`） | 同上 + `try_look=1` | `code=0` 但 `dash` 为空、`is_preview=1` | 只有 `durl` 一段 **60160 ms / 6.2 MB 试看** |

补充事实：

- **TV 端老 appkey 已废**：`/x/tv/playurl`（`appkey=4409e2ce8ffd12b8`）匿名返回 `-400 请求错误`，别再走"TV 端免登录 1080P"这条老路。
- **CDN 强烈防盗链**：必须带 `Referer: https://www.bilibili.com`，只给 UA 会拿到 `403`（部分 mcdn 节点直接断连）。
- **链接有效期 2 小时**：dash 与 mp4 的 `deadline` 参数均为 `请求时刻 + 7200s`，必须现取现用。
- **域名形态**：`upos-sz-*.bilivideo.com`（mp4 常在）、`*.edge.mountaintoys.cn`、`xy*.mcdn.bilivideo.cn`（P2P 加速节点，稳定性差，建议优先 `upos-`/`*.bilivideo.com`）。
- **DASH 是音视频分离**（`.m4s`），需要前端 MSE（dash.js / 自己拼 `init + media`）或后端合并；html5 通道则是单文件 mp4，可直接喂给播放器。
- **风控**：`www.bilibili.com` 的 HTML 页会直接返回验证码页，但 `api.bilibili.com` 正常；REST playurl 不带 cookie 也能过，但 web 类接口建议带 `buvid3/buvid4` 防 `-352`。

## 四、为什么 1080P 拿不到

这不是接口或签名问题（C 组表明签名都不校验），而是**账号态策略**：

- 服务端根据 `access_key` / 登录态裁剪 `dash.video`，未登录只下发 ≤480P 轨道；请求里 `qn=112`、`fourk=1` 会被无声降级。
- `accept_quality`、`support_formats` 是"理论上这个视频有哪些画质"，与实际下发的轨道无关 —— 这两处最容易被误读成"能拿 1080P"。
- 网页端 `platform=html5` 另开一条 mp4 通道，宽松到 720P，是匿名质量的唯一例外，但**不提供 DASH、不能选码率**。

## 五、落地建议（CC98 桌面端内嵌 B 站视频）

按需求从简到繁：

1. **只求能播 → 官方 iframe**：`https://player.bilibili.com/player.html?bvid=BV...&autoplay=0`（实测匿名 `200`）。WebView2 直接嵌入，画质、风控、签名全部由 B 站自己负责，零维护。
2. **要用自己的播放控件**:走 html5 单文件 mp4(720P),但必须带 `Referer: https://www.bilibili.com/` 与浏览器 UA。官方 `MediaPlayerElement` 既不能自定义请求头,又会在控件卸载时以 `E_ABORT(0x80004004)` 触发原生 failfast 崩进程 —— 本项目已改用 **libmpv**(移植 `Richasy/mpv-winui` 的 OpenGL→D3D11 渲染管线),把直链连同 `referrer` / `user-agent` 直接交给 mpv 拉流,边下边播,不需要先下载落盘。
3. **要自适应/省流量**：走 appkey 签名的 DASH（480P），自行做音视频合并与码率切换，代价最高、收益有限。
4. **继续用 bili-kernel**：修掉 `GetToken().UserId` 的 4 处空引用，且不注册 `IBiliTokenResolver` / `IBiliCookiesResolver`；`GetVideoPlayDetailWithGrpcAsync` 是匿名最省事的入口。
5. **确实需要 1080P+**：只能登录。bili-kernel 自带 TV 扫码 / 二维码授权，扫码后 `access_key` 写入 token 即可解锁到 1080P+（大会员内容还需会员态）。

## 六、复现

```powershell
python tools/bili-anon-playurl-probe.py              # 用热门榜第一条做样片
python tools/bili-anon-playurl-probe.py BV1xx411c7mD # 指定视频
```

输出即第三节的表格（含 CDN `Referer` 实测与 PGC 免费/付费对比）。

## 七、风险提示

- 抓直链与绕过登录态取流属于灰色地带，与 B 站用户协议存在冲突；链接带防盗链与 2 小时时效，B 站可随时收紧（`platform=pc` 不校验签名就是随时会变的例子）。
- 建议定位为"用户已在浏览器登录后仍能播放"的兜底能力，而不是以"免登录看高清/去下载"为卖点；不要做批量下载、去水印等衍生功能。
- 客户端请不要内置会过期的直链缓存；每次播放按需取流，并对 `code != 0`、`dash` 为空、`is_preview=1` 做降级提示。
