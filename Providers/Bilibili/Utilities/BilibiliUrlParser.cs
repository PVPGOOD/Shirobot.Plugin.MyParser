using System.Text.RegularExpressions;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Utilities;

internal static partial class BilibiliUrlParser
{
    public static bool ContainsBilibiliUrl(string text)
    {
        return ExtractBvid(text) is not null || ExtractCvid(text) is not null || ExtractOpusId(text) is not null || ExtractLiveRoomId(text) is not null || ExtractB23Url(text) is not null;
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
        var bvid = ExtractBvid(text);
        if (bvid is not null)
        {
            return $"https://www.bilibili.com/video/{bvid}/";
        }

        return ExtractB23Url(text);
    }

    [GeneratedRegex("BV[0-9A-Za-z]{10}", RegexOptions.IgnoreCase)]
    private static partial Regex BvidRegex();

    [GeneratedRegex(@"(?:cv|/read/cv)(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CvidRegex();

    [GeneratedRegex(@"/opus/(\d+)|\bopus(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpusRegex();

    [GeneratedRegex(@"live\.bilibili\.com/(?:blanc/)?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LiveRoomRegex();

    [GeneratedRegex(@"(?:https?://)?b23\.tv/[0-9A-Za-z]+", RegexOptions.IgnoreCase)]
    private static partial Regex B23UrlRegex();
}
