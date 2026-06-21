using System.Net;

namespace MyParser.Provider.Xiaohongshu.Infrastructure;

public static class XiaohongshuHttpClientFactory
{
    public static HttpClient Create(PluginConfig config)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };

        var timeout = Math.Clamp(config.RequestTimeoutSeconds, 5, 60);
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeout) };
    }
}
