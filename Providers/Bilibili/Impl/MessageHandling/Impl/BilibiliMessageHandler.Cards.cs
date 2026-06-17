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

internal sealed partial class BilibiliMessageHandler
{
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
        var coverTask = BuildRemoteImageAsync(result.CoverUrl, result.SourceUrl, $"bilibili_cover_{result.Bvid}");
        var avatarTask = _context.Render is null
            ? Task.FromResult<(string Uri, string? LocalPath)>((string.Empty, null))
            : BuildRemoteImageAsync(result.AuthorAvatarUrl, result.SourceUrl, $"bilibili_avatar_{result.Bvid}");
        var coverImage = await coverTask;
        var coverUri = coverImage.Uri;
        if (_context.Render is null)
        {
            BotLog.Warning($"MyParser Bilibili Avalonia 渲染服务不可用，直接发送原始封面: bvid={result.Bvid}");
            return coverUri;
        }

        try
        {
            var avatarImage = await avatarTask;
            var coverBitmap = !string.IsNullOrWhiteSpace(coverImage.LocalPath)
                ? RenderBitmapUtilities.DecodeImageFileForRender(coverImage.LocalPath)
                : RenderBitmapUtilities.DecodeBase64ImageForRender(coverUri);
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

    private Task<(string Uri, string? LocalPath)> BuildRemoteImageAsync(string? imageUrl, string? referer, string filePrefix)
    {
        return RemoteImageFetchService.BuildRemoteImageAsync(
            CoverHttp,
            "Bilibili",
            imageUrl,
            referer,
            filePrefix,
            ResolveCoverDownloadDirectory(),
            request =>
            {
                request.Headers.TryAddWithoutValidation("User-Agent", BilibiliConstants.UserAgent);
                request.Headers.TryAddWithoutValidation("Referer", referer ?? BilibiliConstants.Origin + "/");
                request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
                if (!string.IsNullOrWhiteSpace(_config.BilibiliCookie))
                {
                    request.Headers.TryAddWithoutValidation("Cookie", _config.BilibiliCookie);
                }
            });
    }

}
