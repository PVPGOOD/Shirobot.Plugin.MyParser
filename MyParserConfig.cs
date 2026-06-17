namespace Shirobot.Plugin.MyParser;

public sealed class MyParserConfig
{
    public bool AutoParseDouyinLinks { get; set; } = true;

    public bool AutoParseBilibiliLinks { get; set; } = true;

    public bool AutoParseXiaohongshuLinks { get; set; } = false;

    public string ParseCommandPrefix { get; set; } = "#parse";

    public string DouyinCookie { get; set; } = string.Empty;

    public string DouyinCookieFileName { get; set; } = "douyin_cookie.txt";

    public bool CreateDouyinCookieFileIfMissing { get; set; } = true;

    public bool CheckDouyinCookieLoginStatusOnStartup { get; set; } = true;

    public string BilibiliCookie { get; set; } = string.Empty;

    public string BilibiliCookieFileName { get; set; } = "bilibili_cookie.txt";

    public bool CreateBilibiliCookieFileIfMissing { get; set; } = true;

    public bool CheckBilibiliCookieLoginStatusOnStartup { get; set; } = true;

    public string BilibiliLoginCommand { get; set; } = "#bili-login";

    public string DouyinCookieCheckCommand { get; set; } = "#douyin-cookie-check";

    public string BilibiliCookieCheckCommand { get; set; } = "#bili-cookie-check";

    public string XiaohongshuCookieCheckCommand { get; set; } = "#xhs-cookie-check";

    public string BilibiliDownloadDirectory { get; set; } = "downloads/MyParser/bilibili";

    public string XiaohongshuCookie { get; set; } = string.Empty;

    public string XiaohongshuCookieFileName { get; set; } = "xiaohongshu_cookie.txt";

    public bool CreateXiaohongshuCookieFileIfMissing { get; set; } = true;

    public bool CheckXiaohongshuCookieLoginStatusOnStartup { get; set; } = false;

    public string XiaohongshuLoginCommand { get; set; } = "#xhs-login";

    public string XiaohongshuDownloadDirectory { get; set; } = "downloads/MyParser/xiaohongshu";

    public string XiaohongshuSignServerUrl { get; set; } = string.Empty;

    public string XiaohongshuSignServerToken { get; set; } = string.Empty;

    public bool XiaohongshuFetchComments { get; set; } = true;

    public int XiaohongshuCommentCount { get; set; } = 10;

    public string FfmpegPath { get; set; } = string.Empty;

    public string FfprobePath { get; set; } = string.Empty;

    public bool PreferHighFps { get; set; } = true;

    public bool PreferH265 { get; set; } = false;

    public bool PreferCompatibleVideoSegment { get; set; } = false;

    public int VideoSegmentMaxLongSide { get; set; } = 1920;

    public int VideoSegmentMaxFps { get; set; } = 60;

    public bool PreferH264ForVideoSegment { get; set; } = true;

    public bool LogSelectedQualityInfo { get; set; } = true;

    public int RequestTimeoutSeconds { get; set; } = 15;

    public bool QuoteReply { get; set; } = false;

    public bool IncludeCoverUrl { get; set; } = true;

    public bool SendVideoAsFile { get; set; } = true;

    public bool SendVideoSegmentAsBase64 { get; set; } = true;

    public int VideoSegmentBase64MaxMegabytes { get; set; } = 80;

    public bool IncludeVideoThumbUri { get; set; } = false;

    public bool SendCoverWithVideoSegment { get; set; } = true;

    public bool UploadVideoAsFile { get; set; } = false;

    public bool UploadVideoAsFileOnlyOnVideoSendFailure { get; set; } = true;

    public bool TreatZeroMessageSeqAsVideoSendFailure { get; set; } = true;

    public bool UseLocalHttpServerForLargeVideoSegment { get; set; } = true;

    public string LocalVideoHttpHost { get; set; } = "127.0.0.1";

    public int LocalVideoHttpPort { get; set; } = 0;

    public string LocalVideoHttpPublicBaseUrl { get; set; } = string.Empty;

    public bool AllowLanAccessToLocalVideoHttpServer { get; set; } = false;

    public bool DeleteLocalVideoAfterSend { get; set; } = true;

    public bool UploadVideoAsBase64 { get; set; } = true;

    public int UploadVideoBase64MaxMegabytes { get; set; } = 80;

    public bool IncludeRawMediaUrls { get; set; } = false;

    public int MaxVideoDownloadMegabytes { get; set; } = 5120;

    public bool AutoFallbackQualityBySize { get; set; } = false;

    public bool LogDownloadProgress { get; set; } = true;

    public int DownloadProgressLogIntervalSeconds { get; set; } = 2;

    public bool EnableParallelVideoDownload { get; set; } = true;

    public int ParallelDownloadSegments { get; set; } = 16;

    public int ParallelDownloadMaxSegments { get; set; } = 32;

    public int ParallelDownloadMinMegabytes { get; set; } = 3;

    public string DownloadDirectory { get; set; } = "downloads/MyParser/douyin";

    public bool IncludeLocalFilePath { get; set; } = false;

    public int MaxImagesToShow { get; set; } = 6;
}
