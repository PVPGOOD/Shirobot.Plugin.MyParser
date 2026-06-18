using ShiroBot.SDK.Config;

namespace Shirobot.Plugin.MyParser;

public sealed class MyParserConfig
{
    [ConfigField("ffmpeg 可执行文件路径。留空时自动从 PATH 或程序目录查找。", Label = "ffmpeg 路径", Placeholder = "ffmpeg")]
    public string FfmpegPath { get; set; } = string.Empty;

    [ConfigField("ffprobe 可执行文件路径。留空时自动从 PATH 或程序目录查找。", Label = "ffprobe 路径", Placeholder = "ffprobe")]
    public string FfprobePath { get; set; } = string.Empty;
    
    [ConfigField("是否自动解析聊天中的抖音链接。", Label = "自动解析抖音链接")]
    public bool AutoParseDouyinLinks { get; set; } = true;

    [ConfigField("是否自动解析聊天中的 Bilibili 链接。", Label = "自动解析 Bilibili 链接")]
    public bool AutoParseBilibiliLinks { get; set; } = true;

    [ConfigField("是否自动解析聊天中的小红书链接。", Label = "自动解析小红书链接")]
    public bool AutoParseXiaohongshuLinks { get; set; } = false;

    [ConfigField("手动解析命令前缀，例如 #parse <链接>。", Label = "解析命令前缀", Placeholder = "#parse")]
    public string ParseCommandPrefix { get; set; } = "#parse";

    [ConfigField("小红书 xhshow sign 服务地址。留空时小红书部分接口不可用。", Label = "小红书 Sign 服务地址", Placeholder = "https://127.0.0.1:xxxx")]
    public string XiaohongshuSignServerUrl { get; set; } = string.Empty;

    [ConfigField("小红书 xhshow sign 服务 Token。请只在可信环境中配置。", Label = "小红书 Sign Token", Type = "password")]
    public string XiaohongshuSignServerToken { get; set; } = string.Empty;

    [ConfigField("解析小红书图文时是否尝试获取评论。需要 Cookie、xsec_token 和 sign 服务。", Label = "获取小红书评论")]
    public bool XiaohongshuFetchComments { get; set; } = true;

    [ConfigField("小红书评论最多获取条数。", Label = "小红书评论数量", Min = 0, Max = 50)]
    public int XiaohongshuCommentCount { get; set; } = 10;

    [ConfigField("选择视频流时优先更高帧率。", Label = "优先高帧率")]
    public bool PreferHighFps { get; set; } = true;

    [ConfigField("选择视频流时优先 H.265。关闭时更偏向兼容性。", Label = "优先 H.265")]
    public bool PreferH265 { get; set; } = false;

    [ConfigField("发送 VideoSegment 前优先选择更兼容的编码/规格。", Label = "优先兼容 VideoSegment")]
    public bool PreferCompatibleVideoSegment { get; set; } = false;

    [ConfigField("VideoSegment 推荐最大长边像素。", Label = "VideoSegment 最大长边", Min = 240, Max = 7680)]
    public int VideoSegmentMaxLongSide { get; set; } = 1920;

    [ConfigField("VideoSegment 推荐最大帧率。", Label = "VideoSegment 最大帧率", Min = 15, Max = 240)]
    public int VideoSegmentMaxFps { get; set; } = 60;

    [ConfigField("发送 VideoSegment 时优先 H.264 以提高兼容性。", Label = "VideoSegment 优先 H.264")]
    public bool PreferH264ForVideoSegment { get; set; } = true;

    [ConfigField("是否在日志中输出选择到的视频清晰度与编码信息。", Label = "记录画质选择日志")]
    public bool LogSelectedQualityInfo { get; set; } = true;

    [ConfigField("网络请求超时时间，单位秒。", Label = "请求超时秒数", Min = 5, Max = 300)]
    public int RequestTimeoutSeconds { get; set; } = 15;

    [ConfigField("回复解析结果时是否引用原消息。", Label = "引用回复")]
    public bool QuoteReply { get; set; } = false;

    [ConfigField("文本解析结果中是否包含封面 URL。", Label = "包含封面链接")]
    public bool IncludeCoverUrl { get; set; } = true;

    [ConfigField("是否下载并发送视频文件/视频消息。关闭后只发送解析文本或卡片。", Label = "发送视频")]
    public bool SendVideoAsFile { get; set; } = true;

    [ConfigField("小视频是否优先用 base64:// 发送 VideoSegment。", Label = "VideoSegment 使用 Base64")]
    public bool SendVideoSegmentAsBase64 { get; set; } = true;

    [ConfigField("VideoSegment 使用 Base64 的最大文件大小，单位 MB。", Label = "VideoSegment Base64 上限 MB", Min = 1, Max = 512)]
    public int VideoSegmentBase64MaxMegabytes { get; set; } = 80;

    [ConfigField("VideoSegment 是否附带缩略图 URI。", Label = "包含视频缩略图")]
    public bool IncludeVideoThumbUri { get; set; } = false;

    [ConfigField("是否上传视频为群/私聊文件。", Label = "上传视频文件")]
    public bool UploadVideoAsFile { get; set; } = false;

    [ConfigField("仅当 VideoSegment 发送失败时才上传视频文件。", Label = "失败时才上传文件")]
    public bool UploadVideoAsFileOnlyOnVideoSendFailure { get; set; } = true;

    [ConfigField("VideoSegment 返回 message_seq <= 0 时视为发送失败并触发 fallback。", Label = "零消息序号视为失败")]
    public bool TreatZeroMessageSeqAsVideoSendFailure { get; set; } = true;

    [ConfigField("大视频无法 Base64 时是否使用插件本地 HTTP 服务提供临时 URL。", Label = "大视频使用本地 HTTP")]
    public bool UseLocalHttpServerForLargeVideoSegment { get; set; } = true;

    [ConfigField("本地视频 HTTP 服务监听地址。默认仅本机。", Label = "本地 HTTP 监听地址", Placeholder = "127.0.0.1")]
    public string LocalVideoHttpHost { get; set; } = "127.0.0.1";

    [ConfigField("本地视频 HTTP 服务端口。0 表示自动选择空闲端口。", Label = "本地 HTTP 端口", Min = 0, Max = 65535)]
    public int LocalVideoHttpPort { get; set; } = 0;

    [ConfigField("本地视频 HTTP 对外访问基础 URL。留空时按监听地址自动生成。", Label = "本地 HTTP 公网地址", Placeholder = "http://127.0.0.1:端口/myparser")]
    public string LocalVideoHttpPublicBaseUrl { get; set; } = string.Empty;

    [ConfigField("是否允许局域网客户端访问本地视频 HTTP 服务。默认关闭。", Label = "允许局域网访问本地 HTTP")]
    public bool AllowLanAccessToLocalVideoHttpServer { get; set; } = false;

    [ConfigField("解析 Bilibili 直播时是否尝试发送短回溯片段。", Label = "发送 Bilibili 直播回溯")]
    public bool SendBilibiliLiveReplayClip { get; set; } = true;

    [ConfigField("Bilibili 直播短回溯片段时长，单位秒。", Label = "直播回溯秒数", Min = 3, Max = 120)]
    public int BilibiliLiveReplayClipSeconds { get; set; } = 12;

    [ConfigField("Bilibili 直播短回溯片段最大下载大小，单位 MB。", Label = "直播回溯最大 MB", Min = 1, Max = 2048)]
    public int BilibiliLiveReplayClipMaxMegabytes { get; set; } = 256;

    [ConfigField("Bilibili 直播回溯 ffmpeg 超时时间，单位秒。", Label = "直播回溯 ffmpeg 超时", Min = 10, Max = 1800)]
    public int BilibiliLiveReplayClipFfmpegTimeoutSeconds { get; set; } = 240;

    [ConfigField("视频发送完成后是否清理插件 tmp 目录中的本地视频。", Label = "发送后删除本地视频")]
    public bool DeleteLocalVideoAfterSend { get; set; } = true;

    [ConfigField("发送完成后延迟多少秒删除本地视频。0 表示立即删除。", Label = "延迟删除秒数", Min = 0, Max = 86400)]
    public int DeleteLocalVideoDelaySeconds { get; set; } = 0;

    [ConfigField("上传视频文件时是否优先使用 base64://。", Label = "文件上传使用 Base64")]
    public bool UploadVideoAsBase64 { get; set; } = true;

    [ConfigField("文件上传使用 Base64 的最大文件大小，单位 MB。", Label = "文件上传 Base64 上限 MB", Min = 1, Max = 512)]
    public int UploadVideoBase64MaxMegabytes { get; set; } = 80;

    [ConfigField("解析结果中是否包含原始媒体直链。", Label = "包含原始媒体链接")]
    public bool IncludeRawMediaUrls { get; set; } = false;

    [ConfigField("Bilibili 分 P 总览最多展示封面数量。", Label = "Bilibili 分P封面上限", Min = 0, Max = 200)]
    public int BilibiliMultiPageCoverImageLimit { get; set; } = 50;

    [ConfigField("单个视频最大下载大小，单位 MB。", Label = "最大视频下载 MB", Min = 1, Max = 51200)]
    public int MaxVideoDownloadMegabytes { get; set; } = 5120;

    [ConfigField("视频超过大小限制时是否自动降级尝试较低画质。", Label = "超限自动降画质")]
    public bool AutoFallbackQualityBySize { get; set; } = false;

    [ConfigField("是否记录视频下载进度日志。", Label = "记录下载进度")]
    public bool LogDownloadProgress { get; set; } = true;

    [ConfigField("下载进度日志输出间隔，单位秒。", Label = "下载进度间隔秒", Min = 1, Max = 60)]
    public int DownloadProgressLogIntervalSeconds { get; set; } = 2;

    [ConfigField("并行下载默认分片数。", Label = "并行下载分片数", Min = 1, Max = 32)]
    public int ParallelDownloadSegments { get; set; } = 16;

    [ConfigField("并行下载最大分片数。", Label = "并行下载最大分片", Min = 1, Max = 64)]
    public int ParallelDownloadMaxSegments { get; set; } = 32;

    [ConfigField("图文内容最多发送或展示的图片数量。", Label = "最大图片数量", Min = 1, Max = 50)]
    public int MaxImagesToShow { get; set; } = 6;
}
