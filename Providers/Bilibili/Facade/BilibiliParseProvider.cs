using Shirobot.Plugin.MyParser.Parsing;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Models;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Facade;

internal sealed class BilibiliParseProvider(BilibiliParser parser) : IParseProvider, IDisposable
{
    public BilibiliParser Parser { get; } = parser;

    public string Id => "bilibili";
    public string Name => "Bilibili";

    public bool CanHandle(string text)
    {
        return Utilities.BilibiliUrlParser.ExtractBvid(text) is not null
               || Utilities.BilibiliUrlParser.ExtractB23Url(text) is not null;
    }

    public async Task<MediaParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await Parser.ParseAsync(text, cancellationToken);
        return new MediaParseResult
        {
            ProviderId = Id,
            ProviderName = Name,
            MediaId = result.Bvid,
            SourceUrl = result.SourceUrl,
            Title = result.Title,
            AuthorName = result.AuthorName,
            AuthorId = result.AuthorId,
            CoverUrl = result.CoverUrl,
            MusicUrl = null,
            Tags = [],
            IsGallery = false,
            IsVideo = result.IsVideo,
            ProviderPayload = result,
        };
    }

    public void Dispose() => Parser.Dispose();
}
