using Shirobot.Plugin.MyParser.Parsing;

namespace MyParser.Provider.BiliBili.Parsing;

public sealed class BilibiliArticleParseProvider(BilibiliParser parser) : IParseProvider, IProviderPriority
{
    public BilibiliParser Parser { get; } = parser;

    public string Id => "bilibili-article";
    public string Name => "Bilibili 专栏";
    public int Priority => 20;

    public bool CanHandle(string text)
    {
        return Utilities.BilibiliUrlParser.ExtractStrictBilibiliUrl(text) is not null
               || Utilities.BilibiliUrlParser.ExtractCvid(text) is not null
               || Utilities.BilibiliUrlParser.ExtractOpusId(text) is not null
               || Utilities.BilibiliUrlParser.ExtractB23Url(text) is not null;
    }

    public async Task<MediaParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await Parser.ParseArticleAsync(text, cancellationToken);
        return new MediaParseResult
        {
            ProviderId = Id,
            ProviderName = Name,
            MediaId = result.IsOpus ? "opus" + result.OpusId : "cv" + result.Cvid,
            SourceUrl = result.SourceUrl,
            Title = result.Title,
            AuthorName = result.AuthorName,
            AuthorId = result.AuthorId,
            CoverUrl = result.BannerUrl,
            MusicUrl = null,
            Tags = result.Categories,
            IsGallery = false,
            IsVideo = false,
            ProviderPayload = result,
        };
    }
}
