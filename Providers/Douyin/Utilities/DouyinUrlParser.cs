using System.Text.RegularExpressions;

namespace Shirobot.Plugin.MyParser.Providers.Douyin.Utilities;

internal static partial class DouyinUrlParser
{
    public static bool ContainsDouyinUrl(string text) => ExtractDouyinUrl(text) is not null;

    public static string? ExtractDouyinUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in UrlRegex().Matches(text))
        {
            var url = match.Value.Trim().TrimEnd('，', '。', '、', ',', '.', ';', '；', ')', '）', ']', '】', '>', '》', '"', '\'');
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsDouyinHost(uri.Host))
            {
                return uri.ToString();
            }
        }

        return null;
    }

    public static string? ExtractAwemeId(string input)
    {
        foreach (var pattern in AwemeIdPatterns())
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success && match.Groups[1].Value.Length >= 15)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    public static Uri MakeAbsolute(Uri location, Uri baseUri) => location.IsAbsoluteUri ? location : new Uri(baseUri, location);

    private static bool IsDouyinHost(string host) => host.EndsWith("douyin.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("iesdouyin.com", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("(?:(?:https?://)?(?:v\\.)?douyin\\.com/[^\\s<>\"']+|(?:https?://)?(?:www\\.)?iesdouyin\\.com/[^\\s<>\"']+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    private static string[] AwemeIdPatterns() =>
    [
        @"/video/(\d{15,25})",
        @"/note/(\d{15,25})",
        @"/aweme/detail/(\d{15,25})",
        @"/share/video/(\d{15,25})",
        @"modal_id=(\d{15,25})",
        "aweme_id[=\\\"':]+(\\d{15,25})",
        "itemId[\\\"':]+(\\d{15,25})",
        "note_id[=\\\"':]+(\\d{15,25})",
    ];
}
