namespace Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Models;

internal sealed record XiaohongshuParseResult
{
    public required string NoteId { get; init; }
    public string? SourceUrl { get; init; }
    public string? OriginalUrl { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? AuthorName { get; init; }
    public string? AuthorId { get; init; }
    public string? AuthorAvatarUrl { get; init; }
    public string? CoverUrl { get; init; }
    public string? XsecToken { get; init; }
    public string XsecSource { get; init; } = "pc_feed";
    public long LikeCount { get; init; }
    public long CollectCount { get; init; }
    public long CommentCount { get; init; }
    public long ShareCount { get; init; }
    public double? DurationSeconds { get; init; }
    public List<string> Tags { get; init; } = [];
    public List<XiaohongshuImageInfo> Images { get; init; } = [];
    public List<XiaohongshuVideoFormat> VideoFormats { get; init; } = [];
    public List<XiaohongshuComment> Comments { get; init; } = [];
    public string? LocalVideoPath { get; set; }
    public string? LocalVideoFileUri { get; set; }
    public bool LocalVideoRegisteredToHttpServer { get; set; }
    public bool IsVideo => VideoFormats.Count > 0;
    public bool IsGallery => Images.Count > 0 && !IsVideo;
    public XiaohongshuVideoFormat? SelectedVideo => VideoFormats.FirstOrDefault();
}

internal sealed record XiaohongshuImageInfo
{
    public required string Url { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

internal sealed record XiaohongshuVideoFormat
{
    public required string Url { get; init; }
    public string FormatId { get; init; } = "default";
    public string Ext { get; init; } = "mp4";
    public int Width { get; init; }
    public int Height { get; init; }
    public double Fps { get; init; }
    public double BitrateKbps { get; init; }
    public double? DurationSeconds { get; init; }
    public List<string> Urls { get; init; } = [];
}

internal sealed record XiaohongshuComment
{
    public string? Id { get; init; }
    public string Content { get; init; } = string.Empty;
    public long LikeCount { get; init; }
    public string? IpLocation { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public XiaohongshuUser User { get; init; } = new();
    public List<XiaohongshuComment> SubComments { get; init; } = [];
}

internal sealed record XiaohongshuUser
{
    public string? Id { get; init; }
    public string Nickname { get; init; } = "匿名用户";
    public string? Avatar { get; init; }
}

internal sealed record XiaohongshuLoginStatus(bool IsLogin, string? UserName, string? UserId, string Message, bool NeedVerify = false);

internal sealed record XiaohongshuQrLoginSession(string QrId, string Code, string Url, string Cookie, DateTimeOffset CreatedAt);

internal sealed record XiaohongshuQrPollResult(int CodeStatus, string Message, bool IsLogin, bool NeedVerify, string Cookie, string? UserName = null);

internal class XiaohongshuParseException(string message) : Exception(message);

internal sealed class XiaohongshuLoginRequiredException(string message) : XiaohongshuParseException(message);

internal sealed class XiaohongshuSignRequiredException(string message) : XiaohongshuParseException(message);
