using Avalonia.Media.Imaging;

namespace MyParser.Provider.Xiaohongshu.Views;

public sealed class XiaohongshuCardViewModel
{
    public Bitmap? Cover { get; init; }
    public Bitmap? SecondImage { get; init; }
    public Bitmap? Avatar { get; init; }
    public string KindText { get; init; } = "小红书图文";
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string MetaText { get; init; } = string.Empty;
    public string StatsText { get; init; } = string.Empty;
    public string TagsText { get; init; } = string.Empty;
    public List<XiaohongshuCommentViewModel> Comments { get; init; } = [];
}

public sealed class XiaohongshuCommentViewModel
{
    public string Index { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
}
