using Shirobot.Plugin.MyParser.Providers.Douyin.Models;
using Shirobot.Plugin.MyParser.Parsing;
using Shirobot.Plugin.MyParser.Providers.Douyin.Facade;
namespace Shirobot.Plugin.MyParser.Providers.Douyin.Facade;

internal sealed class DouyinParseProvider(DouyinParser parser) : IParseProvider, IDisposable
{
    public DouyinParser Parser { get; } = parser;

    public string Id => "douyin";
    public string Name => "抖音";

    public bool CanHandle(string text) => DouyinParser.ContainsDouyinUrl(text);

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
