namespace Shirobot.Plugin.MyParser.Parsing;

internal sealed class ParseProviderRegistry(IEnumerable<IParseProvider> providers)
{
    private readonly IReadOnlyList<IParseProvider> _providers = providers.ToArray();

    public IParseProvider? FindProvider(string text)
    {
        return _providers.FirstOrDefault(provider => provider.CanHandle(text));
    }

    public async Task<MediaParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(text)
            ?? throw new InvalidOperationException("未找到可处理该链接的解析提供商。");
        return await provider.ParseAsync(text, cancellationToken);
    }
}
