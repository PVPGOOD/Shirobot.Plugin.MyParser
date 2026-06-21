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
private async Task SendGalleryMessageAsync(IncomingMessage message, DouyinParseResult result)
    {
        if (result.Images.Count == 0)
        {
            await _context.Message.ReplyAsync(message, FormatDouyinResult(result));
            return;
        }

        if (result.Images.Count == 1)
        {
            await SendSingleGalleryImageAsync(message, result, result.Images[0]);
            return;
        }

        var forwardedMessages = new List<OutgoingForwardedMessage>();
        var senderId = GetBotOrSenderId(message);
        var senderName = string.IsNullOrWhiteSpace(result.AuthorName) ? "抖音图文" : result.AuthorName!;

        var imageInputs = result.Images.Select((image, index) => (image, Index: index + 1)).ToArray();
        var imageFiles = await _hostServices.SelectParallelOrderedAsync(
            imageInputs,
            6,
            item => BuildRemoteImageAsync(item.image.Url, result.SourceUrl, $"douyin_image_{result.AwemeId}_{item.Index:D2}"));
        foreach (var imageFile in imageFiles)
        {
            if (string.IsNullOrWhiteSpace(imageFile.Uri))
            {
                continue;
            }

            var segments = new List<OutgoingSegment>
            {
                new ImageOutgoingSegment(imageFile.Uri),
            };
            forwardedMessages.Add(new OutgoingForwardedMessage(senderId, senderName, segments));
        }

        if (forwardedMessages.Count == 0)
        {
            await _context.Message.ReplyAsync(message, FormatDouyinResult(result));
            return;
        }

        var title = string.IsNullOrWhiteSpace(result.Title) ? "抖音图文" : TrimLine(result.Title, 48);
        var preview = result.Images.Take(4).Select((_, index) => $"图片 {index + 1}").ToArray();
        var summary = $"共 {result.Images.Count} 张";
        var forward = new ForwardOutgoingSegment(forwardedMessages, title, preview, summary, "抖音图文");
        var stopwatch = Stopwatch.StartNew();
        BotLog.Info($"MyParser 图文合并转发发送开始: aweme_id={result.AwemeId}, scene={GetMessageScene(message)}, images={forwardedMessages.Count}/{result.Images.Count}");

        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await _context.Message.SendGroupMessageAsync(group.Group.GroupId, forward);
                BotLog.Info($"MyParser 图文合并转发发送完成: aweme_id={result.AwemeId}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
            case FriendIncomingMessage friend:
            {
                var response = await _context.Message.SendPrivateMessageAsync(friend.SenderId, forward);
                BotLog.Info($"MyParser 图文合并转发发送完成: aweme_id={result.AwemeId}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
            default:
            {
                var response = await _context.Message.ReplyAsync(message, forward);
                BotLog.Info($"MyParser 图文合并转发发送完成: aweme_id={result.AwemeId}, scene=reply, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
        }
    }

    private async Task SendSingleGalleryImageAsync(IncomingMessage message, DouyinParseResult result, DouyinImageInfo image)
    {
        var imageFile = await BuildRemoteImageAsync(image.Url, result.SourceUrl, $"douyin_image_{result.AwemeId}_01");
        var segmentList = new List<OutgoingSegment>
        {
            new ImageOutgoingSegment(imageFile.Uri),
        };
        var segments = segmentList.ToArray();
        var stopwatch = Stopwatch.StartNew();
        BotLog.Info($"MyParser 单图图文 ImageSegment 发送开始: aweme_id={result.AwemeId}, scene={GetMessageScene(message)}, uri_preview={_hostServices.PreviewUri(imageFile.Uri)}");

        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await _context.Message.SendGroupMessageAsync(group.Group.GroupId, segments);
                BotLog.Info($"MyParser 单图图文 ImageSegment 发送完成: aweme_id={result.AwemeId}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
            case FriendIncomingMessage friend:
            {
                var response = await _context.Message.SendPrivateMessageAsync(friend.SenderId, segments);
                BotLog.Info($"MyParser 单图图文 ImageSegment 发送完成: aweme_id={result.AwemeId}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
            default:
            {
                var response = await _context.Message.ReplyAsync(message, segments);
                BotLog.Info($"MyParser 单图图文 ImageSegment 发送完成: aweme_id={result.AwemeId}, scene=reply, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                break;
            }
        }
    }

    private async Task SendMusicMessageAsync(IncomingMessage message, DouyinParseResult result)
    {
        if (string.IsNullOrWhiteSpace(result.MusicUrl))
        {
            return;
        }

        var segment = new RecordOutgoingSegment(result.MusicUrl);
        var stopwatch = Stopwatch.StartNew();
        BotLog.Info($"MyParser 图文音乐 RecordSegment 发送开始: aweme_id={result.AwemeId}, scene={GetMessageScene(message)}, uri_preview={_hostServices.PreviewUri(result.MusicUrl)}");

        try
        {
            switch (message)
            {
                case GroupIncomingMessage group:
                {
                    var response = await _context.Message.SendGroupMessageAsync(group.Group.GroupId, segment);
                    BotLog.Info($"MyParser 图文音乐 RecordSegment 发送完成: aweme_id={result.AwemeId}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                    break;
                }
                case FriendIncomingMessage friend:
                {
                    var response = await _context.Message.SendPrivateMessageAsync(friend.SenderId, segment);
                    BotLog.Info($"MyParser 图文音乐 RecordSegment 发送完成: aweme_id={result.AwemeId}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                    break;
                }
                default:
                {
                    var response = await _context.Message.ReplyAsync(message, segment);
                    BotLog.Info($"MyParser 图文音乐 RecordSegment 发送完成: aweme_id={result.AwemeId}, scene=reply, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 图文音乐 RecordSegment 发送失败，回退文本链接: aweme_id={result.AwemeId}, error={ex.Message}");
            await _context.Message.ReplyAsync(message, "音乐：" + result.MusicUrl);
        }
    }
}
