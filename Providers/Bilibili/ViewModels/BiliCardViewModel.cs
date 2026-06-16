using Avalonia.Media.Imaging;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.ViewModels;

public sealed class BiliCardViewModel
{
    public Bitmap? Cover { get; init; }
    public Bitmap? Avatar { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string AuthorMeta { get; init; } = string.Empty;
    public string DurationText { get; init; } = string.Empty;
    public string TagsText { get; init; } = string.Empty;
    public string LikeCount { get; init; } = string.Empty;
    public string CoinCount { get; init; } = string.Empty;
    public string CollectCount { get; init; } = string.Empty;
    public string ShareCount { get; init; } = string.Empty;
}
