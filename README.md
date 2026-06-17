<div align="center">

<p><strong><span style="font-size: 2.2em;">Shirobot.Plugin.MyParser</span></strong></p>

<p><em>一个面向 <a href="https://github.com/ShirokaProject/ShiroBot">ShiroBot</a> 的抖音 / Bilibili / 小红书内容解析与转发插件。</em></p>

</div>

## 简介

Shirobot.Plugin.MyParser 是 [ShiroBot](https://github.com/ShirokaProject/ShiroBot) 生态中的学习型插件项目，用于个人学习、技术交流和合法合规的插件开发实践。

当前支持抖音、Bilibili、小红书内容解析，包括视频、图集、图文、专栏、番剧、直播、封面卡片渲染、长文档图片渲染、评论卡片和消息转发流程。

> 维护说明：本项目主要服务个人使用和学习验证，属于随缘维护项目。第三方平台页面结构、接口、风控策略随时可能变化，因此不保证长期稳定、实时适配或及时修复。

## 功能

- 自动识别聊天中的抖音 / Bilibili / 小红书链接。
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
5. 写入插件目录下的 `douyin_cookie.txt`，或写入运行时配置项 `DouyinCookie`。
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
4. 插件会将 Cookie 保存到运行时配置和插件目录下的 `bilibili_cookie.txt`。

也可以手动获取 Cookie：

1. 在浏览器打开并登录 Bilibili 网页端。
2. 打开开发者工具，进入 Network / 网络面板。
3. 刷新页面，选择任意 `bilibili.com` 或 `api.bilibili.com` 请求。
4. 复制请求头中的完整 `Cookie`。
5. 写入 `bilibili_cookie.txt`，或写入运行时配置项 `BilibiliCookie`。

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
4. 如果流程可用，插件会将 Cookie 保存到运行时配置和插件目录下的 `xiaohongshu_cookie.txt`。

推荐方式：浏览器复制小红书 Cookie：

1. 在浏览器打开并登录小红书网页版。
2. 打开开发者工具，进入 Network / 网络面板。
3. 刷新页面，或打开任意笔记详情页。
4. 选择 `xiaohongshu.com` 或 `edith.xiaohongshu.com` 相关请求。
5. 在 Request Headers / 请求头中找到 `Cookie`。
6. 复制 `Cookie:` 后面的完整值。
7. 写入插件目录下的 `xiaohongshu_cookie.txt`，或写入运行时配置项 `XiaohongshuCookie`。
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

## 配置

插件通过 [ShiroBot](https://github.com/ShirokaProject/ShiroBot) 配置上下文读取 `MyParserConfig`。常用配置示例：

```json
{
  "AutoParseDouyinLinks": true,
  "AutoParseBilibiliLinks": true,
  "AutoParseXiaohongshuLinks": false,
  "ParseCommandPrefix": "#parse",
  "BilibiliLoginCommand": "#bili-login",
  "XiaohongshuLoginCommand": "#xhs-login",
  "DouyinCookieCheckCommand": "#douyin-cookie-check",
  "BilibiliCookieCheckCommand": "#bili-cookie-check",
  "XiaohongshuCookieCheckCommand": "#xhs-cookie-check",
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
  "IncludeLocalFilePath": false,
  "MaxImagesToShow": 6
}
```

## 项目结构

```text
Parsing/                 # 解析提供者抽象与注册表
Providers/               # 平台解析实现、模型、视图和消息处理
Services/                # 下载器、进度日志等通用服务
Utility/                 # 通用工具函数
MyParserConfig.cs        # 插件配置
MyParserPlugin.cs        # 插件入口
LocalVideoHttpServer.cs  # 本地媒体 HTTP 服务
```

## 构建

还原与构建：

```bash
dotnet build -c Release
```

发布：

```bash
dotnet publish -c Release
```

调试时如需自动复制到本机 [ShiroBot](https://github.com/ShirokaProject/ShiroBot) 宿主目录，可以使用项目根目录下的 `.env` 指定宿主程序路径。`.env` 已被 `.gitignore` 忽略。

`.env` 示例：

```env
HOST_EXE=C:\Path\To\ShiroBot\bin\Debug\net10.0\ShiroBot.exe
```

也可以使用 MSBuild 属性覆盖：

```bash
dotnet build -c Debug -p:HostExe="C:\Path\To\ShiroBot\bin\Debug\net10.0\ShiroBot.exe"
```

或使用宿主项目根目录：

```bash
dotnet build -c Debug -p:HostProjectRoot="C:\Path\To\ShiroBot\"
```

## 部署

1. 构建 Release 版本。
2. 将生成的插件文件复制到 [ShiroBot](https://github.com/ShirokaProject/ShiroBot) 插件目录。
3. 在 [ShiroBot](https://github.com/ShirokaProject/ShiroBot) 配置中启用插件。
4. 按需配置命令前缀、下载目录、超时、登录态、sign 服务和媒体发送策略。
5. 重启或热重载 [ShiroBot](https://github.com/ShirokaProject/ShiroBot)。

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
