# MyParser.Provider.BiliBili

Bilibili provider，负责 Bilibili 视频、分 P、番剧、专栏 / 图文和直播解析。

## 功能

- 支持普通 BV / av 视频解析。
- 支持封面卡片渲染。
- 支持 DASH 音视频下载、本地 ffmpeg 合并与 QQ VideoSegment 发送。
- 支持分 P 视频识别：
  - 不带 `p=` 的分 P BV 默认发送分 P 总览。
  - 带 `?p=4` 的链接会解析并发送指定 P 视频。
  - 分 P 总览中展示每 P 标题、封面、时长和链接，并可通过回复数字解析指定分 P。
- 支持专栏、图文 / opus 解析，正文通过合并转发发送，并支持长文档图片渲染。
- 支持番剧 `md` / `ss` / `ep` 链接解析：
  - `md` / `ss` 展示作品信息和全集列表。
  - `ep` 会先展示番剧信息与全集列表，再解析并发送当前单集视频。
- 支持 Bilibili 直播原站解析：可识别 `live.bilibili.com` 直播间链接和跳转到直播间的 `b23.tv` 短链。
- 支持 Bilibili 直播卡片渲染，展示直播间标题、主播、头像、封面、状态、房间观众、人气 / 看过、开播时间和直播时长。
- 支持直播短回溯：从当前 HLS 可回溯分片生成短 MP4，并发送到 QQ。
- 支持 QQ 轻应用 / 小程序 payload 中的 Bilibili 链接提取。

## 命令

```text
#bili-login
#bili-cookie-check
```

以上命令仅允许机器人 Owner/Admin 私信使用。

## 登录与 Cookie

Bilibili 支持插件内扫码登录：

```text
#bili-login
```

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

## 注意事项

- 视频下载、合并和直播回溯依赖本地网络、Cookie 状态、目标内容权限和 `ffmpeg`。
- 直播接口、番剧接口和专栏 / opus 页面结构可能随 Bilibili 调整而变化。
- 请勿将 `cookies/bilibili.txt` 提交到仓库或公开日志。
