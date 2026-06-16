namespace Shirobot.Plugin.MyParser.Parsing;

internal sealed record MediaParseResult
{
    public required string ProviderId { get; init; }
    public required string ProviderName { get; init; }
    public required string MediaId { get; init; }
    public string? SourceUrl { get; init; }
    public string? Title { get; init; }
    public string? AuthorName { get; init; }
    public string? AuthorId { get; init; }
    public string? CoverUrl { get; init; }
    public string? MusicUrl { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public bool IsGallery { get; init; }
    public bool IsVideo { get; init; }

    public required object ProviderPayload { get; init; }
}
