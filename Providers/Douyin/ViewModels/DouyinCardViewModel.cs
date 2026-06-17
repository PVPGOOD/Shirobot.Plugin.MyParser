using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Shirobot.Plugin.MyParser.Providers.Douyin.ViewModels;

public sealed class DouyinCardViewModel
{
    public DouyinCardViewModel()
    {
        // 设计时数据
        Cover = LoadDesignBackground();
        Avatar = LoadDesignAvatar();
        CoverUri = "https://p3-sign.douyinpic.com/obj/eeaafc00000000000001020000000000_1678889600?x-expires=1678889600&x-signature=Z%2F%2F%2Bzqj%2Fh7n9lXoQyK8%2Fh5s%3D";
        AlbumId = "1234567890";
        Title = "这是视频标题";
        Description = "这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述";
        AuthorName = "UP主名字";
        AuthorMeta = "UP主简介UP主简介";
        DurationText = "12:34";
        PageText = "1/1";
        ViewCount = "1.2K";
        LikeCount = "345";
        CollectCount = "678";
        CommentCount = "90";
        ShareCount = "12";
        MusicText = "背景音乐：某某某 - 某某某";
        TagsText = "#标签1 #标签2 #标签3 #标签4 #标签5";
    }
    
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
    
    private static Bitmap? LoadDesignBackground([CallerFilePath] string sourceFilePath = "")
    {
        return LoadDesignBitmap("background.png", sourceFilePath);
    }

    private static Bitmap? LoadDesignAvatar([CallerFilePath] string sourceFilePath = "")
    {
        return LoadDesignBitmap("avatar.jpg", sourceFilePath);
    }

    private static Bitmap? LoadDesignBitmap(string fileName, string sourceFilePath)
    {
        if (!Design.IsDesignMode)
        {
            return null;
        }

        var viewModelsDir = Path.GetDirectoryName(sourceFilePath);
        if (string.IsNullOrWhiteSpace(viewModelsDir))
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(viewModelsDir, "..", "..", "..", "Assets", "Demo", fileName));
        return File.Exists(path) ? new Bitmap(path) : null;
    }
}
