using System.Text.RegularExpressions;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Utilities;

internal static partial class BilibiliUrlParser
{
    public static bool ContainsStrictBilibiliUrl(string text)
    {
        return ExtractStrictBilibiliUrl(text) is not null;
    }

    public static string? ExtractStrictBilibiliUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = StrictBilibiliUrlRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var url = match.Value.TrimEnd('.', '。', ',', '，', ')', '）', ']', '】', '>', '》');
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
    }

    public static string? NormalizeStandaloneBilibiliId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim().TrimEnd('.', '。', ',', '，');
        var match = StandaloneBilibiliIdRegex().Match(value);
        if (!match.Success)
        {
            return null;
        }

        var prefix = match.Groups["prefix"].Value.ToLowerInvariant();
        var id = match.Groups["id"].Value;
        return prefix switch
        {
            "bv" => $"https://www.bilibili.com/video/BV{id}/",
            "av" => $"https://www.bilibili.com/video/av{id}/",
            "cv" => $"https://www.bilibili.com/read/cv{id}/",
            "opus" => $"https://www.bilibili.com/opus/{id}",
            "ep" => $"https://www.bilibili.com/bangumi/play/ep{id}",
            "ss" => $"https://www.bilibili.com/bangumi/play/ss{id}",
            "md" => $"https://www.bilibili.com/bangumi/media/md{id}",
            _ => null,
        };
    }

    public static bool ContainsBilibiliUrl(string text)
    {
        return ExtractBvid(text) is not null || ExtractCvid(text) is not null || ExtractOpusId(text) is not null || ExtractLiveRoomId(text) is not null || ExtractBangumiIds(text).HasAny || ExtractB23Url(text) is not null;
    }

    public static string? ExtractBvid(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = BvidRegex().Match(text);
        return match.Success ? match.Value : null;
    }

    public static long? ExtractCvid(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = CvidRegex().Match(text);
        return match.Success && long.TryParse(match.Groups[1].Value, out var cvid) ? cvid : null;
    }

    public static string? ExtractOpusId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = OpusRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    public static string? ExtractLiveRoomId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = LiveRoomRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static int? ExtractVideoPage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var match in VideoPageRegex().Matches(text).Cast<Match>())
        {
            if (int.TryParse(match.Groups[1].Value, out var page) && page > 0)
            {
                return page;
            }
        }

        return null;
    }

    public static BilibiliBangumiIds ExtractBangumiIds(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new BilibiliBangumiIds(null, null, null);
        }

        var ep = BangumiEpRegex().Match(text);
        var ss = BangumiSeasonRegex().Match(text);
        var md = BangumiMediaRegex().Match(text);
        return new BilibiliBangumiIds(
            ep.Success && long.TryParse(ep.Groups[1].Value, out var epId) ? epId : null,
            ss.Success && long.TryParse(ss.Groups[1].Value, out var seasonId) ? seasonId : null,
            md.Success && long.TryParse(md.Groups[1].Value, out var mediaId) ? mediaId : null);
    }

    public static string? ExtractB23Url(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = B23UrlRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var url = match.Value.TrimEnd('.', '。', ',', '，', ')', '）', ']', '】');
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
    }

    public static string? ExtractBilibiliUrl(string text)
    {
        var strict = ExtractStrictBilibiliUrl(text);
        if (strict is not null)
        {
            return strict;
        }

        var standalone = NormalizeStandaloneBilibiliId(text);
        if (standalone is not null)
        {
            return standalone;
        }

        var bvid = ExtractBvid(text);
        if (bvid is not null)
        {
            return $"https://www.bilibili.com/video/{bvid}/";
        }

        return ExtractB23Url(text);
    }

    public static long? ExtractAid(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = AidRegex().Match(text);
        return match.Success && long.TryParse(match.Groups[1].Value, out var aid) ? aid : null;
    }

    [GeneratedRegex(@"(?:https?://)?(?:(?:www|m|live|t|space)\.)?bilibili\.com/[^\s<>\""'，。]+|(?:https?://)?b23\.tv/[0-9A-Za-z]+|(?:https?://)?bili2233\.cn/[0-9A-Za-z]+", RegexOptions.IgnoreCase)]
    private static partial Regex StrictBilibiliUrlRegex();

    [GeneratedRegex(@"^(?<prefix>BV)(?<id>[0-9A-Za-z]{10})$|^(?<prefix>av|cv|opus|ep|ss|md)(?<id>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneBilibiliIdRegex();

    [GeneratedRegex("BV[0-9A-Za-z]{10}", RegexOptions.IgnoreCase)]
    private static partial Regex BvidRegex();

    [GeneratedRegex(@"(?:/video/)?av(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AidRegex();

    [GeneratedRegex(@"(?:cv|/read/cv)(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CvidRegex();

    [GeneratedRegex(@"/opus/(\d+)|\bopus(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpusRegex();

    [GeneratedRegex(@"live\.bilibili\.com/(?:blanc/)?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LiveRoomRegex();

    [GeneratedRegex(@"(?:https?://)?b23\.tv/[0-9A-Za-z]+", RegexOptions.IgnoreCase)]
    private static partial Regex B23UrlRegex();

    [GeneratedRegex(@"[?&]p=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex VideoPageRegex();

    [GeneratedRegex(@"(?:/bangumi/play/)?ep(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex BangumiEpRegex();

    [GeneratedRegex(@"(?:/bangumi/play/)?ss(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex BangumiSeasonRegex();

    [GeneratedRegex(@"(?:/bangumi/media/)?md(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex BangumiMediaRegex();
}

internal sealed record BilibiliBangumiIds(long? EpId, long? SeasonId, long? MediaId)
{
    public bool HasAny => EpId is not null || SeasonId is not null || MediaId is not null;
}
