using MyParser.Provider.Douyin.Models;
using Shirobot.Plugin.MyParser.Parsing;
using MyParser.Provider.Douyin.Parsing;
namespace MyParser.Provider.Douyin.Parsing;

public sealed class DouyinParseProvider(DouyinParser parser) : IParseProviderWithParser, IProviderLoginStatusProvider, IProviderPriority, IDisposable
{
    public DouyinParser Parser { get; } = parser;
    public object ParserObject => Parser;

    public string Id => "douyin";
    public string Name => "抖音";
    public int Priority => 10;

    public bool CanHandle(string text) => DouyinParser.ContainsDouyinUrl(text);

    public async Task<ProviderLoginStatus> CheckLoginStatusAsync(CancellationToken cancellationToken = default)
    {
        var message = await Parser.CheckLoginStatusAsync(cancellationToken);
        var isLogin = message.Contains("有效/已登录", StringComparison.OrdinalIgnoreCase);
        return new ProviderLoginStatus(isLogin, null, null, message);
    }

    public async Task<MediaParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await Parser.ParseAsync(text, cancellationToken);
        return new MediaParseResult
        {
            ProviderId = Id,
            ProviderName = Name,
            MediaId = result.AwemeId,
            SourceUrl = result.SourceUrl,
            Title = result.Title,
            AuthorName = result.AuthorName,
            AuthorId = result.AuthorId,
            CoverUrl = result.CoverUrl,
            MusicUrl = result.MusicUrl,
            Tags = result.Tags,
            IsGallery = result.IsGallery,
            IsVideo = result.IsVideo,
            ProviderPayload = result,
        };
    }

    public void Dispose() => Parser.Dispose();
}
