using System.Diagnostics;
using System.Text;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Impl.Services;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Models;
using Shirobot.Plugin.MyParser.Utility;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;


namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Impl.MessageHandling.Impl;

internal sealed partial class BilibiliMessageHandler
{
private async Task TrySendLiveReplayClipAsync(IncomingMessage message, BilibiliLiveParseResult result)
    {
        if (!config.SendBilibiliLiveReplayClip)
        {
            return;
        }

        try
        {
            var clipSeconds = Math.Clamp(config.BilibiliLiveReplayClipSeconds, 1, 30);
            await ReplyAsync(message, $"正在从当前直播流可回溯分片中截取最近约 {clipSeconds} 秒，请稍候…");
            var downloader = new BilibiliLiveClipDownloader(config);
            var clip = await downloader.DownloadRecentClipAsync(result, progress => ReplyAsync(message,
                $"直播回溯分片已冻结：当前 m3u8 提供 {progress.SelectedSegments}/{progress.TotalSegments} 段，约 {progress.ActualSeconds:F0} 秒；正在用 ffmpeg 封装 MP4…"));
            await ReplyAsync(message, "直播回看片段已封装完成，正在发送到 QQ…");
            result.LocalClipPath = clip.LocalPath;
            result.LocalClipFileUri = clip.FileUri;
            var videoUri = BuildLocalVideoSegmentUri(clip.LocalPath, result);
            var segment = new VideoOutgoingSegment(videoUri, string.IsNullOrWhiteSpace(result.CoverUrl) ? null : result.CoverUrl);
            await SendLiveClipVideoMessageAsync(message, result, segment, clip.Stream);
            CleanupLocalLiveClipAfterSend(result);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser Bilibili 直播片段发送未完成: room_id={result.RealRoomId}, detail={ex.Message}");
            await ReplyAsync(message, "直播回看片段截取/发送未完成：" + ex.Message);
        }
    }

    private string BuildLocalVideoSegmentUri(string localPath, BilibiliLiveParseResult result)
    {
        var fileSize = new FileInfo(localPath).Length;
        var useBase64 = config.SendVideoSegmentAsBase64
                        && MemorySafetyUtilities.CanUseBase64ForFile(fileSize, config.VideoSegmentBase64MaxMegabytes);
        string videoUri;
        string uriMode;
        if (useBase64)
        {
            videoUri = "base64://" + Convert.ToBase64String(File.ReadAllBytes(localPath));
            uriMode = "base64";
        }
        else if (config.UseLocalHttpServerForLargeVideoSegment)
        {
            videoUri = GetLocalVideoHttpServer().RegisterFile(localPath);
            result.LocalClipRegisteredToHttpServer = true;
            uriMode = "http";
        }
        else
        {
            videoUri = new Uri(localPath).AbsoluteUri;
            uriMode = "file";
        }

        BotLog.Info($"MyParser Bilibili 直播片段 VideoSegment URI 模式：{uriMode}, room_id={result.RealRoomId}, file_mb={fileSize / 1024d / 1024d:F2}, uri_preview={MediaUriUtilities.PreviewUri(videoUri)}");
        return videoUri;
    }

    private async Task SendLiveClipVideoMessageAsync(IncomingMessage message, BilibiliLiveParseResult result, VideoOutgoingSegment videoSegment, BilibiliLiveStream stream)
    {
        var stopwatch = Stopwatch.StartNew();
        BotLog.Info($"MyParser Bilibili 直播片段 VideoSegment 发送开始: room_id={result.RealRoomId}, scene={GetMessageScene(message)}, stream={stream.Protocol}/{stream.Format}/{stream.Codec}, qn={stream.CurrentQn}, uri_mode={MediaUriUtilities.GetUriMode(videoSegment.Uri)}, uri_preview={MediaUriUtilities.PreviewUri(videoSegment.Uri)}");
        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await context.Message.SendGroupMessageAsync(group.Group.GroupId, videoSegment);
                BotLog.Info($"MyParser Bilibili 直播片段 VideoSegment 发送接口完成: room_id={result.RealRoomId}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureVideoSendAccepted(response.MessageSeq, "group");
                break;
            }
            case FriendIncomingMessage friend:
            {
                var response = await context.Message.SendPrivateMessageAsync(friend.SenderId, videoSegment);
                BotLog.Info($"MyParser Bilibili 直播片段 VideoSegment 发送接口完成: room_id={result.RealRoomId}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureVideoSendAccepted(response.MessageSeq, "friend");
                break;
            }
            default:
            {
                await context.Message.ReplyAsync(message, videoSegment);
                break;
            }
        }
    }

    private void CleanupLocalLiveClipAfterSend(BilibiliLiveParseResult result)
    {
        if (result.LocalClipRegisteredToHttpServer && config.DeleteLocalVideoDelaySeconds <= 0)
        {
            _localVideoHttpServer?.UnregisterFile(result.LocalClipPath);
            result.LocalClipRegisteredToHttpServer = false;
        }

        LocalMediaCleanup.DeleteLocalVideoIfConfigured(config, result.LocalClipPath, "bilibili");
    }

    private async Task SendLiveForwardAsync(IncomingMessage message, BilibiliLiveParseResult result)
    {
        var senderId = GetBotOrSenderId(message);
        var senderName = string.IsNullOrWhiteSpace(result.AnchorName) ? "Bilibili 直播" : result.AnchorName!;
        var forwarded = new List<OutgoingForwardedMessage>();
        var headerSegments = new List<OutgoingSegment>();

        if (!string.IsNullOrWhiteSpace(result.CoverUrl))
        {
            var cover = await BuildRemoteImageAsync(result.CoverUrl, result.SourceUrl, $"bilibili_live_cover_{result.RealRoomId}");
            if (!string.IsNullOrWhiteSpace(cover.Uri))
            {
                headerSegments.Add(new ImageOutgoingSegment(cover.Uri));
            }
        }

        headerSegments.Add(new TextOutgoingSegment(BuildLiveHeaderText(result)));
        forwarded.Add(new OutgoingForwardedMessage(senderId, senderName, headerSegments));

        if (result.Streams.Count > 0)
        {
            foreach (var (stream, index) in result.Streams.Select((stream, index) => (stream, index + 1)))
            {
                forwarded.Add(new OutgoingForwardedMessage(senderId, senderName,
                [
                    new TextOutgoingSegment(BuildLiveStreamText(stream, index))
                ]));
            }
        }
        else
        {
            forwarded.Add(new OutgoingForwardedMessage(senderId, senderName,
            [
                new TextOutgoingSegment("当前未返回可用直播流。")
            ]));
        }

        var title = string.IsNullOrWhiteSpace(result.Title) ? $"Bilibili 直播 {result.RealRoomId}" : TrimLine(result.Title!, 48);
        var preview = new[]
        {
            $"房间 {result.RealRoomId}",
            FormatLiveStatus(result.LiveStatus),
            result.Streams.Count > 0 ? $"播放流 {result.Streams.Count} 条" : "无播放流",
        };
        var summary = $"{FormatLiveStatus(result.LiveStatus)} · {result.Streams.Count} 条播放流";
        var forward = new ForwardOutgoingSegment(forwarded, title, preview, summary, "Bilibili 直播");

        switch (message)
        {
            case GroupIncomingMessage group:
                await context.Message.SendGroupMessageAsync(group.Group.GroupId, forward);
                break;
            case FriendIncomingMessage friend:
                await context.Message.SendPrivateMessageAsync(friend.SenderId, forward);
                break;
            default:
                await context.Message.ReplyAsync(message, forward);
                break;
        }
    }

    private static string BuildLiveHeaderText(BilibiliLiveParseResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bilibili 直播解析成功");
        sb.AppendLine($"房间：{result.RealRoomId}");
        sb.AppendLine($"状态：{FormatLiveStatus(result.LiveStatus)}");
        if (!string.IsNullOrWhiteSpace(result.Title)) sb.AppendLine($"标题：{TrimLine(result.Title, 120)}");
        if (!string.IsNullOrWhiteSpace(result.AnchorName)) sb.AppendLine($"主播：{result.AnchorName}");
        if (!string.IsNullOrWhiteSpace(result.RoomAudienceText)) sb.AppendLine($"房间观众：{result.RoomAudienceText}");
        else if (result.RoomAudienceCount > 0) sb.AppendLine($"房间观众：{FormatCount(result.RoomAudienceCount)}");
        if (!string.IsNullOrWhiteSpace(result.WatchedText)) sb.AppendLine($"人气/看过：{result.WatchedText}");
        else if (result.WatchedCount > 0) sb.AppendLine($"人气/看过：{FormatCount(result.WatchedCount)}");
        else if (result.OnlineCount > 0) sb.AppendLine($"人气值：{FormatCount(result.OnlineCount)}");
        if (result.LiveStartTime is not null) sb.AppendLine($"开播时间：{result.LiveStartTime:yyyy-MM-dd HH:mm:ss}");
        if (result.LiveDuration is not null) sb.AppendLine($"直播时长：{FormatDuration(result.LiveDuration.Value)}");
        if (!string.IsNullOrWhiteSpace(result.CoverUrl)) sb.AppendLine($"封面：{result.CoverUrl}");
        if (!string.IsNullOrWhiteSpace(result.SourceUrl)) sb.AppendLine($"链接：{result.SourceUrl}");
        sb.AppendLine($"播放流：{result.Streams.Count} 条，见后续转发节点。");
        AppendRecommendedLiveStreams(sb, result);
        return sb.ToString().TrimEnd();
    }

    private static void AppendRecommendedLiveStreams(StringBuilder sb, BilibiliLiveParseResult result)
    {
        if (result.Streams.Count == 0)
        {
            return;
        }

        var bestQuality = result.Streams
            .OrderByDescending(i => i.CurrentQn)
            .ThenByDescending(StreamCodecQualityRank)
            .ThenBy(StreamCompatibilityRank)
            .ThenBy(i => i.CdnIndex)
            .Take(2)
            .ToList();
        var bestCompatibility = result.Streams
            .OrderBy(StreamCompatibilityRank)
            .ThenByDescending(i => i.CurrentQn)
            .ThenBy(i => i.CdnIndex)
            .FirstOrDefault();

        sb.AppendLine("推荐播放流：");
        foreach (var stream in bestQuality)
        {
            sb.AppendLine($"- 画质优先：{FormatLiveStreamSummary(stream)}");
        }

        if (bestCompatibility is not null && !bestQuality.Contains(bestCompatibility))
        {
            sb.AppendLine($"- 兼容优先：{FormatLiveStreamSummary(bestCompatibility)}");
        }
        else if (bestCompatibility is not null)
        {
            sb.AppendLine($"- 兼容优先：同上 {FormatLiveStreamSummary(bestCompatibility)}");
        }
    }

    private static string FormatLiveStreamSummary(BilibiliLiveStream stream)
    {
        var accept = stream.AcceptQn.Count > 0 ? $", 可选 qn={string.Join('/', stream.AcceptQn)}" : string.Empty;
        return $"{stream.Protocol}/{stream.Format}/{stream.Codec} qn={stream.CurrentQn} CDN#{stream.CdnIndex}{accept}";
    }

    private static int StreamCompatibilityRank(BilibiliLiveStream stream)
    {
        return (stream.Protocol, stream.Format, stream.Codec) switch
        {
            ("http_hls", "ts", "avc") => 0,
            ("http_hls", "fmp4", "avc") => 1,
            ("http_stream", "flv", "avc") => 2,
            ("http_hls", "ts", "hevc") => 3,
            ("http_hls", "fmp4", "hevc") => 4,
            ("http_stream", "flv", "hevc") => 5,
            ("http_hls", "fmp4", "av1") => 6,
            _ => 99,
        };
    }

    private static int StreamCodecQualityRank(BilibiliLiveStream stream)
    {
        return stream.Codec.ToLowerInvariant() switch
        {
            "av1" => 3,
            "hevc" => 2,
            "avc" => 1,
            _ => 0,
        };
    }

    private static string BuildLiveStreamText(BilibiliLiveStream stream, int index)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#{index} {stream.Protocol}/{stream.Format}/{stream.Codec} qn={stream.CurrentQn} CDN#{stream.CdnIndex}");
        if (stream.AcceptQn.Count > 0)
        {
            sb.AppendLine("可选 qn：" + string.Join(", ", stream.AcceptQn));
        }

        sb.AppendLine(stream.Url);
        return sb.ToString().TrimEnd();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays}天 {duration:hh\\:mm\\:ss}"
            : duration.ToString(@"hh\:mm\:ss");
    }
}
