using System.Diagnostics;
using System.Net;
using System.Text;
using Net.Codecrete.QrCodeGenerator;
using ShiroBot.AvaloniaSdk;
using Shirobot.Plugin.MyParser.Parsing;
using Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Facade;
using Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Infrastructure;
using Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Models;
using Shirobot.Plugin.MyParser.Providers.Xiaohongshu.ViewModels;
using Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Views;
using Shirobot.Plugin.MyParser.Utility;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Impl.MessageHandling;

internal sealed class XiaohongshuMessageHandler : IDisposable
{
    private static readonly HttpClient ImageHttp = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
    });

    private readonly IBotContext _context;
    private readonly MyParserConfig _config;
    private readonly ParseProviderRegistry _providerRegistry;
    private readonly XiaohongshuParseProvider _provider;
    private LocalVideoHttpServer? _localVideoHttpServer;

    public XiaohongshuMessageHandler(IBotContext context, MyParserConfig config, ParseProviderRegistry providerRegistry, XiaohongshuParseProvider provider)
    {
        _context = context;
        _config = config;
        _providerRegistry = providerRegistry;
        _provider = provider;
    }

    public async Task ParseAndReplyAsync(IncomingMessage message, string text)
    {
        try
        {
            await TryReactToSourceMessageAsync(message, "351");
            var media = await _providerRegistry.ParseAsync(text);
            if (media.ProviderPayload is not XiaohongshuParseResult result)
            {
                await TryReactToSourceMessageAsync(message, "379");
                await ReplyAsync(message, "小红书链接已识别，但发送流程尚未接入。");
                return;
            }

            if (result.IsVideo && _config.SendVideoAsFile)
            {
                await SendVideoFlowAsync(message, result);
                await TryReactToSourceMessageAsync(message, "426");
                return;
            }

            if (result.IsGallery)
            {
                await SendGalleryForwardAsync(message, result);
                await SendGalleryCardAsync(message, result);
                await TryReactToSourceMessageAsync(message, "426");
                return;
            }

            await ReplyAsync(message, FormatResult(result));
            await TryReactToSourceMessageAsync(message, "426");
        }
        catch (XiaohongshuSignRequiredException ex)
        {
            await TryReactToSourceMessageAsync(message, "379");
            await ReplyAsync(message, "小红书解析需要 xhshow sign 服务：" + ex.Message);
        }
        catch (XiaohongshuParseException ex)
        {
            await TryReactToSourceMessageAsync(message, "379");
            await ReplyAsync(message, "小红书解析失败：" + ex.Message);
        }
        catch (TaskCanceledException)
        {
            await TryReactToSourceMessageAsync(message, "379");
            await ReplyAsync(message, "小红书解析超时，请稍后重试。若经常失败，请检查 Cookie / sign 服务。");
        }
        catch (Exception ex)
        {
            await TryReactToSourceMessageAsync(message, "379");
            BotLog.Error($"MyParser 小红书解析异常：{ex}");
            await ReplyAsync(message, "小红书解析异常：" + ex.Message);
        }
    }

    public async Task HandleLoginAsync(IncomingMessage message)
    {
        try
        {
            var session = await _provider.Parser.GenerateQrLoginSessionAsync();
            await ReplyAsync(message,
                "小红书扫码登录\n"
                + "请用小红书 App 扫描下面二维码，并在 3 分钟内确认登录。\n"
                + "如果触发安全验证，请改用浏览器登录后复制 Cookie。\n"
                + $"如果二维码图片无法显示，请打开：{session.Url}");
            await SendQrImageAsync(message, session.Url, $"xhs_qr_{session.QrId}");

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var current = session;
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
                var poll = await _provider.Parser.PollQrLoginAsync(current, cts.Token);
                current = current with { Cookie = poll.Cookie };
                if (poll.NeedVerify)
                {
                    await ReplyAsync(message, poll.Message);
                    return;
                }

                if (poll.IsLogin)
                {
                    SaveXiaohongshuCookieToPluginDirectory();
                    _context.Config.Save(_config);
                    await ReplyAsync(message, $"小红书登录成功：{poll.UserName ?? "已登录"}。Cookie 已保存到配置和插件目录。");
                    return;
                }

                BotLog.Info($"MyParser 小红书二维码轮询: status={poll.CodeStatus}, message={poll.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            await ReplyAsync(message, "小红书登录二维码已超时，请重新发送登录命令。");
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 小红书扫码登录失败：{ex}");
            await ReplyAsync(message, "小红书扫码登录失败：" + ex.Message);
        }
    }

    private async Task SendVideoFlowAsync(IncomingMessage message, XiaohongshuParseResult result)
    {
        string? fileUploadInfo = null;
        try
        {
            var videoSegment = await BuildVideoSegmentAsync(result);
            await SendCoverOrCardAsync(message, result);
            await SendVideoMessageAsync(message, result, videoSegment);

            if (_config.UploadVideoAsFile && !_config.UploadVideoAsFileOnlyOnVideoSendFailure && !string.IsNullOrWhiteSpace(result.LocalVideoPath))
            {
                fileUploadInfo = await UploadVideoFileAsync(message, result);
                BotLog.Info($"MyParser 小红书文件上传完成: note_id={result.NoteId}, {fileUploadInfo}");
            }

            if (!result.LocalVideoRegisteredToHttpServer)
            {
                LocalMediaCleanup.DeleteLocalVideoIfConfigured(_config, result.LocalVideoPath, "xiaohongshu");
            }
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 小红书视频消息发送失败: note_id={result.NoteId}, error={ex.Message}");
            if (_config.UploadVideoAsFile && !string.IsNullOrWhiteSpace(result.LocalVideoPath))
            {
                try
                {
                    fileUploadInfo = await UploadVideoFileAsync(message, result);
                    if (result.LocalVideoRegisteredToHttpServer)
                    {
                        _localVideoHttpServer?.UnregisterFile(result.LocalVideoPath);
                    }

                    LocalMediaCleanup.DeleteLocalVideoIfConfigured(_config, result.LocalVideoPath, "xiaohongshu");
                    BotLog.Info($"MyParser 小红书 VideoSegment 失败后文件上传完成: note_id={result.NoteId}, {fileUploadInfo}");
                    return;
                }
                catch (Exception uploadEx)
                {
                    throw new XiaohongshuParseException("视频发送失败，文件上传也失败：" + uploadEx.Message);
                }
            }

            throw;
        }
    }

    private async Task<VideoOutgoingSegment> BuildVideoSegmentAsync(XiaohongshuParseResult result)
    {
        var (fileUri, localPath) = await _provider.Parser.DownloadVideoAsync(result);
        result.LocalVideoFileUri = fileUri;
        result.LocalVideoPath = localPath;
        var fileSize = new FileInfo(localPath).Length;
        var useBase64 = _config.SendVideoSegmentAsBase64 && MemorySafetyUtilities.CanUseBase64ForFile(fileSize, _config.VideoSegmentBase64MaxMegabytes);
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

        BotLog.Info($"MyParser 小红书 VideoSegment URI 模式：{uriMode}, file_mb={fileSize / 1024d / 1024d:F2}, uri_preview={MediaUriUtilities.PreviewUri(videoUri)}");
        var thumbUri = _config.IncludeVideoThumbUri && !string.IsNullOrWhiteSpace(result.CoverUrl) ? result.CoverUrl : null;
        return new VideoOutgoingSegment(videoUri, thumbUri);
    }

    private async Task SendVideoMessageAsync(IncomingMessage message, XiaohongshuParseResult result, VideoOutgoingSegment videoSegment)
    {
        var segments = new OutgoingSegment[] { videoSegment };
        var stopwatch = Stopwatch.StartNew();
        BotLog.Info($"MyParser 小红书 VideoSegment 发送开始: note_id={result.NoteId}, scene={GetMessageScene(message)}, uri_mode={MediaUriUtilities.GetUriMode(videoSegment.Uri)}");
        switch (message)
        {
            case GroupIncomingMessage group:
                await _context.Message.SendGroupMessageAsync(group.Group.GroupId, segments);
                break;
            case FriendIncomingMessage friend:
                await _context.Message.SendPrivateMessageAsync(friend.SenderId, segments);
                break;
            default:
                await _context.Message.ReplyAsync(message, segments);
                break;
        }

        BotLog.Info($"MyParser 小红书 VideoSegment 发送完成: note_id={result.NoteId}, elapsed={stopwatch.Elapsed:mm\\:ss}");
    }

    private async Task SendCoverOrCardAsync(IncomingMessage message, XiaohongshuParseResult result)
    {
        if (!_config.SendCoverWithVideoSegment || string.IsNullOrWhiteSpace(result.CoverUrl))
        {
            return;
        }

        var image = await BuildRemoteImageAsync(result.CoverUrl, result.SourceUrl, $"xhs_cover_{result.NoteId}");
        if (!string.IsNullOrWhiteSpace(image.Uri))
        {
            await SendImageAsync(message, new ImageOutgoingSegment(image.Uri));
        }
    }

    private async Task SendGalleryForwardAsync(IncomingMessage message, XiaohongshuParseResult result)
    {
        var max = Math.Clamp(_config.MaxImagesToShow, 1, 20);
        var forwarded = new List<OutgoingForwardedMessage>();
        var senderId = GetBotOrSenderId(message);
        var senderName = string.IsNullOrWhiteSpace(result.AuthorName) ? "小红书图文" : result.AuthorName!;
        forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, [new TextOutgoingSegment(BuildHeaderText(result))]));
        foreach (var (image, index) in result.Images.Take(max).Select((image, index) => (image, index + 1)))
        {
            var local = await BuildRemoteImageAsync(image.Url, result.SourceUrl, $"xhs_image_{result.NoteId}_{index:D2}");
            if (!string.IsNullOrWhiteSpace(local.Uri))
            {
                forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, [new ImageOutgoingSegment(local.Uri)]));
            }
        }

        if (result.Comments.Count > 0)
        {
            forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, [new TextOutgoingSegment(BuildCommentsText(result))]));
        }

        if (!string.IsNullOrWhiteSpace(result.SourceUrl))
        {
            forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, [new TextOutgoingSegment("原文：" + result.SourceUrl)]));
        }

        var title = string.IsNullOrWhiteSpace(result.Title) ? "小红书图文" : TrimLine(result.Title!, 48);
        var preview = new[] { "小红书图文", senderName, result.Comments.Count > 0 ? $"前 {result.Comments.Count} 条评论" : $"图片 {result.Images.Count} 张" };
        var summary = $"{result.Images.Count} 张图 · {result.Comments.Count} 条评论";
        var forward = new ForwardOutgoingSegment(forwarded, title, preview, summary, "小红书图文");
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

    private async Task SendGalleryCardAsync(IncomingMessage message, XiaohongshuParseResult result)
    {
        var cardUri = await BuildGalleryCardUriAsync(result);
        if (!string.IsNullOrWhiteSpace(cardUri))
        {
            await SendImageAsync(message, new ImageOutgoingSegment(cardUri));
        }
    }

    private async Task<string> BuildGalleryCardUriAsync(XiaohongshuParseResult result)
    {
        if (_context.Render is null || result.Images.Count == 0)
        {
            return string.Empty;
        }

        try
        {
            var cover = await BuildRemoteImageAsync(result.Images[0].Url, result.SourceUrl, $"xhs_card_cover_{result.NoteId}");
            var second = await BuildRemoteImageAsync(result.Images.ElementAtOrDefault(1)?.Url ?? result.Images[0].Url, result.SourceUrl, $"xhs_card_second_{result.NoteId}");
            var avatar = await BuildRemoteImageAsync(result.AuthorAvatarUrl, result.SourceUrl, $"xhs_card_avatar_{result.NoteId}");
            var vm = new XiaohongshuCardViewModel
            {
                Cover = !string.IsNullOrWhiteSpace(cover.LocalPath) ? RenderBitmapUtilities.DecodeImageFileForRender(cover.LocalPath) : RenderBitmapUtilities.DecodeBase64ImageForRender(cover.Uri),
                SecondImage = !string.IsNullOrWhiteSpace(second.LocalPath) ? RenderBitmapUtilities.DecodeImageFileForRender(second.LocalPath) : RenderBitmapUtilities.DecodeBase64ImageForRender(second.Uri),
                Avatar = !string.IsNullOrWhiteSpace(avatar.LocalPath) ? RenderBitmapUtilities.DecodeImageFileForRender(avatar.LocalPath) : RenderBitmapUtilities.DecodeBase64ImageForRender(avatar.Uri),
                Title = string.IsNullOrWhiteSpace(result.Title) ? "小红书图文" : result.Title!,
                Description = string.IsNullOrWhiteSpace(result.Description) ? "" : result.Description!,
                AuthorName = string.IsNullOrWhiteSpace(result.AuthorName) ? "未知作者" : result.AuthorName!,
                MetaText = $"{result.Images.Count} 图 · {result.Comments.Count} 条评论",
                StatsText = $"{FormatCount(result.LikeCount)}赞 · {FormatCount(result.CollectCount)}收藏 · {FormatCount(result.CommentCount)}评论",
                TagsText = result.Tags.Count > 0 ? string.Join(" ", result.Tags.Take(6).Select(i => "#" + i)) : "# 小红书",
                Comments = result.Comments.Take(10).Select((comment, index) => new XiaohongshuCommentViewModel
                {
                    Index = (index + 1).ToString(),
                    Nickname = comment.User.Nickname,
                    Content = string.IsNullOrWhiteSpace(comment.Content) ? "[空评论]" : comment.Content,
                    Meta = $"{FormatCount(comment.LikeCount)}赞" + (string.IsNullOrWhiteSpace(comment.IpLocation) ? "" : " · " + comment.IpLocation),
                }).ToList(),
            };
            if (vm.Comments.Count == 0)
            {
                vm.Comments.Add(new XiaohongshuCommentViewModel { Index = "1", Nickname = "提示", Content = "没有获取到评论；评论接口需要登录 Cookie、xsec_token 和 xhshow sign 服务。", Meta = "MyParser" });
            }

            var png = await _context.RenderControlPngAsync<XiaohongshuCard>(vm, new ControlRenderOptions(RenderTheme.Dark));
            var dir = Path.Combine(ResolveDownloadDirectory(), "cards");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"xhs_card_{SanitizeLocalFileName(result.NoteId)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png");
            await File.WriteAllBytesAsync(path, png);
            BotLog.Info($"MyParser 小红书图文卡片渲染完成: note_id={result.NoteId}, comments={result.Comments.Count}, png_kb={png.Length / 1024d:F1}, file={path}");
            return "base64://" + Convert.ToBase64String(png);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 小红书图文卡片渲染失败: note_id={result.NoteId}, error={ex.Message}");
            return string.Empty;
        }
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
            request.Headers.TryAddWithoutValidation("User-Agent", XiaohongshuConstants.UserAgent);
            request.Headers.TryAddWithoutValidation("Referer", referer ?? XiaohongshuConstants.Origin + "/");
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            if (!string.IsNullOrWhiteSpace(_config.XiaohongshuCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", _config.XiaohongshuCookie);
            }

            using var response = await ImageHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            const long maxBytes = 10 * 1024L * 1024L;
            if (response.Content.Headers.ContentLength is > maxBytes)
            {
                return (imageUrl, null);
            }

            await using var input = await response.Content.ReadAsStreamAsync();
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer);
                if (read == 0) break;
                total += read;
                if (total > maxBytes) return (imageUrl, null);
                output.Write(buffer, 0, read);
            }

            var bytes = output.ToArray();
            var ext = MediaUriUtilities.GuessImageExtension(response.Content.Headers.ContentType?.MediaType, imageUrl, bytes);
            var dir = ResolveDownloadDirectory();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{SanitizeLocalFileName(filePrefix)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{ext}");
            await File.WriteAllBytesAsync(path, bytes);
            return ("base64://" + Convert.ToBase64String(bytes), path);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 小红书图片下载失败，回退 URL: url={imageUrl}, error={ex.Message}");
            return (imageUrl, null);
        }
    }

    private async Task SendQrImageAsync(IncomingMessage message, string text, string fileName)
    {
        var qrFile = await BuildQrImageAsync(text, fileName);
        await SendImageAsync(message, new ImageOutgoingSegment(qrFile.Uri));
    }

    private static async Task<(string Uri, string Path)> BuildQrImageAsync(string text, string fileName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser", "xiaohongshu", "qr");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName + ".png");
        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        var png = qr.ToPngBitmap(border: 4, scale: 8);
        await File.WriteAllBytesAsync(path, png);
        return ("base64://" + Convert.ToBase64String(png), path);
    }

    private async Task SendImageAsync(IncomingMessage message, ImageOutgoingSegment segment)
    {
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

    private async Task<string> UploadVideoFileAsync(IncomingMessage message, XiaohongshuParseResult result)
    {
        if (string.IsNullOrWhiteSpace(result.LocalVideoPath) || !File.Exists(result.LocalVideoPath))
        {
            throw new InvalidOperationException("本地视频文件不存在。");
        }

        var localPath = Path.GetFullPath(result.LocalVideoPath);
        var fileSize = new FileInfo(localPath).Length;
        var useBase64 = _config.UploadVideoAsBase64 && MemorySafetyUtilities.CanUseBase64ForFile(fileSize, _config.UploadVideoBase64MaxMegabytes);
        var fileUri = useBase64 ? "base64://" + Convert.ToBase64String(await File.ReadAllBytesAsync(localPath)) : new Uri(localPath).AbsoluteUri;
        var fileName = Path.GetFileName(localPath);
        var uploadMode = useBase64 ? "base64" : "file";
        var stopwatch = Stopwatch.StartNew();
        return message switch
        {
            GroupIncomingMessage group => $"群文件 FileId={(await _context.File.UploadGroupFileAsync(group.Group.GroupId, fileUri, fileName)).FileId} Mode={uploadMode} elapsed={stopwatch.Elapsed:mm\\:ss}",
            FriendIncomingMessage friend => $"私聊文件 FileId={(await _context.File.UploadPrivateFileAsync(friend.SenderId, fileUri, fileName)).FileId} Mode={uploadMode} elapsed={stopwatch.Elapsed:mm\\:ss}",
            _ => throw new NotSupportedException("当前消息类型不支持文件上传。"),
        };
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
            BotLog.Warning($"MyParser 小红书消息贴表情失败: group_id={group.Group.GroupId}, message_seq={group.MessageSeq}, face={faceId}, error={ex.Message}");
        }
    }

    private void SaveXiaohongshuCookieToPluginDirectory()
    {
        var pluginDir = Path.GetDirectoryName(_context.Config.ConfigPath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(pluginDir);
        var fileName = string.IsNullOrWhiteSpace(_config.XiaohongshuCookieFileName) ? "xiaohongshu_cookie.txt" : _config.XiaohongshuCookieFileName.Trim();
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(pluginDir, fileName);
        File.WriteAllText(path, _config.XiaohongshuCookie ?? string.Empty, Encoding.UTF8);
    }

    private Task ReplyAsync(IncomingMessage message, string text)
    {
        return _config.QuoteReply ? _context.Message.QuoteReplyAsync(message, text) : _context.Message.ReplyAsync(message, text);
    }

    private LocalVideoHttpServer GetLocalVideoHttpServer()
    {
        return _localVideoHttpServer ??= new LocalVideoHttpServer(_config.LocalVideoHttpHost, _config.LocalVideoHttpPort, _config.LocalVideoHttpPublicBaseUrl, _config.AllowLanAccessToLocalVideoHttpServer);
    }

    private static string BuildHeaderText(XiaohongshuParseResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.IsVideo ? "小红书视频" : "小红书图文");
        if (!string.IsNullOrWhiteSpace(result.Title)) sb.AppendLine(result.Title);
        if (!string.IsNullOrWhiteSpace(result.AuthorName)) sb.AppendLine("作者：" + result.AuthorName);
        if (!string.IsNullOrWhiteSpace(result.Description)) sb.AppendLine(TrimLine(result.Description, 600));
        sb.AppendLine($"数据：{FormatCount(result.LikeCount)}赞 / {FormatCount(result.CollectCount)}收藏 / {FormatCount(result.CommentCount)}评论");
        return sb.ToString().TrimEnd();
    }

    private static string BuildCommentsText(XiaohongshuParseResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("前 10 条评论");
        foreach (var (comment, index) in result.Comments.Take(10).Select((comment, index) => (comment, index + 1)))
        {
            sb.AppendLine($"{index}. {comment.User.Nickname}: {comment.Content}");
        }

        return sb.ToString().TrimEnd();
    }

    private string FormatResult(XiaohongshuParseResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.IsVideo ? "小红书视频解析成功" : "小红书解析成功");
        sb.AppendLine($"Note：{result.NoteId}");
        if (!string.IsNullOrWhiteSpace(result.Title)) sb.AppendLine($"标题：{TrimLine(result.Title, 140)}");
        if (!string.IsNullOrWhiteSpace(result.AuthorName)) sb.AppendLine($"作者：{result.AuthorName}");
        if (!string.IsNullOrWhiteSpace(result.Description)) sb.AppendLine($"摘要：{TrimLine(result.Description, 220)}");
        sb.AppendLine($"图片数：{result.Images.Count}");
        sb.AppendLine($"评论：已获取 {result.Comments.Count} 条");
        sb.AppendLine($"数据：{FormatCount(result.LikeCount)}赞 / {FormatCount(result.CollectCount)}收藏 / {FormatCount(result.CommentCount)}评论");
        if (!string.IsNullOrWhiteSpace(result.SourceUrl)) sb.AppendLine($"链接：{result.SourceUrl}");
        return sb.ToString().TrimEnd();
    }

    private static long GetBotOrSenderId(IncomingMessage message) => message switch
    {
        GroupIncomingMessage group => group.SenderId,
        FriendIncomingMessage friend => friend.SenderId,
        TempIncomingMessage temp => temp.SenderId,
        _ => 0,
    };

    private static string GetMessageScene(IncomingMessage message) => message switch
    {
        GroupIncomingMessage => "group",
        FriendIncomingMessage => "friend",
        TempIncomingMessage => "temp",
        _ => message.GetType().Name,
    };

    private string ResolveDownloadDirectory()
    {
        return Path.IsPathRooted(_config.XiaohongshuDownloadDirectory)
            ? _config.XiaohongshuDownloadDirectory
            : Path.Combine(AppContext.BaseDirectory, _config.XiaohongshuDownloadDirectory);
    }

    private static string FormatCount(long value)
    {
        if (value <= 0) return "--";
        if (value >= 100_000_000) return $"{value / 100_000_000d:F1}亿";
        if (value >= 10_000) return $"{value / 10_000d:F1}万";
        return value.ToString();
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
