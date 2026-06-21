using System.Net;

namespace MyParser.Provider.BiliBili.Infrastructure;

public static class BilibiliHttpClientFactory
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

    public static TimeSpan GetTimeout(PluginConfig config)
    {
        var timeout = Math.Clamp(config.RequestTimeoutSeconds, 5, 120);
        return TimeSpan.FromSeconds(timeout);
    }

    public static HttpClient Create(PluginConfig config)
    {
        return new HttpClient(CreateHandler()) { Timeout = GetTimeout(config) };
    }
}
