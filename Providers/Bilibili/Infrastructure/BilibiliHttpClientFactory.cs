using System.Net;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Infrastructure;

internal static class BilibiliHttpClientFactory
{
    public static HttpClientHandler CreateHandler()
    {
        return new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
    }

    public static TimeSpan GetTimeout(MyParserConfig config)
    {
        var timeout = Math.Clamp(config.RequestTimeoutSeconds, 5, 120);
        return TimeSpan.FromSeconds(timeout);
    }

    public static HttpClient Create(MyParserConfig config)
    {
        return new HttpClient(CreateHandler()) { Timeout = GetTimeout(config) };
    }
}
