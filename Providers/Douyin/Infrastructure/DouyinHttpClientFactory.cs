using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Shirobot.Plugin.MyParser.Providers.Douyin.Infrastructure;

internal static class DouyinHttpClientFactory
{
    public static HttpClient Create(MyParserConfig config)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        handler.CookieContainer.Add(new Cookie("ttwid", GenerateTtwidFallback(), "/", ".douyin.com"));

        var timeout = Math.Clamp(config.RequestTimeoutSeconds, 5, 60);
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeout) };
    }

    private static string GenerateTtwidFallback()
    {
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"1%7C{random}%7C{ts}%7C{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(random + ts))).ToLowerInvariant()}";
    }
}
