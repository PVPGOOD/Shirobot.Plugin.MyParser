using ShiroBot.Model.Common;

namespace Shirobot.Plugin.MyParser.Parsing;

internal sealed class ParseProviderRegistry(IEnumerable<IParseProvider> providers)
{
    private readonly IReadOnlyList<IParseProvider> _providers = providers.ToArray();

    public IParseProvider? FindProvider(string text)
    {
        return _providers.FirstOrDefault(provider => provider.CanHandle(text));
    }

    public IParseProvider? FindProvider(IncomingMessage message, out string parseText)
    {
        var plainText = GetPlainText(message);
        foreach (var provider in _providers)
        {
            var candidate = provider is IIncomingMessageParseProvider incomingProvider
                ? incomingProvider.ExtractParseText(message)
                : plainText;
            if (string.IsNullOrWhiteSpace(candidate) || !provider.CanHandle(candidate))
            {
                continue;
            }

            parseText = candidate;
            return provider;
        }

        parseText = plainText;
        return null;
    }

    public async Task<MediaParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(text)
            ?? throw new InvalidOperationException("未找到可处理该链接的解析提供商。");
        return await provider.ParseAsync(text, cancellationToken);
    }

    private static string GetPlainText(IncomingMessage message)
    {
        var segments = message switch
        {
            FriendIncomingMessage friend => friend.Segments,
            GroupIncomingMessage group => group.Segments,
            TempIncomingMessage temp => temp.Segments,
            _ => [],
        };
        return string.Concat(segments.OfType<TextIncomingSegment>().Select(i => i.Text));
    }
}
