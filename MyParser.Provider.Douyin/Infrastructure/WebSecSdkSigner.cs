using System.Security.Cryptography;
using System.Text;

namespace MyParser.Provider.Douyin.Infrastructure;

public static class WebSecSdkSigner
{
    private const string WebSignSalt = "A96D855A08C0A9707F8BEF0D9A527E4E";

    public static string SignUrl(string url, string? uifid, long? nowUnixSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(uifid))
        {
            return url;
        }

        var marker = url.Contains('?') ? '&' : '?';
        var unsignedUrl = RemoveExistingSignature(url);
        var query = ExtractQuery(unsignedUrl);
        var signSeconds = nowUnixSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = string.Join('_', uifid, signSeconds.ToString(), WebSignSalt, query);
        var signature = Md5LowerHex(payload);
        return unsignedUrl + marker + "x-secsdk-web-signature=" + signature;
    }

    public static string SignQuery(string query, string? uifid, long? nowUnixSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(uifid))
        {
            return query;
        }

        var unsignedQuery = RemoveExistingSignatureFromQuery(query);
        var signSeconds = nowUnixSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = string.Join('_', uifid, signSeconds.ToString(), WebSignSalt, unsignedQuery);
        return unsignedQuery + "&x-secsdk-web-signature=" + Md5LowerHex(payload);
    }

    private static string ExtractQuery(string url)
    {
        var question = url.IndexOf('?');
        if (question < 0 || question == url.Length - 1)
        {
            return string.Empty;
        }

        var fragment = url.IndexOf('#', question + 1);
        return fragment < 0 ? url[(question + 1)..] : url[(question + 1)..fragment];
    }

    private static string RemoveExistingSignature(string url)
    {
        var question = url.IndexOf('?');
        if (question < 0)
        {
            return url;
        }

        var prefix = url[..(question + 1)];
        var queryAndFragment = url[(question + 1)..];
        var hash = queryAndFragment.IndexOf('#');
        var fragment = hash < 0 ? string.Empty : queryAndFragment[hash..];
        var query = hash < 0 ? queryAndFragment : queryAndFragment[..hash];
        return prefix + RemoveExistingSignatureFromQuery(query) + fragment;
    }

    private static string RemoveExistingSignatureFromQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return query;
        }

        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("x-secsdk-web-signature=", StringComparison.OrdinalIgnoreCase));
        return string.Join('&', parts);
    }

    private static string Md5LowerHex(string value)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
