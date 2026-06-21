using MyParser.Provider.Douyin.Models;
using System.Text.Json;
using MyParser.Provider.Douyin.Abstractions;
using static MyParser.Provider.Douyin.Utilities.DouyinParseHelpers;

namespace MyParser.Provider.Douyin.WorkParsers;

public sealed class DouyinGalleryWorkParser : IDouyinWorkParser
{
    public bool CanParse(JsonElement aweme)
    {
        return TryGetProperty(aweme, "images", out var images)
               && images.ValueKind == JsonValueKind.Array
               && images.GetArrayLength() > 0
               && ExtractImages(aweme).Count > 0;
    }

    public DouyinParseResult Parse(JsonElement aweme, string fallbackAwemeId, string sourceUrl)
    {
        var awemeId = GetString(aweme, "aweme_id") ?? fallbackAwemeId;
        var title = GetString(aweme, "desc");
        var author = TryGetProperty(aweme, "author", out var authorEl) ? authorEl : default;
        var video = TryGetProperty(aweme, "video", out var videoEl) ? videoEl : default;
        var images = ExtractImages(aweme);
        var musicEl = TryGetProperty(aweme, "music", out var music) ? music : default;
        var statistics = TryGetProperty(aweme, "statistics", out var statisticsEl) ? statisticsEl : default;

        return new DouyinParseResult
        {
            AwemeId = awemeId,
            SourceUrl = sourceUrl,
            Title = title,
            AuthorName = author.ValueKind == JsonValueKind.Object ? GetString(author, "nickname") : null,
            AuthorId = author.ValueKind == JsonValueKind.Object ? GetString(author, "sec_uid") ?? GetString(author, "unique_id") : null,
            AuthorAvatarUrl = author.ValueKind == JsonValueKind.Object ? ExtractAuthorAvatarUrl(author) : null,
            AuthorFollowerCount = author.ValueKind == JsonValueKind.Object ? GetLong(author, "follower_count") : 0,
            AuthorRegion = author.ValueKind == JsonValueKind.Object ? GetString(author, "region") ?? GetString(author, "ip_location") : null,
            DurationMilliseconds = video.ValueKind == JsonValueKind.Object ? GetLong(video, "duration") : 0,
            CoverUrl = images.FirstOrDefault()?.Url ?? ExtractSimpleCoverUrl(video),
            CoverSource = "detail_image",
            VideoUrl = null,
            MusicUrl = musicEl.ValueKind == JsonValueKind.Object ? ExtractFirstUrl(musicEl, "play_url") : null,
            MusicTitle = musicEl.ValueKind == JsonValueKind.Object ? GetString(musicEl, "title") : null,
            MusicAuthor = musicEl.ValueKind == JsonValueKind.Object ? GetString(musicEl, "author") : null,
            LikeCount = statistics.ValueKind == JsonValueKind.Object ? GetLong(statistics, "digg_count") : 0,
            CollectCount = statistics.ValueKind == JsonValueKind.Object ? GetLong(statistics, "collect_count") : 0,
            CommentCount = statistics.ValueKind == JsonValueKind.Object ? GetLong(statistics, "comment_count") : 0,
            ShareCount = statistics.ValueKind == JsonValueKind.Object ? GetLong(statistics, "share_count") : 0,
            PlayCount = statistics.ValueKind == JsonValueKind.Object ? GetFirstLong(statistics, "play_count", "recommend_count") : 0,
            Tags = ExtractTags(aweme),
            Qualities = [],
            Images = images,
        };
    }

    private static List<DouyinImageInfo> ExtractImages(JsonElement aweme)
    {
        var result = new List<DouyinImageInfo>();
        if (!TryGetProperty(aweme, "images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var img in images.EnumerateArray())
        {
            var imageUrl = EnumerateUrlList(img).FirstOrDefault(u => !u.Contains(".webp", StringComparison.OrdinalIgnoreCase))
                ?? EnumerateUrlList(img).FirstOrDefault();
            string? livePhotoUrl = null;
            if (TryGetProperty(img, "video", out var imgVideo)
                && TryGetProperty(imgVideo, "play_addr", out var playAddr))
            {
                livePhotoUrl = EnumerateUrlList(playAddr).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(livePhotoUrl))
                {
                    livePhotoUrl = NormalizeNoWatermarkUrl(livePhotoUrl);
                }
            }

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                result.Add(new DouyinImageInfo { Url = imageUrl, LivePhotoUrl = livePhotoUrl });
            }
        }

        return result.GroupBy(i => i.Url).Select(g => g.First()).ToList();
    }
}
