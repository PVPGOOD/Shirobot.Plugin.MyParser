using Avalonia.Media.Imaging;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.ViewModels;

public sealed class BiliArticleCardViewModel
{
    public Bitmap? Cover { get; init; }
    public Bitmap? Avatar { get; init; }
    public string KindText { get; init; } = "Bilibili 专栏";
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string AuthorMeta { get; init; } = string.Empty;
    public string StatsText { get; init; } = string.Empty;
    public string TagsText { get; init; } = string.Empty;
    public string ImageCountText { get; init; } = string.Empty;
    public string PublishTimeText { get; init; } = string.Empty;
}
