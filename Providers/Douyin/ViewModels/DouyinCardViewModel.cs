using Avalonia.Media.Imaging;

namespace Shirobot.Plugin.MyParser.Providers.Douyin.ViewModels;

public sealed class DouyinCardViewModel
{
    public Bitmap? Cover { get; init; }
    public Bitmap? Avatar { get; init; }
    public string CoverUri { get; init; } = string.Empty;
    public string AlbumId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string AuthorMeta { get; init; } = string.Empty;
    public string DurationText { get; init; } = string.Empty;
    public string PageText { get; init; } = string.Empty;
    public string ViewCount { get; init; } = string.Empty;
    public string LikeCount { get; init; } = string.Empty;
    public string CollectCount { get; init; } = string.Empty;
    public string CommentCount { get; init; } = string.Empty;
    public string ShareCount { get; init; } = string.Empty;
    public string MusicText { get; init; } = string.Empty;
    public string TagsText { get; init; } = string.Empty;
}
