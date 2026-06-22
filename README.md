<div align="center">

<p><strong><span style="font-size: 2.2em;">Shirobot.Plugin.MyParser</span></strong></p>

<p><em>一个面向 <a href="https://github.com/ShirokaProject/ShiroBot">ShiroBot</a> 的抖音 / Bilibili / 小红书 / 小黑盒内容解析与转发插件。</em></p>

</div>

## 简介

Shirobot.Plugin.MyParser 是 [ShiroBot](https://github.com/ShirokaProject/ShiroBot) 生态中的学习型插件项目，用于个人学习、技术交流和合法合规的插件开发实践。

当前支持抖音、Bilibili、小红书和小黑盒内容解析，包括视频、图集、图文、专栏、番剧、直播、帖子/文章、封面卡片渲染、长文档图片渲染、评论卡片和消息转发流程。

> 维护说明：本项目主要服务个人使用和学习验证，属于随缘维护项目。第三方平台页面结构、接口、风控策略随时可能变化，因此不保证长期稳定、实时适配或及时修复。

## 功能

- 自动识别聊天中的抖音 / Bilibili / 小红书 / 小黑盒链接。
- 支持手动命令触发：`#parse <链接>`。
- 支持需要登录态的解析场景。
- 支持视频流下载、本地合并与 QQ 消息发送。
- 支持封面卡片、图文卡片和长文档图片渲染。
- 支持合并转发正文与图片。
- 支持消息处理状态表情：开始、完成、失败。
- 支持处理进度日志、质量选择和文件发送 fallback。

### 抖音

- 支持抖音视频解析与转发。
- 支持图集、LivePhoto 等多媒体内容解析。
- 支持 Cookie 登录态检查。
- 支持解析开始、成功、失败状态表情。

### Bilibili

- 支持普通 BV 视频解析、封面卡片渲染、DASH 音视频下载、本地 ffmpeg 合并与 QQ VideoSegment 发送。
- 支持分 P 视频识别：不带 `p=` 的分 P BV 默认发送分 P 总览；带 `?p=4` 的链接会解析并发送指定 P 视频。
- 支持分 P 总览中展示每 P 标题、封面、时长和链接，并可通过回复数字解析指定分 P。
- 支持专栏、图文 / opus 解析，正文通过合并转发发送，并支持长文档图片渲染。
- 支持番剧 `md` / `ss` / `ep` 链接解析：`md`/`ss` 展示作品信息和全集列表；`ep` 会先展示番剧信息与全集列表，再解析并发送当前单集视频。
- 支持 Bilibili 直播原站解析：可识别 `live.bilibili.com` 直播间原站链接和跳转到直播间的 `b23.tv` 短链，通过 Bilibili 原站接口解析直播间信息。
- 支持直播间信息展示：标题、主播、封面、直播状态、房间观众、人气/看过、开播时间、直播时长和播放流列表。
- 支持直播播放流合并转发，并推荐画质优先与兼容优先播放流。
- 支持直播短回溯：从当前 HLS 可回溯分片生成短 MP4，并发送到 QQ。
- 支持 QQ 轻应用 / 小程序 payload 中的 Bilibili 链接提取。

### 小红书

- 支持小红书笔记解析。
- 支持视频、图文图片、正文和评论提取。
- 支持前 10 条评论获取与图文问答卡片渲染。
- 支持外部 sign 服务集成。
- 支持 Cookie 登录态检查和实验性扫码登录。

### 小黑盒

- 支持小黑盒帖子 / 文章链接解析，覆盖 `xiaoheihe.cn`、`heybox.cn`、`maxjia.com` 等域名。
- 支持 QQ 轻应用 / 小程序 payload 中的小黑盒跳转链接提取。
- 支持帖子信息卡片渲染、文章正文合并转发和文章长图渲染。
- 支持正文中的段落、标题、引用、列表、图片、图片说明和视频直链展示。
- 优先使用轻量 HTTP fast path，不默认引入 Chrome / Playwright / Chromium 等浏览器运行时。
- 当小黑盒接口返回验证码 / 风控状态时，会明确回复发送者，而不是静默失败。

## 命令

```text
#parser
#parse <链接>
#bili-login
#xhs-login
#douyin-cookie-check
#bili-cookie-check
#xhs-cookie-check
```

说明：如需登录态，请只在可信环境中配置，并妥善保管生成的本地凭据文件。登录和 Cookie 检查命令涉及账号凭据，仅允许机器人 Owner/Admin 通过私信触发；群聊和临时会话不会执行。

## 平台登录与 Cookie

### 抖音

抖音目前不提供插件内扫码登录命令，需要手动提供 Cookie：

1. 在浏览器打开并登录抖音网页端。
2. 打开浏览器开发者工具，进入 Network / 网络面板。
3. 刷新页面或打开任意作品请求。
4. 在请求头中找到 `Cookie`，复制 `Cookie:` 后面的完整值。
5. 写入插件目录下的 `cookies/douyin.txt`。
6. 重启或热重载插件。

建议 Cookie 至少包含 `sessionid`、`ttwid` 等字段。Cookie 属于账号凭据，不要提交到仓库、日志、截图或公开聊天。

检查抖音 Cookie 有效性：

```text
#douyin-cookie-check
```

该命令仅允许机器人 Owner/Admin 私信使用。

### Bilibili

Bilibili 支持插件内扫码登录：

```text
#bili-login
```

该命令仅允许机器人 Owner/Admin 私信使用。

流程：

1. Owner/Admin 私信机器人发送 `#bili-login`。
2. 使用 Bilibili App 扫描机器人发送的二维码。
3. 在 App 中确认登录。
4. 插件会将 Cookie 保存到运行时配置和插件目录下的 `cookies/bilibili.txt`。

也可以手动获取 Cookie：

1. 在浏览器打开并登录 Bilibili 网页端。
2. 打开开发者工具，进入 Network / 网络面板。
3. 刷新页面，选择任意 `bilibili.com` 或 `api.bilibili.com` 请求。
4. 复制请求头中的完整 `Cookie`。
5. 写入插件目录下的 `cookies/bilibili.txt`。

Bilibili 视频解析需要登录态，Cookie 通常需要包含 `SESSDATA`、`bili_jct` 等字段。视频与音频流会分别下载，并用本地 `ffmpeg` 合并后发送。

检查 Bilibili Cookie：

```text
#bili-cookie-check
```

该命令仅允许机器人 Owner/Admin 私信使用。

### 小红书

小红书解析依赖 Web 接口签名。插件本身不内置签名算法，需要你自行搭建参考项目中的 `xhshow` sign 服务，然后在运行时配置中填写服务地址和 token。

运行时配置示例：

```toml
auto_parse_xiaohongshu_links = true
xiaohongshu_login_command = "#xhs-login"
xiaohongshu_sign_server_url = "<your-sign-server-url>"
xiaohongshu_sign_server_token = "<your-local-sign-token>"
xiaohongshu_fetch_comments = true
xiaohongshu_comment_count = 10
```

扫码登录：

```text
#xhs-login
```

该命令仅允许机器人 Owner/Admin 私信使用。

> 注意：`#xhs-login` 目前属于实验性功能，暂且无法保证可用。小红书登录接口和风控策略变化较快，实际使用建议优先通过浏览器手动复制 Cookie。

实验性流程：

1. 确保 `xhshow` sign 服务已启动，且运行时配置中已填写 `XiaohongshuSignServerUrl` / `XiaohongshuSignServerToken`。
2. Owner/Admin 私信机器人发送 `#xhs-login`。
3. 使用小红书 App 扫描二维码并确认。
4. 如果流程可用，插件会将 Cookie 保存到运行时配置和插件目录下的 `cookies/xiaohongshu.txt`。

推荐方式：浏览器复制小红书 Cookie：

1. 在浏览器打开并登录小红书网页版。
2. 打开开发者工具，进入 Network / 网络面板。
3. 刷新页面，或打开任意笔记详情页。
4. 选择 `xiaohongshu.com` 或 `edith.xiaohongshu.com` 相关请求。
5. 在 Request Headers / 请求头中找到 `Cookie`。
6. 复制 `Cookie:` 后面的完整值。
7. 写入插件目录下的 `cookies/xiaohongshu.txt`。
8. 重启或热重载插件。

小红书 Cookie 建议包含：

```text
a1=...; web_session=...; webId=...; xsecappid=xhs-pc-web
```

其中 `web_session` 用于登录态，`a1` / `webId` / `xsecappid` 会参与签名服务请求。缺少这些字段时，评论区、搜索恢复 `xsec_token`、作者作品列表等接口可能无法正常使用。

评论区接口通常需要登录 Cookie、`xsec_token` 和 sign 服务。分享链接缺少 `xsec_token` 时，插件会尽量通过作者作品列表或搜索恢复，但该能力受平台风控和页面结构影响，不保证每次成功。

检查小红书 Cookie 有效性：

```text
#xhs-cookie-check
```

该命令仅允许机器人 Owner/Admin 私信使用。检查命令会调用小红书登录态接口，并返回当前 Cookie 是否有效、是否已登录，或是否触发安全验证。

### 小黑盒

小黑盒 provider 当前优先走纯 HTTP 解析，不提供插件内登录命令，也不要求默认配置 Cookie。

解析流程会尽量直接请求小黑盒 Web API 并渲染卡片、合并转发正文和文章长图。由于小黑盒存在验证码和设备风控，部分帖子可能返回验证码 / 风控状态；插件会向发送者回复明确失败原因。

> 注意：本项目不会内置浏览器自动化或验证码处理能力来绕过平台风控。若后续需要增强稳定性，建议优先考虑本地 Cookie / 稳定设备状态 / 可选外部解析服务，并确保使用行为符合目标平台规则。

## 配置

插件通过 [ShiroBot](https://github.com/ShirokaProject/ShiroBot) 配置上下文读取 `MyParserConfig`。常用配置示例：

```json
{
  "AutoParseDouyinLinks": true,
  "AutoParseBilibiliLinks": true,
  "AutoParseXiaohongshuLinks": false,
  "ParseCommandPrefix": "#parse",
  "XiaohongshuSignServerUrl": "",
  "XiaohongshuSignServerToken": "",
  "XiaohongshuFetchComments": true,
  "XiaohongshuCommentCount": 10,
  "RequestTimeoutSeconds": 15,
  "QuoteReply": false,
  "SendVideoAsFile": true,
  "SendVideoSegmentAsBase64": true,
  "VideoSegmentBase64MaxMegabytes": 80,
  "UploadVideoAsFile": false,
  "AllowLanAccessToLocalVideoHttpServer": false,
  "DeleteLocalVideoAfterSend": true,
  "DeleteLocalVideoDelaySeconds": 0,
  "MaxImagesToShow": 6
}
```

## 项目结构

当前采用“主插件 + Provider 模块 + 共享抽象层”的拆分结构：

```text
MyParser.sln
Directory.Packages.props              # 集中管理 NuGet 包版本
Directory.Build.targets               # 本地 Debug 构建/复制辅助逻辑

MyParser/                             # 主插件项目：Shirobot.Plugin.MyParser
  MyParserPlugin.cs                   # 插件入口、provider 发现与调度
  ProviderHostServices.cs             # 向 provider 暴露宿主能力
  LocalVideoHttpServer.cs             # 本地媒体 HTTP 服务
  MessageHandling/Common/             # 主插件通用消息/图片/并发工具
  Services/                           # 下载器、进度日志等宿主通用服务
  Utility/                            # 宿主侧工具函数

MyParser.Sharing/                     # provider 共享抽象层，输出 myparser-sharing.dll
  Parsing/                            # provider contract、DTO、base class、注册表
  PluginConfig.cs                     # 插件配置模型
  Utility/MyParserRuntime.cs          # 轻量运行时状态工具

MyParser.Provider.BiliBili/           # Bilibili provider，输出 myparser-provider-bilibili.dll
MyParser.Provider.Douyin/             # 抖音 provider，输出 myparser-provider-douyin.dll
MyParser.Provider.Xiaohongshu/        # 小红书 provider，输出 myparser-provider-xiaohongshu.dll
MyParser.Provider.Heybox/             # 小黑盒 provider，输出 myparser-provider-heybox.dll
```

主插件不直接引用具体 provider 实现；运行时会从插件目录下的 `provider/` 子目录加载 `myparser-provider-*.dll`，并通过 `IMyParserProviderModule` 发现各平台能力。

### 模块职责

- `MyParser`：保留 ShiroBot 插件入口、消息调度、下载、上传、渲染、本地 HTTP、缓存和清理等宿主能力。
- `MyParser.Sharing`：只放 provider contract、DTO、轻量 base class 和纯工具，不承载主插件具体业务实现。
- `MyParser.Provider.*`：只负责平台差异，包括 URL 识别、平台解析、平台模型、登录/Cookie 抽象、平台消息处理、平台卡片 View/ViewModel 等。

## Provider 开发流程

新增或维护 provider 时，建议按下面的流程处理。

### 1. 创建 provider 项目

provider 项目统一放在仓库根目录，命名为：

```text
MyParser.Provider.<Platform>/
```

项目文件建议保持以下约定：

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <AssemblyName>myparser-provider-<platform></AssemblyName>
  <RootNamespace>MyParser.Provider.<Platform></RootNamespace>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="..\MyParser.Sharing\MyParser.Sharing.csproj" />
</ItemGroup>
```

如 provider 需要 Avalonia 卡片渲染，再引用：

```xml
<PackageReference Include="ShiroBot.AvaloniaSdk" />
```

如 provider 需要二维码能力，再引用：

```xml
<PackageReference Include="Net.Codecrete.QrCodeGenerator" />
```

包版本统一在 `Directory.Packages.props` 中维护，provider 项目里不要单独写版本号。

### 2. 实现 provider module

每个 provider 需要提供一个实现 `IMyParserProviderModule` 的 module 类型，推荐继承 `MyParserProviderModuleBase`：

```csharp
public sealed class ExampleProviderModule : MyParserProviderModuleBase
{
    public override string Id => "example";
    public override string DisplayName => "Example";

    public override IEnumerable<IParseProvider> CreateProviders(PluginConfig config)
    {
        yield return new ExampleParseProvider(config);
    }
}
```

如果 provider 需要接管消息发送流程，实现 `IProviderMessageHandlerFactory`，并返回平台自己的 `IProviderMessageHandler`：

```csharp
public override IProviderMessageHandler CreateMessageHandler(ProviderMessageHandlerContext context)
{
    return new ExampleMessageHandler(context);
}
```

运行时主插件会扫描 `provider/myparser-provider-*.dll`，自动创建 module，不需要在主插件中手动 `new` provider 类型。

### 3. 实现解析与平台差异

provider 内部只放平台相关逻辑，常见目录如下：

```text
Parsing/          # URL 识别、ParseProvider、Parser
Models/           # 平台响应 DTO 和解析结果模型
Infrastructure/   # 平台 HTTP client、headers、签名等基础设施
Services/         # 平台解析服务
MessageHandling/  # 平台消息处理入口
MessageHandling/Impl/ # 较大的 partial handler 实现
Utilities/        # 平台专用纯工具
Views/            # Avalonia View 和 ViewModel
```

目录不是强制的。一两个文件不需要单独建目录；大型 handler 建议用 `partial` 拆到 `MessageHandling/Impl/`。

### 4. 使用 HostServices，不反向依赖主插件实现

provider 不应直接引用主插件中的具体实现，也不要在主插件里直接引用 provider 具体类型。需要宿主能力时，通过 `IProviderHostServices` 使用：

- 回复文本、发送图片、发送 reaction。
- 下载、Range 下载、进度日志和缓存。
- 上传本地视频文件。
- 注册/注销本地视频 HTTP 文件。
- 远程图片获取与图片解码。
- 并发保序选择工具。
- 临时文件清理策略。

如果发现多个 provider 重复实现相同宿主能力，优先把能力下放到 `MyParser/ProviderHostServices.cs` 或共享 contract，而不是复制到各 provider。

### 5. 保持 Sharing 轻量

`MyParser.Sharing` 只能放 provider 需要共享的抽象和轻量工具，例如：

- provider contract / interface
- DTO / record
- base class
- 纯工具方法

不要把下载器、本地 HTTP 服务、上传实现、渲染实现、消息发送实现等宿主业务放进 `MyParser.Sharing`。

### 6. 接入 solution、构建和运行时复制

新增公开 provider 后，需要同步更新：

- `MyParser.sln`：把 provider 项目加入 `Providers` solution folder。
- `Directory.Build.targets`：把 provider csproj 加入 `MyParserProviderProject`，方便本地 Debug 构建时自动复制。
- `.github/workflows/build.yml`：把 provider 路径和产物检查加入 CI。
- `.github/workflows/release.yml`：把 provider dll 加入 release 包。

本地调试时，`Directory.Build.local.props` 中配置好 `HostPluginDir` 后，构建主插件或 provider 会把产物复制到：

```text
plugins/Shirobot.Plugin.MyParser/
plugins/Shirobot.Plugin.MyParser/provider/
```

### 7. 验证 checklist

提交前建议至少检查：

```bash
dotnet build MyParser.sln -c Release -p:CopyPluginToHost=false -p:BuildMyParserProvidersOnMainPluginBuild=false
```

并确认：

- 主插件不直接引用 provider 具体类型。
- provider 只引用 `MyParser.Sharing` 和必要 NuGet 包，不引用主插件项目。
- provider dll 名称符合 `myparser-provider-*.dll`。
- 启动日志能看到 provider module 和 provider 能力概览。
- 没有提交 Cookie、Token、本机路径、`Directory.Build.local.props`、`launchSettings.json`、`bin/`、`obj/` 等本地文件。

## 构建

还原与构建整个 solution：

```bash
dotnet restore MyParser.sln
dotnet build MyParser.sln -c Release -p:CopyPluginToHost=false -p:BuildMyParserProvidersOnMainPluginBuild=false
```

也可以单独构建主插件或某个 provider：

```bash
dotnet build MyParser/Shirobot.Plugin.MyParser.csproj -c Release -p:CopyPluginToHost=false
dotnet build MyParser.Provider.BiliBili/MyParser.Provider.BiliBili.csproj -c Release -p:CopyPluginToHost=false
dotnet build MyParser.Provider.Douyin/MyParser.Provider.Douyin.csproj -c Release -p:CopyPluginToHost=false
dotnet build MyParser.Provider.Xiaohongshu/MyParser.Provider.Xiaohongshu.csproj -c Release -p:CopyPluginToHost=false
dotnet build MyParser.Provider.Heybox/MyParser.Provider.Heybox.csproj -c Release -p:CopyPluginToHost=false
```

Release 构建产物主要位于：

```text
MyParser/bin/Release/net10.0/Shirobot.Plugin.MyParser.dll
MyParser/bin/Release/net10.0/myparser-sharing.dll
MyParser.Provider.BiliBili/bin/Release/net10.0/myparser-provider-bilibili.dll
MyParser.Provider.Douyin/bin/Release/net10.0/myparser-provider-douyin.dll
MyParser.Provider.Xiaohongshu/bin/Release/net10.0/myparser-provider-xiaohongshu.dll
MyParser.Provider.Heybox/bin/Release/net10.0/myparser-provider-heybox.dll
```

### 本地调试配置

本项目区分 **运行配置** 和 **构建输出配置**：

- `.env`：给 Rider / 本地运行配置读取，主要用于指定要启动的 ShiroBot 宿主。
- `Directory.Build.local.props`：给 MSBuild 读取，用于把 Debug 构建产物直接输出到 ShiroBot 插件目录。

这两个文件都属于本机私有配置，不应提交到仓库；`.gitignore` 已忽略它们。

`.env` 示例：

```env
# Rider / 本地运行配置使用：启动这个 ShiroBot 宿主。
HOST_EXE=C:\Path\To\ShiroBot\bin\Debug\net10.0\ShiroBot.exe

# 可选：给运行配置或脚本识别插件目录；MSBuild 是否输出到这里，以 Directory.Build.local.props 为准。
HOST_PLUGIN_DIR=C:\Path\To\ShiroBot\bin\Debug\net10.0\plugins\Shirobot.Plugin.MyParser
```

`Directory.Build.local.props` 示例：

```xml
<Project>
  <PropertyGroup>
    <HostPluginDir>C:\Path\To\ShiroBot\bin\Debug\net10.0\plugins\Shirobot.Plugin.MyParser\</HostPluginDir>
  </PropertyGroup>
</Project>
```

配置好后，普通 Debug 构建会直接生成到宿主插件目录：

```bash
dotnet build -c Debug
```

> 注意：Rider 的 `.env` 通常只注入运行进程，不一定会注入 MSBuild 项目评估阶段。因此不要只依赖 `.env` 控制插件输出目录；请使用 `Directory.Build.local.props` 指定 `HostPluginDir`。

## 热重载

插件运行时会监听以下文件变化并自动重载：

- 插件目录下的 `config.toml`：更新普通运行配置，例如自动解析开关、下载策略、sign 服务等。
- 插件目录下的 `cookies/douyin.txt`、`cookies/bilibili.txt`、`cookies/xiaohongshu.txt`：更新各平台 Cookie。

Cookie 文件清空或删除后，对应平台登录态会立即清空；文件重新创建或写入有效 Cookie 后会自动生效。

> 固定命令（如 `#bili-login`、`#xhs-login`、`#douyin-cookie-check`、`#bili-cookie-check`、`#xhs-cookie-check`）不走配置项。`#parse` 前缀支持随配置热重载。

## 部署

插件目录建议保持如下结构：

```text
plugins/Shirobot.Plugin.MyParser/
  Shirobot.Plugin.MyParser.dll
  myparser-sharing.dll
  QrCodeGenerator.dll
  config.toml                    # 可由 ShiroBot/插件运行时生成或维护
  cookies/                       # 本地 Cookie 文件目录，不要提交到仓库
    bilibili.txt
    douyin.txt
    xiaohongshu.txt
  provider/
    myparser-provider-bilibili.dll
    myparser-provider-douyin.dll
    myparser-provider-xiaohongshu.dll
    myparser-provider-heybox.dll
```

部署步骤：

1. 构建 Release 版本，或下载 GitHub Release 中的 `Shirobot.Plugin.MyParser.zip`。
2. 将主插件文件复制到 [ShiroBot](https://github.com/ShirokaProject/ShiroBot) 插件目录 `plugins/Shirobot.Plugin.MyParser/`。
3. 将 provider dll 复制到 `plugins/Shirobot.Plugin.MyParser/provider/`。
4. 在 [ShiroBot](https://github.com/ShirokaProject/ShiroBot) 配置中启用插件。
5. 按需配置命令前缀、下载目录、超时、登录态、sign 服务和媒体发送策略。
6. 重启或热重载 [ShiroBot](https://github.com/ShirokaProject/ShiroBot)。

启动时插件会打印 provider 能力概览。如果日志提示未发现 provider module，请优先检查：

- `plugins/Shirobot.Plugin.MyParser/provider/` 是否存在 `myparser-provider-*.dll`。
- `myparser-sharing.dll` 是否与 provider dll 来自同一次构建。
- 是否有旧的 ShiroBot 进程或调试器锁住插件 dll，导致文件没有被成功覆盖。

## GitHub Actions

仓库包含两个工作流：

- `.github/workflows/build.yml`：当主插件、共享库、provider 或构建配置发生变化时，在 `main` / `master` push、PR 和手动触发时执行 Release 构建，并上传 `myparser-build` artifact。
- `.github/workflows/release.yml`：推送 `v*` tag 或手动触发时，构建并打包 `Shirobot.Plugin.MyParser.zip`，包内包含主插件、`myparser-sharing.dll` 和 provider dll。

工作流只构建公开仓库中的 provider 项目，不包含本地私有项目或本机路径配置。

部署建议：

- 不要把 Cookie、Token、账号信息和本地配置提交到公开仓库。
- 生产环境建议保持本地路径展示关闭。
- 大文件消息建议优先使用本地 HTTP 服务或文件发送 fallback，避免超大消息造成内存压力。
- 本地 HTTP 服务默认只允许本机访问；如确需局域网访问，需显式开启 `AllowLanAccessToLocalVideoHttpServer`。
- `DeleteLocalVideoAfterSend` 默认开启，发送完成后会清理插件下载目录内的本地视频文件。
- 不建议将本项目部署为面向公众开放的解析 / 下载服务。

## 许可证

本项目使用 GNU General Public License v3.0。
详见 [LICENSE](./LICENSE)。

## 合规与免责声明

本项目仅用于技术学习、技术交流、插件开发学习和个人实验，不构成法律意见。使用者必须自行确认使用行为具备合法授权，并自行承担由使用行为产生的全部责任。

本项目与抖音、Bilibili、小红书或其他第三方内容平台、权利方、服务提供方均不存在从属、合作、授权或背书关系。本项目不提供、托管、索引、售卖或分发任何第三方内容，也不保证可访问、获取或处理任何第三方内容。

本项目的使用必须遵守以下边界：

- **不得用于侵权用途**：不得下载、复制、传播、分发、展示或保存未获授权的第三方内容。
- **不得用于规避技术保护措施**：不得绕过 DRM、付费墙、会员限制、验证码、访问控制、加密保护或其他技术保护措施。
- **不得作为公开代下服务**：不得将本项目部署为面向公众的解析、下载、转存、分发或镜像服务。
- **不得批量抓取或滥用请求**：不得用于高频请求、批量采集、镜像站点、数据售卖，或任何可能影响第三方服务稳定性的行为。
- **不得移除权利信息**：不得删除、隐藏或篡改版权声明、作者信息、水印、来源标识或其他权利管理信息。
- **不得提交敏感信息**：不得将 Cookie、Token、个人账号、群号、聊天记录、本地配置或其他敏感信息提交到公开仓库。
- **必须遵守第三方规则**：使用者必须自行阅读并遵守目标网站的用户协议、robots 规则、版权政策、隐私政策和当地法律法规。
- **必须仅处理合法授权内容**：使用者只能处理自己拥有权利、已获授权、许可条款允许或法律允许合理使用的内容。

因使用者自行部署、修改、分发、公开服务化或以其他方式使用本项目而产生的任何版权、合同、隐私、数据安全、账号安全、服务条款或其他法律责任，均由使用者自行承担。

如权利人认为本项目中的说明、示例或实现方式存在侵权风险，可以通过 GitHub Issues 联系本项目维护者处理；如认为存在需要正式处理的侵权内容，也可以按照 GitHub 的 DMCA 流程向 GitHub 提交通知。
