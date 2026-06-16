using Shirobot.Plugin.MyParser.Providers.Douyin.Models;
using System.Text.Json;
using Shirobot.Plugin.MyParser.Providers.Douyin.Abstractions;
using static Shirobot.Plugin.MyParser.Providers.Douyin.Utilities.DouyinParseHelpers;

namespace Shirobot.Plugin.MyParser.Providers.Douyin.Impl.WorkParsers;

internal sealed class DouyinLiveWorkParser : IDouyinWorkParser
{
    public bool CanParse(JsonElement aweme)
    {
        return TryGetProperty(aweme, "room", out _)
               || TryGetProperty(aweme, "live_room", out _)
               || TryGetProperty(aweme, "webcast_room", out _);
    }

    public DouyinParseResult Parse(JsonElement aweme, string fallbackAwemeId, string sourceUrl)
    {
        var awemeId = GetString(aweme, "aweme_id") ?? fallbackAwemeId;
        var author = TryGetProperty(aweme, "author", out var authorEl) ? authorEl : default;
        var statistics = TryGetProperty(aweme, "statistics", out var statisticsEl) ? statisticsEl : default;

        return new DouyinParseResult
        {
            AwemeId = awemeId,
            SourceUrl = sourceUrl,
            Title = GetString(aweme, "desc") ?? "抖音直播",
            AuthorName = author.ValueKind == JsonValueKind.Object ? GetString(author, "nickname") : null,
            AuthorId = author.ValueKind == JsonValueKind.Object ? GetString(author, "sec_uid") ?? GetString(author, "unique_id") : null,
            AuthorAvatarUrl = author.ValueKind == JsonValueKind.Object ? ExtractAuthorAvatarUrl(author) : null,
            AuthorFollowerCount = author.ValueKind == JsonValueKind.Object ? GetLong(author, "follower_count") : 0,
            AuthorRegion = author.ValueKind == JsonValueKind.Object ? GetString(author, "region") ?? GetString(author, "ip_location") : null,
            CoverUrl = ExtractLiveCoverUrl(aweme),
            CoverSource = "live",
            MusicUrl = null,
            LikeCount = statistics.ValueKind == JsonValueKind.Object ? GetLong(statistics, "digg_count") : 0,
            CollectCount = statistics.ValueKind == JsonValueKind.Object ? GetLong(statistics, "collect_count") : 0,
            CommentCount = statistics.ValueKind == JsonValueKind.Object ? GetLong(statistics, "comment_count") : 0,
            ShareCount = statistics.ValueKind == JsonValueKind.Object ? GetLong(statistics, "share_count") : 0,
            PlayCount = statistics.ValueKind == JsonValueKind.Object ? GetFirstLong(statistics, "play_count", "recommend_count") : 0,
            Tags = ExtractTags(aweme),
            Qualities = [],
            Images = [],
        };
    }

    private static string? ExtractLiveCoverUrl(JsonElement aweme)
    {
        foreach (var property in new[] { "room", "live_room", "webcast_room" })
        {
            if (!TryGetProperty(aweme, property, out var room) || room.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var coverProperty in new[] { "cover", "cover_url", "room_cover", "background" })
            {
                var url = ExtractFirstUrl(room, coverProperty) ?? GetString(room, coverProperty);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }
            }
        }

        return null;
    }
}
