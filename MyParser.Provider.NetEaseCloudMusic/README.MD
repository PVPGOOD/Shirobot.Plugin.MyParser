# MyParser.Provider.NetEaseCloudMusic

网易云音乐 provider，负责网易云歌曲链接、短链、搜索结果选择、歌曲信息卡片、歌词卡片和 QQ 语音发送。

## 功能

- 支持网易云音乐歌曲链接解析，例如 `https://music.163.com/song?id=<id>`。
- 支持 `163cn.tv` 网易云短链展开。
- 支持 `#wyy <歌名/歌手>` 搜索歌曲，并通过回复搜索结果序号继续解析。
- 支持歌曲封面卡片渲染。
- 支持歌词卡片渲染。
- 支持获取音频直链并下载到本地。
- 支持将歌曲转换为 QQ 语音 / SILK RecordSegment 发送。
- 支持发送电脑兼容版语音，并可配置额外发送手机高音质版。
- 支持语音发送失败时回退为文件上传或文本提示。
- 支持扫码登录、Cookie 保存和 Cookie 状态检查。

## 命令

```text
#wyy <歌名/歌手>
#wyy-login
#wyy-cookie-check
```

说明：

- `#wyy <歌名/歌手>`：搜索网易云歌曲，机器人返回候选列表后，回复序号即可解析并发送 QQ 语音。
- `#wyy-login`：网易云音乐扫码登录，仅允许 Owner/Admin 私信使用。
- `#wyy-cookie-check`：检查网易云音乐 Cookie 状态，仅允许 Owner/Admin 私信使用。

## 登录与 Cookie

网易云音乐支持插件内扫码登录：

```text
#wyy-login
```

流程：

1. Owner/Admin 私信机器人发送 `#wyy-login`。
2. 使用网易云音乐手机 App 扫描机器人发送的二维码。
3. 在 App 中确认登录。
4. 插件会将 Cookie 保存到运行时配置和插件目录下的 `cookies/netease.txt`。

也可以手动获取 Cookie：

1. 在浏览器打开并登录网易云音乐网页版。
2. 打开开发者工具，进入 Network / 网络面板。
3. 刷新页面或打开任意歌曲请求。
4. 复制请求头中的完整 `Cookie`。
5. 写入插件目录下的 `cookies/netease.txt`。
6. 重启或等待插件热重载。

Cookie 建议包含：

```text
MUSIC_U=...; __csrf=...; NMTID=...
```

无 Cookie 时仍可尝试搜索和解析部分免费歌曲；VIP、版权受限或高音质音频通常需要有效 Cookie，且仍可能受账号权限限制。

检查 Cookie：

```text
#wyy-cookie-check
```

## 配置

常用配置：

```json
{
  "AutoParseNetEaseCloudMusicLinks": true,
  "SendNetEaseMobileBestRecord": false
}
```

说明：

- `AutoParseNetEaseCloudMusicLinks`：是否自动解析聊天中的网易云音乐歌曲链接。
- `SendNetEaseMobileBestRecord`：发送网易云音乐语音时是否额外发送手机高音质版；关闭时默认只发送电脑兼容版。

## 注意事项

- 音频直链获取受版权、地区、账号权限、Cookie 状态和网易云接口策略影响。
- QQ 语音发送依赖本地下载、音频处理和 SILK 编码流程；失败时会尝试文件上传 fallback。
- 请勿将 `cookies/netease.txt` 提交到仓库或公开日志。
