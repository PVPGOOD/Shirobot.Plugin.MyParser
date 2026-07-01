using System.Diagnostics;
using System.Net;
using System.Text;
using MyParser.Provider.Douyin.Infrastructure;
using MyParser.Provider.Douyin.Models;
using MyParser.Provider.Douyin.Views;
using ShiroBot.AvaloniaSdk;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;
using Shirobot.Plugin.MyParser.Parsing;
using static MyParser.Provider.Douyin.Infrastructure.DouyinRequestHeaders;

namespace MyParser.Provider.Douyin.MessageHandling;

internal sealed partial class DouyinMessageHandler
{
private async Task SendCoverMessageAsync(IncomingMessage message, DouyinParseResult result)
    {
        var coverUri = await BuildCoverCardUriAsync(result);
        var segment = new ImageOutgoingSegment(coverUri);
        var stopwatch = Stopwatch.StartNew();
        BotLog.Info($"MyParser 封面卡片 ImageSegment 发送开始: aweme_id={result.AwemeId}, scene={GetMessageScene(message)}, uri_preview={_hostServices.PreviewUri(coverUri)}");

        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await _context.Message.SendGroupMessageAsync(group.Group.GroupId, segment);
                BotLog.Info($"MyParser 封面卡片 ImageSegment 发送接口完成: aweme_id={result.AwemeId}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
            case FriendIncomingMessage friend:
            {
                var response = await _context.Message.SendPrivateMessageAsync(friend.SenderId, segment);
                BotLog.Info($"MyParser 封面卡片 ImageSegment 发送接口完成: aweme_id={result.AwemeId}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
            default:
            {
                var response = await _context.Message.ReplyAsync(message, segment);
                BotLog.Info($"MyParser 封面卡片 ImageSegment 发送接口完成: aweme_id={result.AwemeId}, scene=reply, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
        }
    }

    private async Task<string> BuildCoverCardUriAsync(DouyinParseResult result)
    {
        var coverTask = BuildCoverImageAsync(result);
        var avatarTask = _context.Render is null
            ? Task.FromResult(new ProviderImageBuildResult(string.Empty, null))
            : BuildRemoteImageAsync(result.AuthorAvatarUrl, result.SourceUrl, $"douyin_avatar_{result.AwemeId}");
        var coverImage = await coverTask;
        var coverUri = coverImage.Uri;
        if (_context.Render is null)
        {
            BotLog.Warning($"MyParser Avalonia 渲染服务不可用，直接发送原始封面: aweme_id={result.AwemeId}");
            return coverUri;
        }

        try
        {
            var avatarImage = await avatarTask;
            var coverBitmap = !string.IsNullOrWhiteSpace(coverImage.LocalPath)
                ? _hostServices.DecodeImageFileForRender(coverImage.LocalPath)
                : _hostServices.DecodeBase64ImageForRender(coverUri);
            var avatarBitmap = !string.IsNullOrWhiteSpace(avatarImage.LocalPath)
                ? _hostServices.DecodeImageFileForRender(avatarImage.LocalPath)
                : _hostServices.DecodeBase64ImageForRender(avatarImage.Uri);
            BotLog.Info($"MyParser 封面卡片纹理准备: aweme_id={result.AwemeId}, bitmap={(coverBitmap is null ? "null" : "ok")}, avatar={(avatarBitmap is null ? "null" : "ok")}, mode=base64");

            var vm = new DouyinCardViewModel
            {
                Cover = coverBitmap,
                Avatar = avatarBitmap,
                CoverUri = coverUri,
                AlbumId = $"抖音 {result.AwemeId}",
                Title = string.IsNullOrWhiteSpace(result.Title) ? (result.IsGallery ? "抖音图文" : "抖音视频") : result.Title,
                Description = BuildDescriptionText(result),
                AuthorName = string.IsNullOrWhiteSpace(result.AuthorName) ? "@未知作者" : "@" + result.AuthorName,
                AuthorMeta = BuildAuthorMeta(result),
                DurationText = FormatDurationText(result.DurationMilliseconds),
                PageText = BuildCoverCardSubtitle(result),
                ViewCount = FormatCount(result.PlayCount),
                LikeCount = FormatCount(result.LikeCount),
                CollectCount = FormatCount(result.CollectCount),
                CommentCount = FormatCount(result.CommentCount),
                ShareCount = FormatCount(result.ShareCount),
                MusicText = BuildMusicText(result),
                TagsText = BuildTagsText(result),
            };
            var png = await _context.RenderControlPngAsync<DouyinCard>(vm, new ControlRenderOptions(RenderTheme.Auto));
            var uri = "base64://" + Convert.ToBase64String(png);
            BotLog.Info($"MyParser 封面卡片渲染完成: aweme_id={result.AwemeId}, cover_url={result.CoverUrl}, png_kb={png.Length / 1024d:F1}, view={typeof(DouyinCard).FullName}, mode=base64");
            return uri;
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 封面卡片渲染失败，直接发送原始封面: aweme_id={result.AwemeId}, cover_url={result.CoverUrl}, error={ex.Message}");
            return coverUri;
        }
    }

    private static string BuildCoverCardSubtitle(DouyinParseResult result)
    {
        var quality = result.Qualities.FirstOrDefault();
        var author = string.IsNullOrWhiteSpace(result.AuthorName) ? "未知作者" : result.AuthorName;
        if (result.IsGallery)
        {
            return $"{author} · 图文 {result.Images.Count} 张";
        }

        return quality is null ? author : $"{author} · {quality.Label}";
    }

    private static string BuildAuthorMeta(DouyinParseResult result)
    {
        var parts = new List<string>();
        if (result.AuthorFollowerCount > 0)
        {
            parts.Add($"{FormatCount(result.AuthorFollowerCount)}粉丝");
        }

        if (!string.IsNullOrWhiteSpace(result.AuthorRegion))
        {
            parts.Add(result.AuthorRegion);
        }

        parts.Add(result.PlayCount > 0 ? $"{FormatCount(result.PlayCount)}播放" : "播放量--");

        return parts.Count > 0 ? string.Join(" · ", parts) : "抖音作者";
    }

    private static string BuildDescriptionText(DouyinParseResult result)
    {
        return string.Empty;
    }

    private static string FormatDurationText(long milliseconds)
    {
        if (milliseconds <= 0)
        {
            return "--:--";
        }

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    private static string BuildMusicText(DouyinParseResult result)
    {
        if (string.IsNullOrWhiteSpace(result.MusicTitle) && string.IsNullOrWhiteSpace(result.MusicAuthor))
        {
            return "音乐：--";
        }

        if (string.IsNullOrWhiteSpace(result.MusicAuthor))
        {
            return "音乐：" + TrimLine(result.MusicTitle!, 34);
        }

        if (string.IsNullOrWhiteSpace(result.MusicTitle))
        {
            return "音乐：" + TrimLine(result.MusicAuthor!, 34);
        }

        return "音乐：" + TrimLine($"{result.MusicTitle} · {result.MusicAuthor}", 34);
    }

    private static string BuildTagsText(DouyinParseResult result)
    {
        return result.Tags.Count == 0
            ? "#抖音"
            : TrimLine(string.Join(" ", result.Tags.Take(5).Select(i => "#" + i)), 42);
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

    private Task<ProviderImageBuildResult> BuildCoverImageAsync(DouyinParseResult result)
    {
        var coverUrl = result.CoverUrl ?? throw new InvalidOperationException("封面 URL 为空。");
        return BuildRemoteImageAsync(coverUrl, result.SourceUrl, $"douyin_cover_{result.AwemeId}");
    }

    private Task<ProviderImageBuildResult> BuildRemoteImageAsync(string? imageUrl, string? referer, string filePrefix)
    {
        return _hostServices.BuildProviderImageAsync(new ProviderImageBuildRequest(
            "抖音",
            imageUrl,
            referer,
            filePrefix,
            request =>
            {
                request.Headers.TryAddWithoutValidation("User-Agent", DouyinConstants.UserAgent);
                request.Headers.TryAddWithoutValidation("Referer", referer ?? "https://www.douyin.com/");
                request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            }));
    }

    private void LogCoverImageInfo(DouyinParseResult result, string mode, long bytes, string uri)
    {
        BotLog.Info($"MyParser 封面 ImageSegment URI 模式：aweme_id={result.AwemeId}, mode={mode}, size_kb={(bytes > 0 ? bytes / 1024d : 0):F1}, uri_preview={_hostServices.PreviewUri(uri)}");
    }
}
