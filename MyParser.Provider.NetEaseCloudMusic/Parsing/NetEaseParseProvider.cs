using MyParser.Provider.NetEaseCloudMusic.Utilities;

namespace MyParser.Provider.NetEaseCloudMusic.Parsing;

public sealed class NetEaseParseProvider(NetEaseParser parser) : IParseProviderWithParser, IProviderLoginStatusProvider, IQrLoginProvider, IProviderPriority, IDisposable
{
    public NetEaseParser Parser { get; } = parser;
    public object ParserObject => Parser;
    public string Id => "neteasecloudmusic";
    public string Name => "网易云音乐";
    public int Priority => 30;

    public bool CanHandle(string text) => NetEaseUrlParser.ContainsNetEaseSongUrl(text);

    public Task<ProviderLoginStatus> CheckLoginStatusAsync(CancellationToken cancellationToken = default) => Parser.CheckLoginStatusAsync(cancellationToken);

    public async Task<QrLoginSession> GenerateQrLoginSessionAsync(CancellationToken cancellationToken = default)
    {
        var session = await Parser.GenerateQrLoginSessionAsync(cancellationToken).ConfigureAwait(false);
        return new QrLoginSession(session.Key, session.Url, session);
    }

    public async Task<QrLoginPollResult> PollQrLoginAsync(QrLoginSession session, CancellationToken cancellationToken = default)
    {
        var poll = await Parser.PollQrLoginAsync(session.Id, cancellationToken).ConfigureAwait(false);
        return new QrLoginPollResult(
            poll.Code,
            poll.Message,
            poll.IsLogin,
            IsExpired: poll.IsExpired,
            IsWaitingConfirmation: poll.IsWaitingConfirmation);
    }

    public async Task<MediaParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await Parser.ParseAsync(text, cancellationToken).ConfigureAwait(false);
        return new MediaParseResult
        {
            ProviderId = Id,
            ProviderName = Name,
            MediaId = result.SongId.ToString(),
            SourceUrl = result.SourceUrl,
            Title = result.Title,
            AuthorName = result.Artists,
            CoverUrl = result.CoverUrl,
            MusicUrl = result.AudioUrl,
            Tags = [result.Quality],
            IsGallery = false,
            IsVideo = false,
            ProviderPayload = result,
        };
    }

    public void Dispose() => Parser.Dispose();
}
