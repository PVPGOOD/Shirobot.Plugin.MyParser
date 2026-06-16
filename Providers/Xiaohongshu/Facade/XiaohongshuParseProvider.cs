using Shirobot.Plugin.MyParser.Parsing;
using Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Models;

namespace Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Facade;

internal sealed class XiaohongshuParseProvider(XiaohongshuParser parser) : IParseProvider, IDisposable
{
    public string Id => "xiaohongshu";
    public string Name => "小红书";
    public XiaohongshuParser Parser { get; } = parser;

    public bool CanHandle(string text) => XiaohongshuParser.ContainsXiaohongshuUrl(text);

    public async Task<MediaParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await Parser.ParseAsync(text, cancellationToken);
        return new MediaParseResult
        {
            ProviderId = Id,
            ProviderName = Name,
            MediaId = result.NoteId,
            SourceUrl = result.SourceUrl,
            Title = result.Title,
            AuthorName = result.AuthorName,
            AuthorId = result.AuthorId,
            CoverUrl = result.CoverUrl,
            Tags = result.Tags,
            IsGallery = result.IsGallery,
            IsVideo = result.IsVideo,
            ProviderPayload = result,
        };
    }

    public void Dispose()
    {
        Parser.Dispose();
    }
}
