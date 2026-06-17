using System.Diagnostics;
using System.Net;
using System.Text;
using Net.Codecrete.QrCodeGenerator;
using ShiroBot.AvaloniaSdk;
using Shirobot.Plugin.MyParser.Parsing;
using Shirobot.Plugin.MyParser.Providers.Common.MessageHandling;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Facade;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Infrastructure;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Impl.Services;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Models;
using Shirobot.Plugin.MyParser.Providers.Bilibili.ViewModels;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Views;
using Shirobot.Plugin.MyParser.Utility;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Impl.MessageHandling.Impl;

internal sealed partial class BilibiliMessageHandler : IDisposable
{
    private static readonly HttpClient CoverHttp = RemoteImageFetchService.CreateImageHttpClient();

    private readonly IBotContext _context;
    private readonly MyParserConfig _config;
    private readonly ParseProviderRegistry _providerRegistry;
    private readonly BilibiliParseProvider _bilibiliProvider;
    private LocalVideoHttpServer? _localVideoHttpServer;

    public BilibiliMessageHandler(
        IBotContext context,
        MyParserConfig config,
        ParseProviderRegistry providerRegistry,
        BilibiliParseProvider bilibiliProvider)
    {
        _context = context;
        _config = config;
        _providerRegistry = providerRegistry;
        _bilibiliProvider = bilibiliProvider;
    }

    public async Task ParseAndReplyAsync(IncomingMessage message, string text)
    {
        try
        {
            await TryReactToSourceMessageAsync(message, "351");
            var media = await _providerRegistry.ParseAsync(text);
            if (media.ProviderPayload is BilibiliArticleParseResult article)
            {
                await SendArticleForwardAsync(message, article);
                await SendArticleDocumentCardAsync(message, article);
                await TryReactToSourceMessageAsync(message, "426");
                return;
            }

            if (media.ProviderPayload is BilibiliLiveParseResult live)
            {
                await SendLiveForwardAsync(message, live);
                await TrySendLiveReplayClipAsync(message, live);
                await TryReactToSourceMessageAsync(message, "426");
                return;
            }

            BilibiliParseResult? episodeVideo = null;
            if (media.ProviderPayload is BilibiliBangumiEpisodeVideoParseResult bangumiEpisode)
            {
                await SendBangumiForwardAsync(message, bangumiEpisode.Bangumi, sendEpHint: false);
                episodeVideo = bangumiEpisode.Video;
            }
            else if (media.ProviderPayload is BilibiliBangumiParseResult bangumi)
            {
                await SendBangumiForwardAsync(message, bangumi);
                await TryReactToSourceMessageAsync(message, "426");
                return;
            }

            if (media.ProviderPayload is BilibiliMultiPageParseResult multiPage)
            {
                await SendMultiPageForwardAsync(message, multiPage);
                await TryReactToSourceMessageAsync(message, "426");
                return;
            }

            var result = episodeVideo ?? media.ProviderPayload as BilibiliParseResult;
            if (result is null)
            {
                return;
            }

            LogBilibiliQualityInfo(result);
            if (!_config.SendVideoAsFile || !result.IsVideo)
            {
                await ReplyAsync(message, FormatBilibiliResult(result, videoDownloadAttempted: false));
                await TryReactToSourceMessageAsync(message, "426");
                return;
            }

            var videoSent = false;
            var fileUploaded = false;
            string? videoSendError = null;
            string? fileUploadInfo = null;

            try
            {
                _ = StartSendCoverMessageAsync(message, result);
                var videoSegment = await BuildVideoSegmentAsync(result);
                await SendVideoMessageAsync(message, result, videoSegment);
                videoSent = true;

                if (_config.UploadVideoAsFile && !_config.UploadVideoAsFileOnlyOnVideoSendFailure && !string.IsNullOrWhiteSpace(result.LocalVideoPath))
                {
                    fileUploadInfo = await UploadVideoFileAsync(message, result);
                    fileUploaded = true;
                    BotLog.Info($"MyParser Bilibili 文件上传完成: bvid={result.Bvid}, {fileUploadInfo}");
                }

                CleanupLocalVideoAfterSend(result);
                await TryReactToSourceMessageAsync(message, "426");
                return;
            }
            catch (Exception ex)
            {
                videoSendError = ex.Message;
                BotLog.Warning($"MyParser Bilibili VideoSegment 发送未确认: bvid={result.Bvid}, detail={ex.Message}");
                if (_config.UploadVideoAsFile && !string.IsNullOrWhiteSpace(result.LocalVideoPath))
                {
                    try
                    {
                        fileUploadInfo = await UploadVideoFileAsync(message, result);
                        fileUploaded = true;
                        BotLog.Info($"MyParser Bilibili VideoSegment 未确认后文件上传完成: bvid={result.Bvid}, {fileUploadInfo}");
                        CleanupLocalVideoAfterSend(result);
                        await TryReactToSourceMessageAsync(message, "426");
                        return;
                    }
                    catch (Exception uploadEx)
                    {
                        fileUploadInfo = uploadEx.Message;
                        BotLog.Warning($"MyParser Bilibili VideoSegment 未确认，文件上传也未完成: bvid={result.Bvid}, detail={uploadEx.Message}");
                    }
                }
            }

            await ReplyAsync(message, FormatBilibiliResult(result, true, videoSent, videoSendError, fileUploaded, fileUploadInfo));
            await TryReactToSourceMessageAsync(message, "379");
        }
        catch (BilibiliLoginRequiredException ex)
        {
            await TryReactToSourceMessageAsync(message, "379");
            await ReplyAsync(message, "Bilibili 解析需要登录：" + ex.Message);
        }
        catch (BilibiliParseException ex)
        {
            await TryReactToSourceMessageAsync(message, "379");
            await ReplyAsync(message, "Bilibili 解析未完成：" + ex.Message);
        }
        catch (TaskCanceledException)
        {
            await TryReactToSourceMessageAsync(message, "379");
            await ReplyAsync(message, "Bilibili 解析超时，请稍后再试。若经常超时，请检查 BilibiliCookie/网络/ffmpeg。");
        }
        catch (Exception ex)
        {
            await TryReactToSourceMessageAsync(message, "379");
            BotLog.Error($"MyParser Bilibili 解析异常：{ex}");
            await ReplyAsync(message, "Bilibili 解析异常：" + ex.Message);
        }
    }

    private Task TryReactToSourceMessageAsync(IncomingMessage message, string faceId)
    {
        return MessageHandlerCommon.ReactAsync(_context, message, faceId, "Bilibili");
    }

    public async Task HandleLoginAsync(IncomingMessage message)
    {
        try
        {
            var session = await _bilibiliProvider.Parser.GenerateQrLoginSessionAsync();
            await ReplyAsync(message,
                "Bilibili 扫码登录\n"
                + "请用哔哩哔哩 App 扫描下面二维码，并在 3 分钟内确认登录。\n"
                + $"如果二维码图片无法显示，请打开：{session.Url}");
            await SendQrImageAsync(message, session.Url, $"bilibili_qr_{session.QrcodeKey}");

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
                var poll = await _bilibiliProvider.Parser.PollQrLoginAsync(session.QrcodeKey, cts.Token);
                switch (poll.Code)
                {
                    case 0 when poll.IsLogin:
                        SaveBilibiliCookieToPluginDirectory();
                        await ReplyAsync(message, $"Bilibili 登录成功，Cookie 已保存到插件 cookie 目录。");
                        return;
                    case 86038:
                        await ReplyAsync(message, "Bilibili 登录二维码已过期，请重新发送登录命令。");
                        return;
                    case 86090:
                        BotLog.Info("MyParser Bilibili 二维码已扫码，等待确认。");
                        break;
                    case 86101:
                        break;
                    default:
                        BotLog.Info($"MyParser Bilibili 二维码轮询: code={poll.Code}, message={poll.Message}");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            await ReplyAsync(message, "Bilibili 登录二维码已超时，请重新发送登录命令。");
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser Bilibili 扫码登录失败：{ex}");
            await ReplyAsync(message, "Bilibili 扫码登录失败：" + ex.Message);
        }
    }

    private async Task<VideoOutgoingSegment> BuildVideoSegmentAsync(BilibiliParseResult result)
    {
        var (fileUri, localPath) = await _bilibiliProvider.Parser.DownloadVideoAsync(result);
        result.LocalVideoFileUri = fileUri;
        result.LocalVideoPath = localPath;
        LogFinalVideoFileInfo(result);

        var fileSize = new FileInfo(localPath).Length;
        var base64LimitBytes = Math.Max(0, _config.VideoSegmentBase64MaxMegabytes) * 1024L * 1024L;
        var useBase64 = _config.SendVideoSegmentAsBase64
                        && MemorySafetyUtilities.CanUseBase64ForFile(fileSize, _config.VideoSegmentBase64MaxMegabytes);
        string videoUri;
        string uriMode;
        if (useBase64)
        {
            videoUri = "base64://" + Convert.ToBase64String(await File.ReadAllBytesAsync(localPath));
            uriMode = "base64";
        }
        else if (_config.UseLocalHttpServerForLargeVideoSegment)
        {
            videoUri = GetLocalVideoHttpServer().RegisterFile(localPath);
            result.LocalVideoRegisteredToHttpServer = true;
            uriMode = "http";
        }
        else
        {
            videoUri = fileUri;
            uriMode = "file";
        }

        BotLog.Info($"MyParser Bilibili VideoSegment URI 模式：{uriMode}, file_mb={fileSize / 1024d / 1024d:F2}, base64_limit_mb={_config.VideoSegmentBase64MaxMegabytes}, uri_preview={MediaUriUtilities.PreviewUri(videoUri)}");
        var thumbUri = _config.IncludeVideoThumbUri && !string.IsNullOrWhiteSpace(result.CoverUrl) ? result.CoverUrl : null;
        return new VideoOutgoingSegment(videoUri, thumbUri);
    }

    private Task StartSendCoverMessageAsync(IncomingMessage message, BilibiliParseResult result)
    {
        if (!_config.SendCoverWithVideoSegment || string.IsNullOrWhiteSpace(result.CoverUrl))
        {
            return Task.CompletedTask;
        }

        return MessageHandlerCommon.RunLoggedBackgroundAsync($"Bilibili 封面卡片异步发送: bvid={result.Bvid}", () => SendCoverMessageAsync(message, result));
    }

    private async Task SendVideoMessageAsync(IncomingMessage message, BilibiliParseResult result, VideoOutgoingSegment videoSegment)
    {
        var segments = new OutgoingSegment[] { videoSegment };
        var stopwatch = Stopwatch.StartNew();
        BotLog.Info($"MyParser Bilibili VideoSegment 发送开始: bvid={result.Bvid}, scene={GetMessageScene(message)}, uri_mode={MediaUriUtilities.GetUriMode(videoSegment.Uri)}, uri_preview={MediaUriUtilities.PreviewUri(videoSegment.Uri)}");
        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await _context.Message.SendGroupMessageAsync(group.Group.GroupId, segments);
                BotLog.Info($"MyParser Bilibili VideoSegment 发送接口完成: bvid={result.Bvid}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureVideoSendAccepted(response.MessageSeq, "group");
                break;
            }
            case FriendIncomingMessage friend:
            {
                var response = await _context.Message.SendPrivateMessageAsync(friend.SenderId, segments);
                BotLog.Info($"MyParser Bilibili VideoSegment 发送接口完成: bvid={result.Bvid}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureVideoSendAccepted(response.MessageSeq, "friend");
                break;
            }
            default:
            {
                await _context.Message.ReplyAsync(message, segments);
                break;
            }
        }
    }

    private void EnsureVideoSendAccepted(long messageSeq, string scene)
    {
        if (_config.TreatZeroMessageSeqAsVideoSendFailure && messageSeq <= 0)
        {
            throw new InvalidOperationException($"VideoSegment 发送返回 message_seq={messageSeq}，未取得有效消息序号，改为文件上传。scene={scene}");
        }
    }

    private void CleanupLocalVideoAfterSend(BilibiliParseResult result)
    {
        if (result.LocalVideoRegisteredToHttpServer)
        {
            _localVideoHttpServer?.UnregisterFile(result.LocalVideoPath);
            result.LocalVideoRegisteredToHttpServer = false;
        }

        LocalMediaCleanup.DeleteLocalVideoIfConfigured(_config, result.LocalVideoPath, "bilibili");
    }

    private async Task SendQrImageAsync(IncomingMessage message, string text, string fileName)
    {
        var qrFile = await BuildQrImageAsync(text, fileName);
        var segment = new ImageOutgoingSegment(qrFile.Uri);
        switch (message)
        {
            case GroupIncomingMessage group:
                await _context.Message.SendGroupMessageAsync(group.Group.GroupId, segment);
                break;
            case FriendIncomingMessage friend:
                await _context.Message.SendPrivateMessageAsync(friend.SenderId, segment);
                break;
            default:
                await _context.Message.ReplyAsync(message, segment);
                break;
        }
    }

    private static async Task<(string Uri, string Path)> BuildQrImageAsync(string text, string fileName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser", "bilibili", "qr");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName + ".png");
        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        var png = qr.ToPngBitmap(border: 4, scale: 8);
        await File.WriteAllBytesAsync(path, png);
        return ("base64://" + Convert.ToBase64String(png), path);
    }

    private Task<string> UploadVideoFileAsync(IncomingMessage message, BilibiliParseResult result)
    {
        return MessageHandlerCommon.UploadLocalVideoFileAsync(_context, _config, message, result.LocalVideoPath, "Bilibili", result.Bvid);
    }

    private void SaveBilibiliCookieToPluginDirectory()
    {
        var path = ResolveCookiePath(_config.BilibiliCookieFileName, "bilibili_cookie.txt");
        File.WriteAllText(path, _config.BilibiliCookie ?? string.Empty, Encoding.UTF8);
    }

    private string ResolveCookiePath(string? configuredFileName, string defaultFileName)
    {
        return MessageHandlerCommon.ResolveCookiePath(_context, _config, configuredFileName, defaultFileName);
    }

    private Task ReplyAsync(IncomingMessage message, string text)
    {
        return SendReplyAsync(message, text);
    }

    private Task<SendMessageResult> SendReplyAsync(IncomingMessage message, string text)
    {
        return MessageHandlerCommon.ReplyTextAsync(_context, _config, message, text);
    }

    private static string NormalizePageReplyText(string text)
    {
        return text.Trim().Trim('"', '\'', '“', '”', '‘', '’', '「', '」', '『', '』').Trim();
    }

    private void SubscribeBilibiliPageReply(BilibiliMultiPageParseResult result, long promptMessageSeq)
    {
        IReplySubscription? subscription = null;
        subscription = _context.Message.SubscribeReply(promptMessageSeq, TimeSpan.FromMinutes(10), async reply =>
        {
            var text = reply switch
            {
                GroupIncomingMessage group => group.GetPlainText(),
                FriendIncomingMessage friend => friend.GetPlainText(),
                TempIncomingMessage temp => string.Concat(temp.Segments.OfType<TextIncomingSegment>().Select(i => i.Text)),
                _ => string.Empty,
            };

            if (!int.TryParse(NormalizePageReplyText(text), out var page) || page <= 0)
            {
                await ReplyAsync(reply, $"请回复 1 到 {result.PageCount} 之间的数字来解析指定分P。");
                return;
            }

            if (page > result.PageCount)
            {
                await ReplyAsync(reply, $"该视频只有 {result.PageCount} 个分P，请回复 1 到 {result.PageCount} 之间的数字。");
                return;
            }

            subscription?.Dispose();
            await ParseAndReplyAsync(reply, $"https://www.bilibili.com/video/{result.Bvid}/?p={page}");
        }, disposeOnReply: false);
    }

    private LocalVideoHttpServer GetLocalVideoHttpServer()
    {
        return _localVideoHttpServer ??= new LocalVideoHttpServer(_config.LocalVideoHttpHost, _config.LocalVideoHttpPort, _config.LocalVideoHttpPublicBaseUrl, _config.AllowLanAccessToLocalVideoHttpServer);
    }

    private void LogFinalVideoFileInfo(BilibiliParseResult result)
    {
        if (!_config.LogSelectedQualityInfo)
        {
            return;
        }

        var selected = result.SelectedVideo;
        var fileSize = !string.IsNullOrWhiteSpace(result.LocalVideoPath) && File.Exists(result.LocalVideoPath) ? new FileInfo(result.LocalVideoPath).Length : 0;
        BotLog.Info(
            "MyParser Bilibili 最终发送视频信息: "
            + $"bvid={result.Bvid}, "
            + $"quality={(selected?.QualityName ?? "unknown")}, "
            + $"fps={(selected?.Fps ?? 0)}, "
            + $"bitrate_kbps={(selected is { Bandwidth: > 0 } ? selected.Bandwidth / 1000 : 0)}, "
            + $"size={(selected is null ? "0x0" : $"{selected.Width}x{selected.Height}")}, "
            + $"codec={(selected?.CodecName ?? "unknown")}, "
            + $"file_mb={(fileSize > 0 ? fileSize / 1024d / 1024d : 0):F2}, "
            + $"file={result.LocalVideoPath}");
    }

    private static void LogBilibiliQualityInfo(BilibiliParseResult result)
    {
        var selected = result.SelectedVideo;
        BotLog.Info($"MyParser Bilibili 选中画质: bvid={result.Bvid}, quality={selected?.QualityName}, fps={selected?.Fps}, size={selected?.Width}x{selected?.Height}, codec={selected?.CodecName}, total_options={result.VideoStreams.Count}");
        foreach (var (stream, index) in result.VideoStreams.Take(12).Select((stream, index) => (stream, index + 1)))
        {
            BotLog.Info($"MyParser Bilibili 可用画质: #{index}, quality={stream.QualityName}, fps={stream.Fps}, bitrate_kbps={stream.Bandwidth / 1000}, size={stream.Width}x{stream.Height}, codec={stream.CodecName}");
        }
    }

    private static IEnumerable<string> SplitText(string text, int chunkSize)
    {
        text = text.Trim();
        for (var i = 0; i < text.Length; i += chunkSize)
        {
            yield return text.Substring(i, Math.Min(chunkSize, text.Length - i));
        }
    }

    private static long GetBotOrSenderId(IncomingMessage message) => message switch
    {
        GroupIncomingMessage group => group.SenderId,
        FriendIncomingMessage friend => friend.SenderId,
        TempIncomingMessage temp => temp.SenderId,
        _ => 0,
    };

    private string FormatBilibiliLiveResult(BilibiliLiveParseResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bilibili 直播解析成功");
        sb.AppendLine($"房间：{result.RealRoomId}");
        sb.AppendLine($"状态：{FormatLiveStatus(result.LiveStatus)}");
        if (!string.IsNullOrWhiteSpace(result.Title)) sb.AppendLine($"标题：{TrimLine(result.Title, 120)}");
        if (!string.IsNullOrWhiteSpace(result.AnchorName)) sb.AppendLine($"主播：{result.AnchorName}");
        if (!string.IsNullOrWhiteSpace(result.SourceUrl)) sb.AppendLine($"链接：{result.SourceUrl}");

        if (result.Streams.Count > 0)
        {
            var recommended = result.Streams.First();
            sb.AppendLine($"推荐流：{recommended.Protocol}/{recommended.Format}/{recommended.Codec} qn={recommended.CurrentQn} CDN#{recommended.CdnIndex}");
            sb.AppendLine(recommended.Url);
            var summary = result.Streams
                .GroupBy(i => $"{i.Protocol}/{i.Format}/{i.Codec}/qn={i.CurrentQn}")
                .Take(8)
                .Select(i => $"{i.Key}×{i.Count()}");
            sb.AppendLine("可用流：" + string.Join("；", summary));
        }
        else
        {
            sb.AppendLine("播放流：当前未返回可用直播流。");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatLiveStatus(int status) => status switch
    {
        0 => "未开播",
        1 => "直播中",
        2 => "轮播",
        _ => status.ToString(),
    };

    private static string BuildAuthorMeta(BilibiliParseResult result)
    {
        var parts = new List<string>();
        if (result.ViewCount > 0)
        {
            parts.Add($"{FormatCount(result.ViewCount)}播放");
        }

        if (result.ReplyCount > 0)
        {
            parts.Add($"{FormatCount(result.ReplyCount)}评论");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "Bilibili UP主";
    }

    private static string BuildCardDescription(BilibiliParseResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Description))
        {
            return result.Description;
        }

        return string.IsNullOrWhiteSpace(result.PartTitle) ? string.Empty : $"P{result.Page} · {result.PartTitle}";
    }

    private static string FormatDurationText(long seconds)
    {
        if (seconds <= 0)
        {
            return "--:--";
        }

        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
    }

    private static string FormatCount(long value)
    {
        if (value <= 0)
        {
            return "--";
        }

        if (value >= 100_000_000)
        {
            return $"{value / 100_000_000d:F1}亿";
        }

        if (value >= 10_000)
        {
            return $"{value / 10_000d:F1}万";
        }

        return value.ToString();
    }

    private string FormatBilibiliResult(BilibiliParseResult result, bool videoDownloadAttempted = false, bool videoSent = false, string? videoSendError = null, bool fileUploaded = false, string? fileUploadInfo = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bilibili 视频解析成功");
        sb.AppendLine($"BV：{result.Bvid}");
        if (!string.IsNullOrWhiteSpace(result.Title)) sb.AppendLine($"标题：{TrimLine(result.Title, 140)}");
        if (!string.IsNullOrWhiteSpace(result.PartTitle)) sb.AppendLine($"分P：P{result.Page} {TrimLine(result.PartTitle, 80)}");
        if (!string.IsNullOrWhiteSpace(result.AuthorName)) sb.AppendLine($"UP主：{result.AuthorName}");
        if (_config.IncludeCoverUrl && !string.IsNullOrWhiteSpace(result.CoverUrl)) sb.AppendLine($"封面：{result.CoverUrl}");
        var selected = result.SelectedVideo;
        if (selected is not null) sb.AppendLine($"清晰度：{selected.QualityName} {selected.Width}x{selected.Height} {selected.Fps:0.###}fps {selected.CodecName}");

        var videoStatus = videoSent
            ? "视频：已下载音视频流、ffmpeg 合并，并已调用 VideoSegment 发送接口"
            : videoDownloadAttempted
                ? $"视频：下载/合并/发送未完成；原因：{TrimLine(videoSendError ?? "未知错误", 100)}"
                : "视频：已解析，未下载发送";
        sb.AppendLine(videoStatus);
        if (_config.UploadVideoAsFile)
        {
            sb.AppendLine(fileUploaded ? $"文件上传：已上传为{fileUploadInfo}" : $"文件上传：未执行或未完成；原因：{TrimLine(fileUploadInfo ?? "未知", 80)}");
        }

        if (_config.IncludeLocalFilePath && !string.IsNullOrWhiteSpace(result.LocalVideoPath))
        {
            sb.AppendLine($"本地文件：{result.LocalVideoPath}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string GetMessageScene(IncomingMessage message) => MessageHandlerCommon.GetMessageScene(message);

    private static string ResolveCoverDownloadDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "downloads", "MyParser", "bilibili");
    }

    private static string SanitizeLocalFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value;
    }

    private static string TrimLine(string value, int maxLength)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }

    public void Dispose()
    {
        _localVideoHttpServer?.Dispose();
        _localVideoHttpServer = null;
    }
}
