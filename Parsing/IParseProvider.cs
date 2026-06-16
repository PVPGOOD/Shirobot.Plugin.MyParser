namespace Shirobot.Plugin.MyParser.Parsing;

internal interface IParseProvider
{
    string Id { get; }
    string Name { get; }
    bool CanHandle(string text);
    Task<MediaParseResult> ParseAsync(string text, CancellationToken cancellationToken = default);
}
