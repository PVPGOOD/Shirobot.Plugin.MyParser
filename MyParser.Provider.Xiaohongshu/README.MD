# MyParser.Provider.Xiaohongshu

小红书 provider，负责小红书笔记、图文、视频和评论相关内容解析。

## 功能

- 支持小红书笔记解析。
- 支持视频、图文图片、正文和评论提取。
- 支持前 10 条评论获取与图文问答卡片渲染。
- 支持外部 sign 服务集成。
- 支持 Cookie 登录态检查和实验性扫码登录。

## 命令

```text
#xhs-login
#xhs-cookie-check
```

以上命令仅允许机器人 Owner/Admin 私信使用。

## Sign 服务配置

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

## 登录与 Cookie

扫码登录：

```text
#xhs-login
```

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
8. 重启或等待插件热重载。

小红书 Cookie 建议包含：

```text
a1=...; web_session=...; webId=...; xsecappid=xhs-pc-web
```

其中 `web_session` 用于登录态，`a1` / `webId` / `xsecappid` 会参与签名服务请求。缺少这些字段时，评论区、搜索恢复 `xsec_token`、作者作品列表等接口可能无法正常使用。

检查小红书 Cookie 有效性：

```text
#xhs-cookie-check
```

## 注意事项

- 评论区接口通常需要登录 Cookie、`xsec_token` 和 sign 服务。
- 分享链接缺少 `xsec_token` 时，插件会尽量通过作者作品列表或搜索恢复，但该能力受平台风控和页面结构影响，不保证每次成功。
- 请勿将 sign 服务 token 或 `cookies/xiaohongshu.txt` 提交到仓库或公开日志。
