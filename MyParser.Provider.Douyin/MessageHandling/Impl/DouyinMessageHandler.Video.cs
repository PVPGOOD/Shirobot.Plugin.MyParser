using System.Diagnostics;
using System.Net;
using System.Text;
using MyParser.Provider.Douyin.Infrastructure;
using MyParser.Provider.Douyin.Models;
using MyParser.Provider.Douyin.Views;
using ShiroBot.AvaloniaSdk;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;
using Shirobot.Plugin.MyParser.Parsing;
using static MyParser.Provider.Douyin.Infrastructure.DouyinRequestHeaders;

namespace MyParser.Provider.Douyin.MessageHandling;

internal sealed partial class DouyinMessageHandler
{
private async Task<VideoOutgoingSegment?> BuildVideoSegmentAsync(DouyinParseResult result)
    {
        if (!_config.SendVideoSegment || _douyinProvider is null || result.IsGallery || !result.IsVideo)
        {
            return null;
        }

        var (fileUri, localPath) = await DownloadVideoAsync(result);
        result.LocalVideoFileUri = fileUri;
        result.LocalVideoPath = localPath;
        LogFinalVideoFileInfo(result);

        var fileSize = new FileInfo(localPath).Length;
        string videoUri;
        string uriMode;
        if (_config.FileProtocol == VideoSegmentFileProtocol.Base64)
        {
            videoUri = "base64://" + Convert.ToBase64String(await File.ReadAllBytesAsync(localPath));
            uriMode = "base64";
        }
        else if (_config.FileProtocol == VideoSegmentFileProtocol.Http)
        {
            videoUri = _hostServices.RegisterLocalVideoFile(localPath);
            result.LocalVideoRegisteredToHttpServer = true;
            uriMode = "http";
        }
        else
        {
            videoUri = fileUri;
            uriMode = "file";
        }

        BotLog.Info($"MyParser VideoSegment URI 模式：{uriMode}, file_mb={fileSize / 1024d / 1024d:F2}, uri_preview={_hostServices.PreviewUri(videoUri)}");
        var thumbUri = !string.IsNullOrWhiteSpace(result.CoverUrl)
            ? result.CoverUrl
            : null;
        return new VideoOutgoingSegment(videoUri, thumbUri);
    }

    private Task<(string FileUri, string LocalPath)> DownloadVideoAsync(DouyinParseResult result)
    {
        var quality = result.Qualities.FirstOrDefault();
        if (quality is null || string.IsNullOrWhiteSpace(result.VideoUrl))
        {
            throw new DouyinParseException("没有可下载的视频地址。");
        }

        var request = new ProviderVideoDownloadRequest(
            "douyin",
            "抖音",
            result.AwemeId,
            $"douyin:{result.AwemeId}:{quality.GearName}:{quality.Ratio}:{quality.Codec}",
            BuildVideoDownloadCandidates(result, quality).ToArray(),
            MyParserRuntime.DownloadDirectory,
            "douyin",
            "mp4",
            (method, url, range) => CreateVideoRequest(method, url, result, range),
            ProviderVideoValidationKind.Mp4,
            "aweme_id");

        return _hostServices.DownloadProviderVideoAsync(_config, request);
    }

    private static IEnumerable<string> BuildVideoDownloadCandidates(DouyinParseResult result, DouyinVideoQuality quality)
    {
        yield return quality.Url;

        if (string.IsNullOrWhiteSpace(quality.Uri))
        {
            yield break;
        }

        var firstRatio = quality.Ratio switch
        {
            "2k" => quality.Height >= 2160 || quality.Width >= 3840 ? "2160p" : "1440p",
            _ => quality.Ratio,
        };
        var ratios = new[] { firstRatio, "1440p", "1080p", "720p", "540p", "480p" }
            .Where(i => !string.IsNullOrWhiteSpace(i) && i != "默认")
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var ratio in ratios)
        {
            yield return $"https://aweme.snssdk.com/aweme/v1/play/?video_id={Uri.EscapeDataString(quality.Uri!)}&ratio={Uri.EscapeDataString(ratio)}&line=0";
        }
    }

    private static HttpRequestMessage CreateVideoRequest(HttpMethod method, string url, DouyinParseResult result, string? range)
    {
        var request = new HttpRequestMessage(method, url);
        ApplyDefaultHeaders(request, result.SourceUrl ?? DouyinConstants.HomeUrl);
        if (!string.IsNullOrWhiteSpace(range))
        {
            request.Headers.TryAddWithoutValidation("Range", range);
        }

        if (!string.IsNullOrWhiteSpace(MyParserRuntime.DouyinCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", MyParserRuntime.DouyinCookie);
        }

        return request;
    }

    private void LogFinalVideoFileInfo(DouyinParseResult result)
    {
        if (!_config.LogSelectedQualityInfo)
        {
            return;
        }

        var selected = result.Qualities.FirstOrDefault();
        var fileSize = !string.IsNullOrWhiteSpace(result.LocalVideoPath) && File.Exists(result.LocalVideoPath)
            ? new FileInfo(result.LocalVideoPath).Length
            : 0;
        BotLog.Info(
            "MyParser 最终发送视频信息: "
            + $"aweme_id={result.AwemeId}, "
            + $"quality={(selected?.Label ?? "unknown")}, "
            + $"ratio={(selected?.Ratio ?? "unknown")}, "
            + $"fps={(selected?.Fps ?? 0)}, "
            + $"bitrate_kbps={(selected is { BitRate: > 0 } ? selected.BitRate / 1000 : 0)}, "
            + $"size={(selected is null ? "0x0" : $"{selected.Width}x{selected.Height}")}, "
            + $"codec={(string.IsNullOrWhiteSpace(selected?.Codec) ? "unknown" : selected.Codec)}, "
            + $"file_mb={(fileSize > 0 ? fileSize / 1024d / 1024d : 0):F2}, "
            + $"path={result.LocalVideoPath}");
    }

    private Task StartSendCoverMessageAsync(IncomingMessage message, DouyinParseResult result)
    {
        if (string.IsNullOrWhiteSpace(result.CoverUrl))
        {
            return Task.CompletedTask;
        }

        return _hostServices.RunLoggedBackgroundAsync($"抖音封面卡片异步发送: aweme_id={result.AwemeId}", () => SendCoverMessageAsync(message, result));
    }

    private async Task SendVideoMessageAsync(IncomingMessage message, DouyinParseResult result, VideoOutgoingSegment videoSegment)
    {
        var segments = new OutgoingSegment[] { videoSegment };
        var stopwatch = Stopwatch.StartNew();
        var uriMode = _hostServices.GetUriMode(videoSegment.Uri);
        BotLog.Info($"MyParser VideoSegment 发送开始: aweme_id={result.AwemeId}, scene={GetMessageScene(message)}, uri_mode={uriMode}, segments={segments.Length}, uri_preview={_hostServices.PreviewUri(videoSegment.Uri)}");

        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await _context.Message.SendGroupMessageAsync(group.Group.GroupId, segments);
                BotLog.Info($"MyParser VideoSegment 发送接口完成: aweme_id={result.AwemeId}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureVideoSendAccepted(response.MessageSeq, "group");
                break;
            }
            case FriendIncomingMessage friend:
            {
                var response = await _context.Message.SendPrivateMessageAsync(friend.SenderId, segments);
                BotLog.Info($"MyParser VideoSegment 发送接口完成: aweme_id={result.AwemeId}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureVideoSendAccepted(response.MessageSeq, "friend");
                break;
            }
            default:
            {
                var response = await _context.Message.ReplyAsync(message, segments);
                BotLog.Info($"MyParser VideoSegment 发送接口完成: aweme_id={result.AwemeId}, scene=reply, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureVideoSendAccepted(response.MessageSeq, "reply");
                break;
            }
        }
    }

    private void EnsureVideoSendAccepted(long messageSeq, string scene)
    {
        if (messageSeq <= 0)
        {
            BotLog.Warning($"MyParser VideoSegment 发送返回 message_seq={messageSeq}，当前 ShiroBot/适配器可能不返回有效消息序号；不再按失败处理。scene={scene}");
        }
    }
}
