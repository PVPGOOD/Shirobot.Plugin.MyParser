# MyParser.Provider.Douyin

抖音 provider，负责抖音视频、图集和 LivePhoto 等内容解析。

## 功能

- 支持抖音视频解析与转发。
- 支持图集、LivePhoto 等多媒体内容解析。
- 支持作品封面 / 信息卡片渲染。
- 支持图文图片合并转发。
- 支持图文背景音乐 RecordSegment 发送。
- 支持 Cookie 登录态检查。
- 支持解析开始、成功、失败状态表情。

## 命令

```text
#douyin-cookie-check
```

该命令仅允许机器人 Owner/Admin 私信使用。

## Cookie

抖音目前不提供插件内扫码登录命令，需要手动提供 Cookie：
[README.md](../README.md)
1. 在浏览器打开并登录抖音网页端。
2. 打开浏览器开发者工具，进入 Network / 网络面板。
3. 刷新页面或打开任意作品请求。
4. 在请求头中找到 `Cookie`，复制 `Cookie:` 后面的完整值。
5. 写入插件目录下的 `cookies/douyin.txt`。
6. 重启或等待插件热重载。

建议 Cookie 至少包含 `sessionid`、`ttwid` 等字段。Cookie 属于账号凭据，不要提交到仓库、日志、截图或公开聊天。

检查抖音 Cookie 有效性：

```text
#douyin-cookie-check
```

## 注意事项

- 抖音页面结构、接口签名和风控策略变化较快，解析稳定性依赖当前接口状态。
- 部分内容可能需要登录态、地区权限或账号权限。
- 请勿将 `cookies/douyin.txt` 提交到仓库或公开日志。
