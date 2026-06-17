namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Models;

internal sealed record BilibiliBangumiParseResult
{
    public long? MediaId { get; init; }
    public long? SeasonId { get; init; }
    public long? RequestedEpId { get; init; }
    public string? Title { get; init; }
    public string? CoverUrl { get; init; }
    public string? Evaluate { get; init; }
    public string? MediaUrl { get; init; }
    public string? SeasonUrl { get; init; }
    public string? PublishText { get; init; }
    public string? RatingText { get; init; }
    public string? PlayText { get; init; }
    public string? FollowText { get; init; }
    public IReadOnlyList<string> Styles { get; init; } = [];
    public IReadOnlyList<BilibiliBangumiEpisodeInfo> Episodes { get; init; } = [];
}

internal sealed record BilibiliBangumiEpisodeVideoParseResult
{
    public required BilibiliBangumiParseResult Bangumi { get; init; }
    public required BilibiliParseResult Video { get; init; }
}

internal sealed record BilibiliBangumiEpisodeInfo
{
    public int Index { get; init; }
    public long? EpId { get; init; }
    public long? Cid { get; init; }
    public long Aid { get; init; }
    public string? Bvid { get; init; }
    public string? Title { get; init; }
    public string? LongTitle { get; init; }
    public string? CoverUrl { get; init; }
    public long DurationMilliseconds { get; init; }
    public string Url => EpId is null ? string.Empty : $"https://www.bilibili.com/bangumi/play/ep{EpId}";
}
