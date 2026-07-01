using System.Text.RegularExpressions;
using MyParser.Provider.WeixinChannels.Infrastructure;

namespace MyParser.Provider.WeixinChannels.Utilities;

internal static partial class WeixinChannelsUrlParser
{
    public static bool ContainsWeixinChannelsUrl(string text)
    {
        return TryExtractShareUrl(text, out _);
    }

    public static bool TryExtractShareUrl(string text, out string shareUrl)
    {
        shareUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = SphUrlRegex().Match(text);
        if (!match.Success)
        {
            return false;
        }

        shareUrl = NormalizeUrl(match.Value);
        return true;
    }

    public static string ExtractSphId(string shareUrl)
    {
        if (!Uri.TryCreate(shareUrl, UriKind.Absolute, out var uri))
        {
            return ProviderTextUtilities.SanitizeFileName(shareUrl, 32);
        }

        var segment = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrWhiteSpace(segment) ? ProviderTextUtilities.SanitizeFileName(shareUrl, 32) : segment;
    }

    private static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('，', '。', ',', '.', ')', '）', ']', '】', '>', '》');
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        return url;
    }

    [GeneratedRegex(@"(?:https?://)?weixin\.qq\.com/sph/[A-Za-z0-9_-]+", RegexOptions.IgnoreCase)]
    private static partial Regex SphUrlRegex();
}
