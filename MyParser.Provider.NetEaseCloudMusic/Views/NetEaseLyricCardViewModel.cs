using Avalonia.Media.Imaging;

namespace MyParser.Provider.NetEaseCloudMusic.Views;

public sealed class NetEaseLyricCardViewModel
{
    public Bitmap? Cover { get; init; }
    public string Title { get; init; } = "歌曲标题";
    public string Artists { get; init; } = "歌手";
    public string Album { get; init; } = "专辑";
    public string LyricText { get; init; } = "暂无歌词";
    public double CardHeight { get; init; } = 420;
    public string SourceText { get; init; } = "网易云音乐 · 歌词";
}
