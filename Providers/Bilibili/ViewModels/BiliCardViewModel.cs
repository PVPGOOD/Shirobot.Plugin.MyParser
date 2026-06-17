using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.ViewModels;

public sealed class BiliCardViewModel
{
    public BiliCardViewModel()
    {
        Cover = LoadDesignBackground();
        Avatar = LoadDesignAvatar();
        Title = "这是视频标题";
        Description = "这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述";
        AuthorName = "UP主名字";
        AuthorMeta = "UP主简介UP主简介";
        DurationText = "12:34";
        TagsText = "#标签1 #标签2 #标签3 #标签4 #标签5";
        LikeCount = "1.2K";
        CoinCount = "345";
        CollectCount = "678";
        ShareCount = "90";
    }
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
