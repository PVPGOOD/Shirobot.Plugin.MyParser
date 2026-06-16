using Shirobot.Plugin.MyParser.Providers.Douyin.Models;
using Shirobot.Plugin.MyParser.Providers.Douyin.Abstractions;
using Shirobot.Plugin.MyParser.Providers.Douyin.Impl.Services;
using Shirobot.Plugin.MyParser.Providers.Douyin.Impl.WorkParsers;
using Shirobot.Plugin.MyParser.Providers.Douyin.Infrastructure;
using Shirobot.Plugin.MyParser.Providers.Douyin.Utilities;

namespace Shirobot.Plugin.MyParser.Providers.Douyin.Facade;

internal sealed class DouyinParser : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly MyParserConfig _config;
    private readonly DouyinParseService _parseService;
    private readonly DouyinVideoDownloader _videoDownloader;
    private readonly DouyinLoginStatusChecker _loginStatusChecker;

    public DouyinParser(MyParserConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? DouyinHttpClientFactory.Create(config);

        IDouyinWorkParser[] workParsers =
        [
            new DouyinLiveWorkParser(),
            new DouyinGalleryWorkParser(),
            new DouyinVideoWorkParser(config),
        ];

        _parseService = new DouyinParseService(config, _http, workParsers);
        _videoDownloader = new DouyinVideoDownloader(config, _http);
        _loginStatusChecker = new DouyinLoginStatusChecker(config, _http);
    }

    public void SetCookieIfEmpty(string? cookie)
    {
        if (!string.IsNullOrWhiteSpace(_config.DouyinCookie) || string.IsNullOrWhiteSpace(cookie))
        {
            return;
        }

        _config.DouyinCookie = cookie.Trim();
    }

    public Task<string> CheckLoginStatusAsync(CancellationToken cancellationToken = default)
    {
        return _loginStatusChecker.CheckLoginStatusAsync(cancellationToken);
    }

    public Task<DouyinParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        return _parseService.ParseAsync(text, cancellationToken);
    }

    public Task<(string FileUri, string LocalPath)> DownloadVideoAsync(DouyinParseResult result, CancellationToken cancellationToken = default)
    {
        return _videoDownloader.DownloadVideoAsync(result, cancellationToken);
    }

    public static bool ContainsDouyinUrl(string text) => DouyinUrlParser.ContainsDouyinUrl(text);

    public static string? ExtractDouyinUrl(string text) => DouyinUrlParser.ExtractDouyinUrl(text);

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
