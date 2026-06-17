using System.Diagnostics;
using Shirobot.Plugin.MyParser.Utility;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.MyParser.Providers.Common.MessageHandling;

internal static class MessageHandlerCommon
{
    public static async Task ReactAsync(IBotContext context, IncomingMessage message, string faceId, string platformName)
    {
        if (message is not GroupIncomingMessage group)
        {
            return;
        }

        try
        {
            await context.Group.SendGroupMessageReactionAsync(group.Group.GroupId, group.MessageSeq, faceId);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser {platformName} 消息贴表情失败: group_id={group.Group.GroupId}, message_seq={group.MessageSeq}, face={faceId}, error={ex.Message}");
        }
    }

    public static Task<SendMessageResult> ReplyTextAsync(IBotContext context, MyParserConfig config, IncomingMessage message, string text)
    {
        return config.QuoteReply ? context.Message.QuoteReplyAsync(message, text) : context.Message.ReplyAsync(message, text);
    }

    public static async Task SendImageAsync(IBotContext context, IncomingMessage message, ImageOutgoingSegment segment)
    {
        switch (message)
        {
            case GroupIncomingMessage group:
                await context.Message.SendGroupMessageAsync(group.Group.GroupId, segment);
                break;
            case FriendIncomingMessage friend:
                await context.Message.SendPrivateMessageAsync(friend.SenderId, segment);
                break;
            default:
                await context.Message.ReplyAsync(message, segment);
                break;
        }
    }

    public static Task RunLoggedBackgroundAsync(string description, Func<Task> action)
    {
        return Task.Run(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                BotLog.Warning($"MyParser {description} 未完成: {ex.Message}");
            }
        });
    }

    public static string ResolveCookiePath(IBotContext context, MyParserConfig config, string? configuredFileName, string defaultFileName)
    {
        var pluginDir = Path.GetDirectoryName(context.Config.ConfigPath) ?? AppContext.BaseDirectory;
        var cookieDir = string.IsNullOrWhiteSpace(config.CookieDirectory)
            ? Path.Combine(pluginDir, "cookie")
            : config.CookieDirectory.Trim();

        if (!Path.IsPathRooted(cookieDir))
        {
            cookieDir = Path.Combine(pluginDir, cookieDir);
        }

        Directory.CreateDirectory(cookieDir);
        var fileName = string.IsNullOrWhiteSpace(configuredFileName) ? defaultFileName : configuredFileName.Trim();
        if (Path.IsPathRooted(fileName))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fileName) ?? cookieDir);
            return fileName;
        }

        return Path.Combine(cookieDir, fileName);
    }

    public static async Task<string> UploadLocalVideoFileAsync(
        IBotContext context,
        MyParserConfig config,
        IncomingMessage message,
        string? localVideoPath,
        string platformName,
        string mediaId)
    {
        if (string.IsNullOrWhiteSpace(localVideoPath) || !File.Exists(localVideoPath))
        {
            throw new InvalidOperationException("本地视频文件不存在。");
        }

        var localPath = Path.GetFullPath(localVideoPath);
        var fileSize = new FileInfo(localPath).Length;
        var useBase64 = config.UploadVideoAsBase64 && MemorySafetyUtilities.CanUseBase64ForFile(fileSize, config.UploadVideoBase64MaxMegabytes);
        var fileUri = useBase64 ? "base64://" + Convert.ToBase64String(await File.ReadAllBytesAsync(localPath)) : new Uri(localPath).AbsoluteUri;
        var fileName = Path.GetFileName(localPath);
        var uploadMode = useBase64 ? "base64" : "file";
        var stopwatch = Stopwatch.StartNew();

        BotLog.Info($"MyParser {platformName} 文件上传开始: media_id={mediaId}, mode={uploadMode}, file_mb={fileSize / 1024d / 1024d:F2}, base64_limit_mb={config.UploadVideoBase64MaxMegabytes}, file={localPath}");

        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await context.File.UploadGroupFileAsync(group.Group.GroupId, fileUri, fileName);
                EnsureFileUploadAccepted(response.FileId, "group", uploadMode);
                return $"群文件 FileId={response.FileId} Mode={uploadMode} elapsed={stopwatch.Elapsed:mm\\:ss}";
            }
            case FriendIncomingMessage friend:
            {
                var response = await context.File.UploadPrivateFileAsync(friend.SenderId, fileUri, fileName);
                EnsureFileUploadAccepted(response.FileId, "friend", uploadMode);
                return $"私聊文件 FileId={response.FileId} Mode={uploadMode} elapsed={stopwatch.Elapsed:mm\\:ss}";
            }
            default:
                throw new NotSupportedException("当前消息类型不支持文件上传。");
        }
    }

    public static void EnsureFileUploadAccepted(string? fileId, string scene, string uploadMode)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            throw new InvalidOperationException($"文件上传返回空 FileId，发送未确认。scene={scene}, mode={uploadMode}");
        }
    }

    public static string GetMessageScene(IncomingMessage message) => message switch
    {
        GroupIncomingMessage => "group",
        FriendIncomingMessage => "friend",
        TempIncomingMessage => "temp",
        _ => "unknown",
    };
}
