using System.Diagnostics;
using System.Net;
using System.Text;
using Net.Codecrete.QrCodeGenerator;
using ShiroBot.AvaloniaSdk;
using Shirobot.Plugin.MyParser.Parsing;
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

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Impl.MessageHandling;

internal sealed class BilibiliMessageHandler : IDisposable
{
    private static readonly HttpClient CoverHttp = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
    });

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

            if (media.ProviderPayload is BilibiliMultiPageParseResult multiPage)
            {
                await SendMultiPageForwardAsync(message, multiPage);
                await TryReactToSourceMessageAsync(message, "426");
                return;
            }

            if (media.ProviderPayload is not BilibiliParseResult result)
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

    private async Task TryReactToSourceMessageAsync(IncomingMessage message, string faceId)
    {
        if (message is not GroupIncomingMessage group)
        {
            return;
        }

        try
        {
            await _context.Group.SendGroupMessageReactionAsync(group.Group.GroupId, group.MessageSeq, faceId);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser Bilibili 消息贴表情失败: group_id={group.Group.GroupId}, message_seq={group.MessageSeq}, face={faceId}, error={ex.Message}");
        }
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
                        _context.Config.Save(_config);
                        await ReplyAsync(message, $"Bilibili 登录成功");
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

    private async Task SendVideoMessageAsync(IncomingMessage message, BilibiliParseResult result, VideoOutgoingSegment videoSegment)
    {
        if (_config.SendCoverWithVideoSegment && !string.IsNullOrWhiteSpace(result.CoverUrl))
        {
            await SendCoverMessageAsync(message, result);
        }

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

    private async Task SendCoverMessageAsync(IncomingMessage message, BilibiliParseResult result)
    {
        var coverUri = await BuildCoverCardUriAsync(result);
        var segment = new ImageOutgoingSegment(coverUri);
        var stopwatch = Stopwatch.StartNew();
        BotLog.Info($"MyParser Bilibili 封面卡片 ImageSegment 发送开始: bvid={result.Bvid}, scene={GetMessageScene(message)}, uri_preview={MediaUriUtilities.PreviewUri(coverUri)}");

        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await _context.Message.SendGroupMessageAsync(group.Group.GroupId, segment);
                BotLog.Info($"MyParser Bilibili 封面卡片 ImageSegment 发送接口完成: bvid={result.Bvid}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
            case FriendIncomingMessage friend:
            {
                var response = await _context.Message.SendPrivateMessageAsync(friend.SenderId, segment);
                BotLog.Info($"MyParser Bilibili 封面卡片 ImageSegment 发送接口完成: bvid={result.Bvid}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
            default:
            {
                await _context.Message.ReplyAsync(message, segment);
                break;
            }
        }
    }

    private async Task<string> BuildCoverCardUriAsync(BilibiliParseResult result)
    {
        var coverImage = await BuildRemoteImageAsync(result.CoverUrl, result.SourceUrl, $"bilibili_cover_{result.Bvid}");
        var coverUri = coverImage.Uri;
        if (_context.Render is null)
        {
            BotLog.Warning($"MyParser Bilibili Avalonia 渲染服务不可用，直接发送原始封面: bvid={result.Bvid}");
            return coverUri;
        }

        try
        {
            var coverBitmap = !string.IsNullOrWhiteSpace(coverImage.LocalPath)
                ? RenderBitmapUtilities.DecodeImageFileForRender(coverImage.LocalPath)
                : RenderBitmapUtilities.DecodeBase64ImageForRender(coverUri);
            var avatarImage = await BuildRemoteImageAsync(result.AuthorAvatarUrl, result.SourceUrl, $"bilibili_avatar_{result.Bvid}");
            var avatarBitmap = !string.IsNullOrWhiteSpace(avatarImage.LocalPath)
                ? RenderBitmapUtilities.DecodeImageFileForRender(avatarImage.LocalPath)
                : RenderBitmapUtilities.DecodeBase64ImageForRender(avatarImage.Uri);

            var selected = result.SelectedVideo;
            var vm = new BiliCardViewModel
            {
                Cover = coverBitmap,
                Avatar = avatarBitmap,
                Title = string.IsNullOrWhiteSpace(result.Title) ? "Bilibili 视频" : result.Title,
                Description = BuildCardDescription(result),
                AuthorName = string.IsNullOrWhiteSpace(result.AuthorName) ? "未知 UP" : result.AuthorName,
                AuthorMeta = BuildAuthorMeta(result),
                DurationText = FormatDurationText(result.DurationSeconds),
                TagsText = selected is null ? "# 视频" : $"# {selected.QualityName}",
                LikeCount = FormatCount(result.LikeCount),
                CoinCount = FormatCount(result.CoinCount),
                CollectCount = FormatCount(result.FavoriteCount),
                ShareCount = FormatCount(result.ShareCount),
            };
            var png = await _context.RenderControlPngAsync<BiliCard>(vm, new ControlRenderOptions(RenderTheme.Auto));
            var cardPath = await SaveRenderedCoverCardAsync(result, png);
            BotLog.Info($"MyParser Bilibili 封面卡片渲染完成: bvid={result.Bvid}, cover_url={result.CoverUrl}, png_kb={png.Length / 1024d:F1}, view={typeof(BiliCard).FullName}, rendered_path={cardPath}");
            return "base64://" + Convert.ToBase64String(png);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser Bilibili 封面卡片渲染失败，直接发送原始封面: bvid={result.Bvid}, cover_url={result.CoverUrl}, error={ex.Message}");
            return coverUri;
        }
    }

    private static async Task<string> SaveRenderedCoverCardAsync(BilibiliParseResult result, byte[] png)
    {
        var dir = Path.Combine(ResolveCoverDownloadDirectory(), "cards");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"bilibili_card_{SanitizeLocalFileName(result.Bvid)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png");
        await File.WriteAllBytesAsync(path, png);
        return path;
    }

    private async Task<(string Uri, string? LocalPath)> BuildRemoteImageAsync(string? imageUrl, string? referer, string filePrefix)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return (string.Empty, null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", BilibiliConstants.UserAgent);
            request.Headers.TryAddWithoutValidation("Referer", referer ?? BilibiliConstants.Origin + "/");
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            if (!string.IsNullOrWhiteSpace(_config.BilibiliCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", _config.BilibiliCookie);
            }

            using var response = await CoverHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            const long maxBytes = 10 * 1024L * 1024L;
            if (contentLength is > 0 && contentLength > maxBytes)
            {
                BotLog.Warning($"MyParser Bilibili 图片过大，回退原始 URL: url={imageUrl}, image_mb={contentLength.Value / 1024d / 1024d:F2}, limit_mb=10");
                return (imageUrl, null);
            }

            await using var input = await response.Content.ReadAsStreamAsync();
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBytes)
                {
                    BotLog.Warning($"MyParser Bilibili 图片下载超过限制，回退原始 URL: url={imageUrl}, limit_mb=10");
                    return (imageUrl, null);
                }

                output.Write(buffer, 0, read);
            }

            var bytes = output.ToArray();
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var extension = MediaUriUtilities.GuessImageExtension(contentType, imageUrl, bytes);
            var localDir = ResolveCoverDownloadDirectory();
            Directory.CreateDirectory(localDir);
            var localPath = Path.Combine(localDir, $"{SanitizeLocalFileName(filePrefix)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{extension}");
            await File.WriteAllBytesAsync(localPath, bytes);
            BotLog.Info($"MyParser Bilibili 图片下载完成: source_url={imageUrl}, content_type={contentType}, bytes={bytes.Length}, local_path={localPath}");
            return ("base64://" + Convert.ToBase64String(bytes), localPath);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser Bilibili 图片转 base64/本地文件失败，回退原始 URL: url={imageUrl}, error={ex.Message}");
            return (imageUrl, null);
        }
    }

    private async Task<string> UploadVideoFileAsync(IncomingMessage message, BilibiliParseResult result)
    {
        if (string.IsNullOrWhiteSpace(result.LocalVideoPath) || !File.Exists(result.LocalVideoPath))
        {
            throw new InvalidOperationException("本地视频文件不存在。");
        }

        var localPath = Path.GetFullPath(result.LocalVideoPath);
        var fileSize = new FileInfo(localPath).Length;
        var base64LimitBytes = Math.Max(0, _config.UploadVideoBase64MaxMegabytes) * 1024L * 1024L;
        var useBase64 = _config.UploadVideoAsBase64 && MemorySafetyUtilities.CanUseBase64ForFile(fileSize, _config.UploadVideoBase64MaxMegabytes);
        var fileUri = useBase64 ? "base64://" + Convert.ToBase64String(await File.ReadAllBytesAsync(localPath)) : new Uri(localPath).AbsoluteUri;
        var fileName = Path.GetFileName(localPath);
        var uploadMode = useBase64 ? "base64" : "file";
        var stopwatch = Stopwatch.StartNew();

        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await _context.File.UploadGroupFileAsync(group.Group.GroupId, fileUri, fileName);
                return $"群文件 FileId={response.FileId} Mode={uploadMode} elapsed={stopwatch.Elapsed:mm\\:ss}";
            }
            case FriendIncomingMessage friend:
            {
                var response = await _context.File.UploadPrivateFileAsync(friend.SenderId, fileUri, fileName);
                return $"私聊文件 FileId={response.FileId} Mode={uploadMode} elapsed={stopwatch.Elapsed:mm\\:ss}";
            }
            default:
                throw new NotSupportedException("当前消息类型不支持文件上传。");
        }
    }

    private void SaveBilibiliCookieToPluginDirectory()
    {
        var pluginDir = Path.GetDirectoryName(_context.Config.ConfigPath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(pluginDir);
        var fileName = string.IsNullOrWhiteSpace(_config.BilibiliCookieFileName) ? "bilibili_cookie.txt" : _config.BilibiliCookieFileName.Trim();
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(pluginDir, fileName);
        File.WriteAllText(path, _config.BilibiliCookie ?? string.Empty, Encoding.UTF8);
    }

    private Task ReplyAsync(IncomingMessage message, string text)
    {
        return SendReplyAsync(message, text);
    }

    private Task<SendMessageResult> SendReplyAsync(IncomingMessage message, string text)
    {
        return _config.QuoteReply ? _context.Message.QuoteReplyAsync(message, text) : _context.Message.ReplyAsync(message, text);
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

    private async Task SendMultiPageForwardAsync(IncomingMessage message, BilibiliMultiPageParseResult result)
    {
        var senderId = GetBotOrSenderId(message);
        var senderName = string.IsNullOrWhiteSpace(result.AuthorName) ? "Bilibili 分P视频" : result.AuthorName!;
        var forwarded = new List<OutgoingForwardedMessage>();
        var headerSegments = new List<OutgoingSegment>();

        if (!string.IsNullOrWhiteSpace(result.CoverUrl))
        {
            var cover = await BuildRemoteImageAsync(result.CoverUrl, result.SourceUrl, $"bilibili_multipage_cover_{result.Bvid}");
            if (!string.IsNullOrWhiteSpace(cover.Uri))
            {
                headerSegments.Add(new ImageOutgoingSegment(cover.Uri));
            }
        }

        headerSegments.Add(new TextOutgoingSegment(BuildMultiPageHeaderText(result)));
        forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, headerSegments));

        var coverImageLimit = Math.Max(0, _config.BilibiliMultiPageCoverImageLimit);
        foreach (var page in result.Pages)
        {
            var segments = new List<OutgoingSegment>();
            if (page.Page <= coverImageLimit && !string.IsNullOrWhiteSpace(page.CoverUrl))
            {
                var pageCover = await BuildRemoteImageAsync(page.CoverUrl, page.SourceUrl, $"bilibili_multipage_{result.Bvid}_p{page.Page:D3}");
                if (!string.IsNullOrWhiteSpace(pageCover.Uri))
                {
                    segments.Add(new ImageOutgoingSegment(pageCover.Uri));
                }
            }

            segments.Add(new TextOutgoingSegment(BuildMultiPagePageText(page)));
            forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, segments));
        }

        var title = string.IsNullOrWhiteSpace(result.Title) ? $"Bilibili 分P视频 {result.Bvid}" : TrimLine(result.Title!, 48);
        var preview = new[]
        {
            $"{result.PageCount} 个分P",
            string.IsNullOrWhiteSpace(result.AuthorName) ? result.Bvid : result.AuthorName!,
            "仅展示分P信息，不下载视频",
        };
        var summary = $"分P视频 · {result.PageCount}P";
        var forward = new ForwardOutgoingSegment(forwarded, title, preview, summary, "Bilibili 分P");

        switch (message)
        {
            case GroupIncomingMessage group:
                await _context.Message.SendGroupMessageAsync(group.Group.GroupId, forward);
                break;
            case FriendIncomingMessage friend:
                await _context.Message.SendPrivateMessageAsync(friend.SenderId, forward);
                break;
            default:
                await _context.Message.ReplyAsync(message, forward);
                break;
        }

        var prompt = await SendReplyAsync(message, $"如需解析指定分P，请用数字回复此消息。例：回复 \"4\" 即解析 P4。也可以直接发送：https://www.bilibili.com/video/{result.Bvid}/?p=4");
        SubscribeBilibiliPageReply(result, prompt.MessageSeq);
    }

    private static string BuildMultiPageHeaderText(BilibiliMultiPageParseResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bilibili 分P视频解析成功");
        sb.AppendLine($"BV：{result.Bvid}");
        sb.AppendLine($"分P数量：{result.PageCount}");
        if (!string.IsNullOrWhiteSpace(result.Title)) sb.AppendLine($"标题：{TrimLine(result.Title, 120)}");
        if (!string.IsNullOrWhiteSpace(result.AuthorName)) sb.AppendLine($"UP：{result.AuthorName}");
        if (result.RequestedPage > 1) sb.AppendLine($"当前链接指定：P{result.RequestedPage}");
        if (!string.IsNullOrWhiteSpace(result.CoverUrl)) sb.AppendLine($"主封面：{result.CoverUrl}");
        if (!string.IsNullOrWhiteSpace(result.SourceUrl)) sb.AppendLine($"链接：{result.SourceUrl}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildMultiPagePageText(BilibiliVideoPageInfo page)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"P{page.Page}: {TrimLine(string.IsNullOrWhiteSpace(page.PartTitle) ? "未命名" : page.PartTitle!, 80)}");
        if (page.DurationSeconds > 0) sb.AppendLine($"时长：{FormatDurationText(page.DurationSeconds)}");
        if (!string.IsNullOrWhiteSpace(page.CoverUrl)) sb.AppendLine($"封面：{page.CoverUrl}");
        sb.AppendLine($"链接：{page.SourceUrl}");
        return sb.ToString().TrimEnd();
    }

    private async Task TrySendLiveReplayClipAsync(IncomingMessage message, BilibiliLiveParseResult result)
    {
        if (!_config.SendBilibiliLiveReplayClip)
        {
            return;
        }

        try
        {
            var clipSeconds = Math.Clamp(_config.BilibiliLiveReplayClipSeconds, 1, 30);
            await ReplyAsync(message, $"正在从当前直播流可回溯分片中截取最近约 {clipSeconds} 秒，请稍候…");
            var downloader = new BilibiliLiveClipDownloader(_config);
            var clip = await downloader.DownloadRecentClipAsync(result, progress => ReplyAsync(message,
                $"直播回溯分片已冻结：当前 m3u8 提供 {progress.SelectedSegments}/{progress.TotalSegments} 段，约 {progress.ActualSeconds:F0} 秒；正在用 ffmpeg 封装 MP4…"));
            await ReplyAsync(message, "直播回看片段已封装完成，正在发送到 QQ…");
            result.LocalClipPath = clip.LocalPath;
            result.LocalClipFileUri = clip.FileUri;
            var videoUri = BuildLocalVideoSegmentUri(clip.LocalPath, result);
            var segment = new VideoOutgoingSegment(videoUri, string.IsNullOrWhiteSpace(result.CoverUrl) ? null : result.CoverUrl);
            await SendLiveClipVideoMessageAsync(message, result, segment, clip.Stream);
            CleanupLocalLiveClipAfterSend(result);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser Bilibili 直播片段发送未完成: room_id={result.RealRoomId}, detail={ex.Message}");
            await ReplyAsync(message, "直播回看片段截取/发送未完成：" + ex.Message);
        }
    }

    private string BuildLocalVideoSegmentUri(string localPath, BilibiliLiveParseResult result)
    {
        var fileSize = new FileInfo(localPath).Length;
        var useBase64 = _config.SendVideoSegmentAsBase64
                        && MemorySafetyUtilities.CanUseBase64ForFile(fileSize, _config.VideoSegmentBase64MaxMegabytes);
        string videoUri;
        string uriMode;
        if (useBase64)
        {
            videoUri = "base64://" + Convert.ToBase64String(File.ReadAllBytes(localPath));
            uriMode = "base64";
        }
        else if (_config.UseLocalHttpServerForLargeVideoSegment)
        {
            videoUri = GetLocalVideoHttpServer().RegisterFile(localPath);
            result.LocalClipRegisteredToHttpServer = true;
            uriMode = "http";
        }
        else
        {
            videoUri = new Uri(localPath).AbsoluteUri;
            uriMode = "file";
        }

        BotLog.Info($"MyParser Bilibili 直播片段 VideoSegment URI 模式：{uriMode}, room_id={result.RealRoomId}, file_mb={fileSize / 1024d / 1024d:F2}, uri_preview={MediaUriUtilities.PreviewUri(videoUri)}");
        return videoUri;
    }

    private async Task SendLiveClipVideoMessageAsync(IncomingMessage message, BilibiliLiveParseResult result, VideoOutgoingSegment videoSegment, BilibiliLiveStream stream)
    {
        var stopwatch = Stopwatch.StartNew();
        BotLog.Info($"MyParser Bilibili 直播片段 VideoSegment 发送开始: room_id={result.RealRoomId}, scene={GetMessageScene(message)}, stream={stream.Protocol}/{stream.Format}/{stream.Codec}, qn={stream.CurrentQn}, uri_mode={MediaUriUtilities.GetUriMode(videoSegment.Uri)}, uri_preview={MediaUriUtilities.PreviewUri(videoSegment.Uri)}");
        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await _context.Message.SendGroupMessageAsync(group.Group.GroupId, videoSegment);
                BotLog.Info($"MyParser Bilibili 直播片段 VideoSegment 发送接口完成: room_id={result.RealRoomId}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureVideoSendAccepted(response.MessageSeq, "group");
                break;
            }
            case FriendIncomingMessage friend:
            {
                var response = await _context.Message.SendPrivateMessageAsync(friend.SenderId, videoSegment);
                BotLog.Info($"MyParser Bilibili 直播片段 VideoSegment 发送接口完成: room_id={result.RealRoomId}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureVideoSendAccepted(response.MessageSeq, "friend");
                break;
            }
            default:
            {
                await _context.Message.ReplyAsync(message, videoSegment);
                break;
            }
        }
    }

    private void CleanupLocalLiveClipAfterSend(BilibiliLiveParseResult result)
    {
        if (result.LocalClipRegisteredToHttpServer)
        {
            _localVideoHttpServer?.UnregisterFile(result.LocalClipPath);
            result.LocalClipRegisteredToHttpServer = false;
        }

        LocalMediaCleanup.DeleteLocalVideoIfConfigured(_config, result.LocalClipPath, "bilibili");
    }

    private async Task SendLiveForwardAsync(IncomingMessage message, BilibiliLiveParseResult result)
    {
        var senderId = GetBotOrSenderId(message);
        var senderName = string.IsNullOrWhiteSpace(result.AnchorName) ? "Bilibili 直播" : result.AnchorName!;
        var forwarded = new List<OutgoingForwardedMessage>();
        var headerSegments = new List<OutgoingSegment>();

        if (!string.IsNullOrWhiteSpace(result.CoverUrl))
        {
            var cover = await BuildRemoteImageAsync(result.CoverUrl, result.SourceUrl, $"bilibili_live_cover_{result.RealRoomId}");
            if (!string.IsNullOrWhiteSpace(cover.Uri))
            {
                headerSegments.Add(new ImageOutgoingSegment(cover.Uri));
            }
        }

        headerSegments.Add(new TextOutgoingSegment(BuildLiveHeaderText(result)));
        forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, headerSegments));

        if (result.Streams.Count > 0)
        {
            foreach (var (stream, index) in result.Streams.Select((stream, index) => (stream, index + 1)))
            {
                forwarded.Add(new OutgoingForwardedMessage(senderId, senderName,
                [
                    new TextOutgoingSegment(BuildLiveStreamText(stream, index))
                ]));
            }
        }
        else
        {
            forwarded.Add(new OutgoingForwardedMessage(senderId, senderName,
            [
                new TextOutgoingSegment("当前未返回可用直播流。")
            ]));
        }

        var title = string.IsNullOrWhiteSpace(result.Title) ? $"Bilibili 直播 {result.RealRoomId}" : TrimLine(result.Title!, 48);
        var preview = new[]
        {
            $"房间 {result.RealRoomId}",
            FormatLiveStatus(result.LiveStatus),
            result.Streams.Count > 0 ? $"播放流 {result.Streams.Count} 条" : "无播放流",
        };
        var summary = $"{FormatLiveStatus(result.LiveStatus)} · {result.Streams.Count} 条播放流";
        var forward = new ForwardOutgoingSegment(forwarded, title, preview, summary, "Bilibili 直播");

        switch (message)
        {
            case GroupIncomingMessage group:
                await _context.Message.SendGroupMessageAsync(group.Group.GroupId, forward);
                break;
            case FriendIncomingMessage friend:
                await _context.Message.SendPrivateMessageAsync(friend.SenderId, forward);
                break;
            default:
                await _context.Message.ReplyAsync(message, forward);
                break;
        }
    }

    private static string BuildLiveHeaderText(BilibiliLiveParseResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bilibili 直播解析成功");
        sb.AppendLine($"房间：{result.RealRoomId}");
        sb.AppendLine($"状态：{FormatLiveStatus(result.LiveStatus)}");
        if (!string.IsNullOrWhiteSpace(result.Title)) sb.AppendLine($"标题：{TrimLine(result.Title, 120)}");
        if (!string.IsNullOrWhiteSpace(result.AnchorName)) sb.AppendLine($"主播：{result.AnchorName}");
        if (!string.IsNullOrWhiteSpace(result.RoomAudienceText)) sb.AppendLine($"房间观众：{result.RoomAudienceText}");
        else if (result.RoomAudienceCount > 0) sb.AppendLine($"房间观众：{FormatCount(result.RoomAudienceCount)}");
        if (!string.IsNullOrWhiteSpace(result.WatchedText)) sb.AppendLine($"人气/看过：{result.WatchedText}");
        else if (result.WatchedCount > 0) sb.AppendLine($"人气/看过：{FormatCount(result.WatchedCount)}");
        else if (result.OnlineCount > 0) sb.AppendLine($"人气值：{FormatCount(result.OnlineCount)}");
        if (result.LiveStartTime is not null) sb.AppendLine($"开播时间：{result.LiveStartTime:yyyy-MM-dd HH:mm:ss}");
        if (result.LiveDuration is not null) sb.AppendLine($"直播时长：{FormatDuration(result.LiveDuration.Value)}");
        if (!string.IsNullOrWhiteSpace(result.CoverUrl)) sb.AppendLine($"封面：{result.CoverUrl}");
        if (!string.IsNullOrWhiteSpace(result.SourceUrl)) sb.AppendLine($"链接：{result.SourceUrl}");
        sb.AppendLine($"播放流：{result.Streams.Count} 条，见后续转发节点。");
        AppendRecommendedLiveStreams(sb, result);
        return sb.ToString().TrimEnd();
    }

    private static void AppendRecommendedLiveStreams(StringBuilder sb, BilibiliLiveParseResult result)
    {
        if (result.Streams.Count == 0)
        {
            return;
        }

        var bestQuality = result.Streams
            .OrderByDescending(i => i.CurrentQn)
            .ThenByDescending(StreamCodecQualityRank)
            .ThenBy(StreamCompatibilityRank)
            .ThenBy(i => i.CdnIndex)
            .Take(2)
            .ToList();
        var bestCompatibility = result.Streams
            .OrderBy(StreamCompatibilityRank)
            .ThenByDescending(i => i.CurrentQn)
            .ThenBy(i => i.CdnIndex)
            .FirstOrDefault();

        sb.AppendLine("推荐播放流：");
        foreach (var stream in bestQuality)
        {
            sb.AppendLine($"- 画质优先：{FormatLiveStreamSummary(stream)}");
        }

        if (bestCompatibility is not null && !bestQuality.Contains(bestCompatibility))
        {
            sb.AppendLine($"- 兼容优先：{FormatLiveStreamSummary(bestCompatibility)}");
        }
        else if (bestCompatibility is not null)
        {
            sb.AppendLine($"- 兼容优先：同上 {FormatLiveStreamSummary(bestCompatibility)}");
        }
    }

    private static string FormatLiveStreamSummary(BilibiliLiveStream stream)
    {
        var accept = stream.AcceptQn.Count > 0 ? $", 可选 qn={string.Join('/', stream.AcceptQn)}" : string.Empty;
        return $"{stream.Protocol}/{stream.Format}/{stream.Codec} qn={stream.CurrentQn} CDN#{stream.CdnIndex}{accept}";
    }

    private static int StreamCompatibilityRank(BilibiliLiveStream stream)
    {
        return (stream.Protocol, stream.Format, stream.Codec) switch
        {
            ("http_hls", "ts", "avc") => 0,
            ("http_hls", "fmp4", "avc") => 1,
            ("http_stream", "flv", "avc") => 2,
            ("http_hls", "ts", "hevc") => 3,
            ("http_hls", "fmp4", "hevc") => 4,
            ("http_stream", "flv", "hevc") => 5,
            ("http_hls", "fmp4", "av1") => 6,
            _ => 99,
        };
    }

    private static int StreamCodecQualityRank(BilibiliLiveStream stream)
    {
        return stream.Codec.ToLowerInvariant() switch
        {
            "av1" => 3,
            "hevc" => 2,
            "avc" => 1,
            _ => 0,
        };
    }

    private static string BuildLiveStreamText(BilibiliLiveStream stream, int index)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#{index} {stream.Protocol}/{stream.Format}/{stream.Codec} qn={stream.CurrentQn} CDN#{stream.CdnIndex}");
        if (stream.AcceptQn.Count > 0)
        {
            sb.AppendLine("可选 qn：" + string.Join(", ", stream.AcceptQn));
        }

        sb.AppendLine(stream.Url);
        return sb.ToString().TrimEnd();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays}天 {duration:hh\\:mm\\:ss}"
            : duration.ToString(@"hh\:mm\:ss");
    }

    private async Task SendArticleForwardAsync(IncomingMessage message, BilibiliArticleParseResult result)
    {
        var senderId = GetBotOrSenderId(message);
        var senderName = string.IsNullOrWhiteSpace(result.AuthorName) ? GetArticleKindText(result) : result.AuthorName!;
        var forwarded = new List<OutgoingForwardedMessage>();

        forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, [new TextOutgoingSegment(BuildArticleHeaderText(result))]));

        var imageIndex = 0;
        foreach (var block in BuildForwardBlocks(result))
        {
            if (block.Type == BilibiliArticleBlockType.Image)
            {
                imageIndex++;
                var image = await BuildRemoteImageAsync(block.Url, result.SourceUrl, $"bilibili_article_{result.Cvid}_{imageIndex:D2}");
                if (!string.IsNullOrWhiteSpace(image.Uri))
                {
                    var segments = new List<OutgoingSegment> { new ImageOutgoingSegment(image.Uri) };
                    if (!string.IsNullOrWhiteSpace(block.Caption))
                    {
                        segments.Add(new TextOutgoingSegment(block.Caption));
                    }

                    forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, segments));
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(block.Text))
            {
                continue;
            }

            foreach (var chunk in SplitText(block.Text, 1200))
            {
                forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, [new TextOutgoingSegment(chunk)]));
            }
        }

        if (!string.IsNullOrWhiteSpace(result.SourceUrl))
        {
            forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, [new TextOutgoingSegment("原文：" + result.SourceUrl)]));
        }

        if (forwarded.Count == 0)
        {
            await ReplyAsync(message, FormatBilibiliArticleResult(result));
            return;
        }

        var title = string.IsNullOrWhiteSpace(result.Title) ? GetArticleKindText(result) : TrimLine(result.Title!, 48);
        var preview = new[]
        {
            GetArticleKindText(result),
            string.IsNullOrWhiteSpace(result.AuthorName) ? "Bilibili" : result.AuthorName!,
            result.ImageUrls.Count > 0 ? $"图片 {result.ImageUrls.Count} 张" : "正文摘要",
        };
        var summary = $"完整正文 + {result.ImageUrls.Count} 张图";
        var forward = new ForwardOutgoingSegment(forwarded, title, preview, summary, GetArticleKindText(result));

        switch (message)
        {
            case GroupIncomingMessage group:
                await _context.Message.SendGroupMessageAsync(group.Group.GroupId, forward);
                break;
            case FriendIncomingMessage friend:
                await _context.Message.SendPrivateMessageAsync(friend.SenderId, forward);
                break;
            default:
                await _context.Message.ReplyAsync(message, forward);
                break;
        }
    }

    private async Task SendArticleDocumentCardAsync(IncomingMessage message, BilibiliArticleParseResult result)
    {
        var cardUri = await BuildArticleDocumentCardUriAsync(result);
        if (string.IsNullOrWhiteSpace(cardUri))
        {
            return;
        }

        var segment = new ImageOutgoingSegment(cardUri);
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

    private async Task<string> BuildArticleDocumentCardUriAsync(BilibiliArticleParseResult result)
    {
        if (_context.Render is null)
        {
            return string.Empty;
        }

        try
        {
            var avatarImage = await BuildRemoteImageAsync(result.AuthorAvatarUrl, result.SourceUrl, $"bilibili_article_avatar_{result.Cvid}_{result.OpusId}");
            var blocks = new List<BiliArticleDocumentBlockViewModel>();
            var estimatedHeight = 230;
            foreach (var block in BuildDocumentBlocksForRender(result).Take(80))
            {
                if (block.Type == BilibiliArticleBlockType.Image)
                {
                    var image = await BuildRemoteImageAsync(block.Url, result.SourceUrl, $"bilibili_doc_img_{blocks.Count:D2}_{result.Cvid}_{result.OpusId}");
                    var height = 280;
                    blocks.Add(new BiliArticleDocumentBlockViewModel
                    {
                        IsImage = true,
                        Image = !string.IsNullOrWhiteSpace(image.LocalPath) ? RenderBitmapUtilities.DecodeImageFileForRender(image.LocalPath) : RenderBitmapUtilities.DecodeBase64ImageForRender(image.Uri),
                        Caption = string.IsNullOrWhiteSpace(block.Caption) ? $"图 {blocks.Count(i => i.IsImage) + 1}" : block.Caption,
                        Height = height,
                    });
                    estimatedHeight += height + 42;
                }
                else if (!string.IsNullOrWhiteSpace(block.Text))
                {
                    foreach (var chunk in SplitText(block.Text, 520))
                    {
                        var styled = CreateStyledTextBlockViewModel(block, chunk);
                        blocks.Add(styled);
                        estimatedHeight += EstimateTextBlockHeight(chunk, styled.FontSize, styled.LineHeight, Math.Max(300, 624 - styled.Indent)) + 18;
                    }
                }
            }

            var canvasHeight = Math.Clamp(estimatedHeight + 96, 900, 12000);
            var vm = new BiliArticleDocumentViewModel
            {
                CanvasHeight = canvasHeight,
                Avatar = !string.IsNullOrWhiteSpace(avatarImage.LocalPath) ? RenderBitmapUtilities.DecodeImageFileForRender(avatarImage.LocalPath) : RenderBitmapUtilities.DecodeBase64ImageForRender(avatarImage.Uri),
                KindText = GetArticleKindText(result),
                Title = string.IsNullOrWhiteSpace(result.Title) ? GetArticleKindText(result) : result.Title!,
                AuthorName = string.IsNullOrWhiteSpace(result.AuthorName) ? "未知作者" : result.AuthorName!,
                MetaText = BuildArticleAuthorMeta(result),
                StatsText = $"{FormatCount(result.ViewCount)}阅读 · {FormatCount(result.LikeCount)}赞 · {FormatCount(result.CoinCount)}投币 · {FormatCount(result.FavoriteCount)}收藏 · {FormatCount(result.ReplyCount)}评论 · {result.ImageUrls.Count}图",
                Blocks = blocks,
            };
            var png = await _context.RenderControlPngAsync<BiliArticleDocument>(vm, new ControlRenderOptions(RenderTheme.Dark));
            var dir = Path.Combine(ResolveCoverDownloadDirectory(), "article-documents");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"bilibili_article_document_{SanitizeLocalFileName(result.IsOpus ? result.OpusId ?? "opus" : "cv" + result.Cvid)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png");
            await File.WriteAllBytesAsync(path, png);
            BotLog.Info($"MyParser Bilibili 完整文档卡片渲染完成: id={(result.IsOpus ? result.OpusId : result.Cvid)}, blocks={blocks.Count}, height={canvasHeight}, png_kb={png.Length / 1024d:F1}, file={path}");
            return "base64://" + Convert.ToBase64String(png);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser Bilibili 完整文档卡片渲染失败: error={ex.Message}");
            return string.Empty;
        }
    }

private static string GetArticleKindText(BilibiliArticleParseResult result) => result.IsOpus ? "Bilibili 图文" : "Bilibili 专栏";

    private static BiliArticleDocumentBlockViewModel CreateStyledTextBlockViewModel(BilibiliArticleBlock block, string text)
    {
        var vm = new BiliArticleDocumentBlockViewModel
        {
            IsImage = false,
            Text = text,
            HeadingLevel = block.HeadingLevel,
            Indent = Math.Clamp(block.IndentLevel, 0, 6) * 24,
            Margin = new Avalonia.Thickness(Math.Clamp(block.IndentLevel, 0, 6) * 24, 0, 0, 0),
        };

        var foreground = NormalizeCardColor(block.TextColor) ?? "#F4EFF4";
        return block.TextStyle switch
        {
            BilibiliArticleTextStyle.Heading => CreateHeadingStyle(vm, block.HeadingLevel, foreground),
            BilibiliArticleTextStyle.Quote => WithStyle(vm, 15, 24, "#D7D0DC", "#25232A", "#66FB7299", "SemiBold", accentBrush: "#FFFB7299", accentWidth: 5),
            BilibiliArticleTextStyle.Code => WithStyle(vm, 13, 21, "#D8F5DD", "#18251C", "#4437D67A", "Normal", accentBrush: "#FF37D67A", accentWidth: 4),
            BilibiliArticleTextStyle.ListItem => WithStyle(vm, 15, 24, foreground, "#141218", "#22CAC4D0", block.IsBold ? "SemiBold" : "Normal", accentBrush: "#6607B6D5", accentWidth: 3),
            _ when block.IsBold || !string.IsNullOrWhiteSpace(block.TextColor) => WithStyle(vm, 15, 24, foreground, "#152831", "#6607B6D5", block.IsBold ? "SemiBold" : "Normal", accentBrush: "#FF07B6D5", accentWidth: 4),
            _ => WithStyle(vm, 15, 24, "#F4EFF4", "Transparent", "Transparent", "Normal"),
        };

        static BiliArticleDocumentBlockViewModel CreateHeadingStyle(BiliArticleDocumentBlockViewModel source, int headingLevel, string foreground)
        {
            return headingLevel switch
            {
                <= 1 => WithStyle(source, 27, 36, foreground == "#F4EFF4" ? "#FFFFFFFF" : foreground, "#33FB7299", "#CCFB7299", "Bold", accentBrush: "#FFFF4F8B", accentWidth: 7),
                2 => WithStyle(source, 23, 32, foreground == "#F4EFF4" ? "#FFFFFFFF" : foreground, "#26FB7299", "#99FB7299", "Bold", accentBrush: "#FFFB7299", accentWidth: 6),
                3 => WithStyle(source, 20, 29, foreground == "#F4EFF4" ? "#FFEAF2" : foreground, "#1E2233", "#7707B6D5", "SemiBold", accentBrush: "#FF07B6D5", accentWidth: 5),
                _ => WithStyle(source, 18, 27, foreground == "#F4EFF4" ? "#FFEAF2" : foreground, "#152831", "#6607B6D5", "SemiBold", accentBrush: "#CC07B6D5", accentWidth: 4),
            };
        }

        static BiliArticleDocumentBlockViewModel WithStyle(BiliArticleDocumentBlockViewModel source, int fontSize, int lineHeight, string foreground, string background, string borderBrush, string fontWeight, string accentBrush = "Transparent", int accentWidth = 0)
        {
            return new BiliArticleDocumentBlockViewModel
            {
                IsImage = source.IsImage,
                Text = source.Text,
                HeadingLevel = source.HeadingLevel,
                Indent = source.Indent,
                Margin = source.Margin,
                FontSize = fontSize,
                LineHeight = lineHeight,
                Foreground = foreground,
                Background = background,
                BorderBrush = borderBrush,
                FontWeight = fontWeight,
                AccentBrush = accentBrush,
                AccentWidth = accentWidth,
            };
        }
    }

    private static string? NormalizeCardColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var color = value.Trim();
        if (color.StartsWith('#') && (color.Length == 7 || color.Length == 9))
        {
            return color.Length == 7 ? "#FF" + color[1..] : color;
        }

        return null;
    }

    private static IEnumerable<BilibiliArticleBlock> BuildForwardBlocks(BilibiliArticleParseResult result)
    {
        if (result.Blocks.Count > 0)
        {
            return result.Blocks;
        }

        var blocks = new List<BilibiliArticleBlock>();
        if (!string.IsNullOrWhiteSpace(result.PlainText))
        {
            blocks.AddRange(SplitText(result.PlainText!, 900).Select(i => new BilibiliArticleBlock { Type = BilibiliArticleBlockType.Text, Text = i }));
        }

        blocks.AddRange(result.ImageUrls.Select(url => new BilibiliArticleBlock { Type = BilibiliArticleBlockType.Image, Url = url }));
        return blocks;
    }

    private static IEnumerable<BilibiliArticleBlock> BuildDocumentBlocksForRender(BilibiliArticleParseResult result)
    {
        if (result.Blocks.Count > 0)
        {
            return result.Blocks;
        }

        var blocks = new List<BilibiliArticleBlock>();
        if (!string.IsNullOrWhiteSpace(result.PlainText))
        {
            blocks.AddRange(SplitText(result.PlainText!, 900).Select(i => new BilibiliArticleBlock { Type = BilibiliArticleBlockType.Text, Text = i }));
        }

        blocks.AddRange(result.ImageUrls.Select(url => new BilibiliArticleBlock { Type = BilibiliArticleBlockType.Image, Url = url }));
        return blocks;
    }

    private static int EstimateTextBlockHeight(string text, int fontSize, int lineHeight, int width)
    {
        var charsPerLine = Math.Max(12, width / Math.Max(1, fontSize));
        var lines = text.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (double)charsPerLine)));
        return Math.Clamp(lines * lineHeight + 6, lineHeight + 8, 1200);
    }

    private static string BuildArticleHeaderText(BilibiliArticleParseResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(GetArticleKindText(result));
        if (!string.IsNullOrWhiteSpace(result.Title)) sb.AppendLine(result.Title);
        if (!string.IsNullOrWhiteSpace(result.AuthorName)) sb.AppendLine("作者：" + result.AuthorName);
        if (result.PublishTime is not null) sb.AppendLine($"发布时间：{result.PublishTime:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"数据：{FormatCount(result.ViewCount)}阅读 / {FormatCount(result.LikeCount)}赞 / {FormatCount(result.ReplyCount)}评论");
        return sb.ToString().TrimEnd();
    }

    private static string BuildArticleBodyText(BilibiliArticleParseResult result)
    {
        return BuildArticleSummary(result, 5000);
    }

    private static string BuildArticleSummary(BilibiliArticleParseResult result, int maxLength = 520)
    {
        var text = !string.IsNullOrWhiteSpace(result.Summary) ? result.Summary! : result.PlainText ?? string.Empty;
        return TrimLine(text, maxLength);
    }

    private static string BuildArticleAuthorMeta(BilibiliArticleParseResult result)
    {
        var parts = new List<string>();
        if (result.AuthorFans > 0) parts.Add($"{FormatCount(result.AuthorFans)}粉丝");
        if (result.Words > 0) parts.Add($"{result.Words}字");
        if (result.ImageUrls.Count > 0) parts.Add($"{result.ImageUrls.Count}图");
        return parts.Count > 0 ? string.Join(" · ", parts) : (result.IsOpus ? "Bilibili 图文" : "Bilibili 专栏");
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

    private string FormatBilibiliArticleResult(BilibiliArticleParseResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.IsOpus ? "Bilibili 图文解析成功" : "Bilibili 专栏解析成功");
        sb.AppendLine(result.IsOpus ? $"Opus：{result.OpusId}" : $"CV：cv{result.Cvid}");
        if (!string.IsNullOrWhiteSpace(result.Title)) sb.AppendLine($"标题：{TrimLine(result.Title, 140)}");
        if (!string.IsNullOrWhiteSpace(result.AuthorName)) sb.AppendLine($"作者：{result.AuthorName}");
        if (result.PublishTime is not null) sb.AppendLine($"发布时间：{result.PublishTime:yyyy-MM-dd HH:mm}");
        if (result.Words > 0) sb.AppendLine($"字数：{result.Words}");
        if (result.Categories.Count > 0) sb.AppendLine($"分类：{string.Join(" / ", result.Categories)}");
        if (!string.IsNullOrWhiteSpace(result.Summary)) sb.AppendLine($"摘要：{TrimLine(result.Summary, 220)}");
        else if (!string.IsNullOrWhiteSpace(result.PlainText)) sb.AppendLine($"摘要：{TrimLine(result.PlainText, 220)}");
        if (_config.IncludeCoverUrl && !string.IsNullOrWhiteSpace(result.BannerUrl)) sb.AppendLine($"封面：{result.BannerUrl}");
        sb.AppendLine($"图片数：{result.ImageUrls.Count}");
        sb.AppendLine($"数据：{FormatCount(result.ViewCount)}阅读 / {FormatCount(result.LikeCount)}赞 / {FormatCount(result.CoinCount)}投币 / {FormatCount(result.FavoriteCount)}收藏 / {FormatCount(result.ReplyCount)}评论");
        if (!string.IsNullOrWhiteSpace(result.SourceUrl)) sb.AppendLine($"链接：{result.SourceUrl}");
        return sb.ToString().TrimEnd();
    }

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

    private static string GetMessageScene(IncomingMessage message) => message switch
    {
        GroupIncomingMessage => "group",
        FriendIncomingMessage => "friend",
        TempIncomingMessage => "temp",
        _ => message.GetType().Name,
    };

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
