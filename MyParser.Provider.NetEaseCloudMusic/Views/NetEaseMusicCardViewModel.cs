using Avalonia.Media.Imaging;

namespace MyParser.Provider.NetEaseCloudMusic.Views;

public sealed class NetEaseMusicCardViewModel
{
    public Bitmap? Cover { get; init; }
    public string Title { get; init; } = "歌曲标题";
    public string Artists { get; init; } = "歌手";
    public string Album { get; init; } = "专辑";
    public string QualityText { get; init; } = "音质";
    public string SizeText { get; init; } = "大小";
    public string BitrateText { get; init; } = "码率";
    public string SongIdText { get; init; } = "ID";
    public string SourceText { get; init; } = "网易云音乐";
}
