using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace MyParser.Provider.BiliBili.Views;

public sealed class BiliCardViewModel
{
    public string Title { get; init; } = "这是视频标题";
    public string Description { get; init; } = "这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述这是视频描述";
    public string AuthorName { get; init; } = "UP主名字";
    public string AuthorMeta { get; init; } = "UP主简介UP主简介";
    public string DurationText { get; init; } = "12:34";
    public string TagsText { get; init; } = "#标签1 #标签2 #标签3 #标签4 #标签5";
    public string LikeCount { get; init; } = "1.2K";
    public string CoinCount { get; init; } = "345";
    public string CollectCount { get; init; } = "678";
    public string ShareCount { get; init; } = "90";
    public Bitmap? Cover { get; init; } = LoadDesignBackground();
    public Bitmap? Avatar { get; init; } = LoadDesignAvatar();

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

        foreach (var path in EnumerateDesignAssetCandidates(viewModelsDir, fileName))
        {
            if (File.Exists(path))
            {
                return new Bitmap(path);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateDesignAssetCandidates(string startDirectory, string fileName)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            yield return Path.Combine(directory.FullName, "Assets", "Demo", fileName);
            yield return Path.Combine(directory.FullName, "MyParser", "Assets", "Demo", fileName);
        }
    }
}
