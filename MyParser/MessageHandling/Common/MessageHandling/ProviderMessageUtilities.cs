using System.Diagnostics;
using System.Collections.Concurrent;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.MyParser.MessageHandling;

internal static class ProviderMessageUtilities
{
    private static readonly ConcurrentDictionary<string, byte> SentReactions = new(StringComparer.Ordinal);

    public static async Task ReactAsync(IBotContext context, IncomingMessage message, string faceId, string platformName)
    {
        if (message is not GroupIncomingMessage group)
        {
            return;
        }

        var key = $"{group.Group.GroupId}:{group.MessageSeq}:{faceId}";
        if (!SentReactions.TryAdd(key, 0))
        {
            return;
        }

        try
        {
            await context.Group.SendGroupMessageReactionAsync(group.Group.GroupId, group.MessageSeq, faceId);
            if (!string.Equals(faceId, "351", StringComparison.OrdinalIgnoreCase))
            {
                await RemoveReactionAsync(context, group, "351", platformName);
            }
        }
        catch (Exception ex)
        {
            SentReactions.TryRemove(key, out _);
            if (IsAlreadyReactedError(ex))
            {
                BotLog.Info($"MyParser {platformName} 消息表情已存在，跳过重复贴表情: group_id={group.Group.GroupId}, message_seq={group.MessageSeq}, face={faceId}");
                return;
            }

            BotLog.Warning($"MyParser {platformName} 消息贴表情失败: group_id={group.Group.GroupId}, message_seq={group.MessageSeq}, face={faceId}, error={ex.Message}");
        }
    }

    public static Task RemoveReactionAsync(IBotContext context, IncomingMessage message, string faceId, string platformName)
    {
        return message is GroupIncomingMessage group
            ? RemoveReactionAsync(context, group, faceId, platformName)
            : Task.CompletedTask;
    }

    private static async Task RemoveReactionAsync(IBotContext context, GroupIncomingMessage group, string faceId, string platformName)
    {
        var key = $"{group.Group.GroupId}:{group.MessageSeq}:{faceId}";
        if (!SentReactions.ContainsKey(key))
        {
            return;
        }

        try
        {
            await context.Group.SendGroupMessageReactionAsync(group.Group.GroupId, group.MessageSeq, faceId, isAdd: false);
            SentReactions.TryRemove(key, out _);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser {platformName} 消息取消表情失败: group_id={group.Group.GroupId}, message_seq={group.MessageSeq}, face={faceId}, error={ex.Message}");
        }
    }

    public static void ClearReactionCache()
    {
        SentReactions.Clear();
    }

    private static bool IsAlreadyReactedError(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("已经设置过", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static Task<SendMessageResult> ReplyTextAsync(IBotContext context, PluginConfig config, IncomingMessage message, string text)
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

    public static string ResolveCookiePath(IBotContext context, string fileName)
    {
        var cookieDirectory = Path.Combine(context.PluginDirectory, "cookies");
        Directory.CreateDirectory(cookieDirectory);
        return Path.Combine(cookieDirectory, Path.GetFileName(fileName));
    }

    public static Task<string> UploadLocalVideoFileAsync(
        IBotContext context,
        PluginConfig config,
        IncomingMessage message,
        string? localVideoPath,
        string platformName,
        string mediaId)
    {
        return UploadLocalFileAsync(context, config, message, localVideoPath, platformName, mediaId);
    }

    public static async Task<string> UploadLocalFileAsync(
        IBotContext context,
        PluginConfig config,
        IncomingMessage message,
        string? localFilePath,
        string platformName,
        string mediaId,
        bool preferBase64 = false)
    {
        if (string.IsNullOrWhiteSpace(localFilePath) || !File.Exists(localFilePath))
        {
            throw new InvalidOperationException("本地文件不存在。");
        }

        var localPath = Path.GetFullPath(localFilePath);
        var fileSize = new FileInfo(localPath).Length;
        var uploadMode = preferBase64 ? "base64" : "file";
        var fileUri = preferBase64
            ? "base64://" + Convert.ToBase64String(await File.ReadAllBytesAsync(localPath))
            : new Uri(localPath).AbsoluteUri;
        var fileName = Path.GetFileName(localPath);
        var stopwatch = Stopwatch.StartNew();

        BotLog.Info($"MyParser {platformName} 文件上传开始: media_id={mediaId}, mode={uploadMode}, file_mb={fileSize / 1024d / 1024d:F2}, file={localPath}");

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

    private static void EnsureFileUploadAccepted(string? fileId, string scene, string uploadMode)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            BotLog.Warning($"MyParser 文件上传返回空 FileId，当前 ShiroBot/适配器可能不返回有效 FileId；不再按失败处理。scene={scene}, mode={uploadMode}");
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
