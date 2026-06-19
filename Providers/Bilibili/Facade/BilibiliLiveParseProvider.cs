using Shirobot.Plugin.MyParser.Parsing;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Impl.Services;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Utilities;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Facade;

internal sealed class BilibiliLiveParseProvider(BilibiliLiveParser parser) : IParseProvider
{
    public BilibiliLiveParser Parser { get; } = parser;

    public string Id => "bilibili-live";
    public string Name => "Bilibili 直播";

    public bool CanHandle(string text)
    {
        return BilibiliUrlParser.ExtractStrictBilibiliUrl(text) is not null
               && (BilibiliUrlParser.ExtractLiveRoomId(text) is not null
                   || BilibiliUrlParser.ExtractB23Url(text) is not null);
    }

    public async Task<MediaParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await Parser.ParseAsync(text, cancellationToken);
        return new MediaParseResult
        {
            ProviderId = Id,
            ProviderName = Name,
            MediaId = result.RealRoomId,
            SourceUrl = result.SourceUrl,
            Title = result.Title,
            AuthorName = result.AnchorName,
            AuthorId = null,
            CoverUrl = result.CoverUrl,
            MusicUrl = null,
            Tags = [],
            IsGallery = false,
            IsVideo = false,
            ProviderPayload = result,
        };
    }
}
