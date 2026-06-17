using Shirobot.Plugin.MyParser.Providers.Bilibili.Facade;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Impl.MessageHandling;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Impl.Services;
using Shirobot.Plugin.MyParser.Providers.Douyin.Impl.MessageHandling;
using Shirobot.Plugin.MyParser.Providers.Douyin.Facade;
using Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Facade;
using Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Impl.MessageHandling;
using System.Text;
using Shirobot.Plugin.MyParser.Parsing;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.MyParser;

[BotPlugin(id: "MyParser",
    Name = "MyParser",
    Version = "0.1.0",
    Author = "PVPGood",
    Category = PluginCategory.Utility,
    Description = "面向 Shirobot 的学习型内容消息处理插件。",
    GithubRepo = "PVPGOOD/Shirobot.Plugin.MyParser/",
    IsPluginSingleFile = false)
]
public sealed class MyParserPlugin : PluginBase
{
    private MyParserConfig _config = new();
    private DouyinParseProvider? _douyinProvider;
    private BilibiliParseProvider? _bilibiliProvider;
    private BilibiliArticleParseProvider? _bilibiliArticleProvider;
    private BilibiliBangumiParseProvider? _bilibiliBangumiProvider;
    private BilibiliLiveParseProvider? _bilibiliLiveProvider;
    private XiaohongshuParseProvider? _xiaohongshuProvider;
    private ParseProviderRegistry? _providerRegistry;
    private DouyinMessageHandler? _douyinMessageHandler;
    private BilibiliMessageHandler? _bilibiliMessageHandler;
    private XiaohongshuMessageHandler? _xiaohongshuMessageHandler;

    public override string Name => "MyParser";

    protected override async Task LoadAsync()
    {
        _config = Context.Config.Load<MyParserConfig>();
        LoadDouyinCookieFromPluginDirectory();
        LoadBilibiliCookieFromPluginDirectory();
        LoadXiaohongshuCookieFromPluginDirectory();
        Context.Config.Save(_config);
        _douyinProvider = new DouyinParseProvider(new DouyinParser(_config));
        var bilibiliParser = new BilibiliParser(_config);
        _bilibiliProvider = new BilibiliParseProvider(bilibiliParser);
        _bilibiliArticleProvider = new BilibiliArticleParseProvider(bilibiliParser);
        _bilibiliBangumiProvider = new BilibiliBangumiParseProvider(new BilibiliBangumiParser(bilibiliParser.HttpClient, _config));
        _bilibiliLiveProvider = new BilibiliLiveParseProvider(new BilibiliLiveParser(bilibiliParser.HttpClient, _config));
        _xiaohongshuProvider = new XiaohongshuParseProvider(new XiaohongshuParser(_config));
        _providerRegistry = new ParseProviderRegistry([_xiaohongshuProvider, _douyinProvider, _bilibiliArticleProvider, _bilibiliBangumiProvider, _bilibiliLiveProvider, _bilibiliProvider]);
        _douyinMessageHandler = new DouyinMessageHandler(Context, _config, _providerRegistry, _douyinProvider);
        _bilibiliMessageHandler = new BilibiliMessageHandler(Context, _config, _providerRegistry, _bilibiliProvider);
        _xiaohongshuMessageHandler = new XiaohongshuMessageHandler(Context, _config, _providerRegistry, _xiaohongshuProvider);

        if (_config.CheckDouyinCookieLoginStatusOnStartup)
        {
            await LogDouyinCookieLoginStatusAsync();
        }

        if (_config.CheckBilibiliCookieLoginStatusOnStartup)
        {
            await LogBilibiliCookieLoginStatusAsync();
        }

        if (_config.CheckXiaohongshuCookieLoginStatusOnStartup)
        {
            await LogXiaohongshuCookieLoginStatusAsync();
        }

        FriendCommands.MapExact("#parser", HandleHelpAsync);
        GroupCommands.MapExact("#parser", HandleHelpAsync);
        FriendCommands.MapExact(_config.BilibiliLoginCommand, HandleBilibiliLoginAsync);
        GroupCommands.MapExact(_config.BilibiliLoginCommand, HandleBilibiliLoginAsync);
        FriendCommands.MapExact(_config.XiaohongshuLoginCommand, HandleXiaohongshuLoginAsync);
        GroupCommands.MapExact(_config.XiaohongshuLoginCommand, HandleXiaohongshuLoginAsync);
        FriendCommands.MapExact(_config.DouyinCookieCheckCommand, HandleDouyinCookieCheckAsync);
        GroupCommands.MapExact(_config.DouyinCookieCheckCommand, HandleDouyinCookieCheckAsync);
        FriendCommands.MapExact(_config.BilibiliCookieCheckCommand, HandleBilibiliCookieCheckAsync);
        GroupCommands.MapExact(_config.BilibiliCookieCheckCommand, HandleBilibiliCookieCheckAsync);
        FriendCommands.MapExact(_config.XiaohongshuCookieCheckCommand, HandleXiaohongshuCookieCheckAsync);
        GroupCommands.MapExact(_config.XiaohongshuCookieCheckCommand, HandleXiaohongshuCookieCheckAsync);

        FriendCommands.MapPrefix(_config.ParseCommandPrefix, HandleParseCommandAsync);
        GroupCommands.MapPrefix(_config.ParseCommandPrefix, HandleParseCommandAsync);

        if (_config.AutoParseDouyinLinks || _config.AutoParseBilibiliLinks || _config.AutoParseXiaohongshuLinks)
        {
            FriendCommands.MapWhen(ShouldAutoParse, HandleAutoParseAsync);
            GroupCommands.MapWhen(ShouldAutoParse, HandleAutoParseAsync);
        }

        BotLog.Info($"MyParser 已加载：抖音/Bilibili/小红书 解析启用。命令：#parser / #parse <链接> / {_config.BilibiliLoginCommand} / {_config.XiaohongshuLoginCommand}");
    }

    private async Task LogDouyinCookieLoginStatusAsync()
    {
        if (_douyinProvider is null)
        {
            return;
        }

        try
        {
            var status = await _douyinProvider.Parser.CheckLoginStatusAsync();
            BotLog.Info($"MyParser DouyinCookie 登录状态：{status}");
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser DouyinCookie 登录状态检查失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task LogBilibiliCookieLoginStatusAsync()
    {
        if (_bilibiliProvider is null)
        {
            return;
        }

        try
        {
            var status = await _bilibiliProvider.Parser.CheckLoginStatusAsync();
            BotLog.Info($"MyParser BilibiliCookie 登录状态：{status.Message}");
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser BilibiliCookie 登录状态检查失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task LogXiaohongshuCookieLoginStatusAsync()
    {
        if (_xiaohongshuProvider is null)
        {
            return;
        }

        try
        {
            var status = await _xiaohongshuProvider.Parser.CheckLoginStatusAsync();
            BotLog.Info($"MyParser XiaohongshuCookie 登录状态：{status.Message}");
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser XiaohongshuCookie 登录状态检查失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LoadDouyinCookieFromPluginDirectory()
    {
        var pluginDir = Path.GetDirectoryName(Context.Config.ConfigPath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(pluginDir);

        var cookieFileName = string.IsNullOrWhiteSpace(_config.DouyinCookieFileName)
            ? "douyin_cookie.txt"
            : _config.DouyinCookieFileName.Trim();
        var cookiePath = Path.IsPathRooted(cookieFileName)
            ? cookieFileName
            : Path.Combine(pluginDir, cookieFileName);

        if (!File.Exists(cookiePath))
        {
            if (_config.CreateDouyinCookieFileIfMissing)
            {
                File.WriteAllText(cookiePath, string.Empty, Encoding.UTF8);
                BotLog.Info($"MyParser 已创建抖音 Cookie 文件：{cookiePath}");
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(_config.DouyinCookie))
        {
            return;
        }

        var cookie = File.ReadAllText(cookiePath, Encoding.UTF8).Trim().TrimStart('\ufeff');
        if (string.IsNullOrWhiteSpace(cookie))
        {
            BotLog.Info($"MyParser DouyinCookie 为空；可编辑文件后重启：{cookiePath}");
            return;
        }

        if (!LooksLikeDouyinCookie(cookie))
        {
            BotLog.Warning($"MyParser 忽略无效 DouyinCookie 文件：{cookiePath}。请确保文件内容是浏览器 Request Headers 中 Cookie: 后面的完整值。");
            return;
        }

        _config.DouyinCookie = cookie;
        BotLog.Info($"MyParser 已从插件目录读取 DouyinCookie：{cookiePath}");
    }

    private void LoadBilibiliCookieFromPluginDirectory()
    {
        var pluginDir = Path.GetDirectoryName(Context.Config.ConfigPath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(pluginDir);

        var cookieFileName = string.IsNullOrWhiteSpace(_config.BilibiliCookieFileName)
            ? "bilibili_cookie.txt"
            : _config.BilibiliCookieFileName.Trim();
        var cookiePath = Path.IsPathRooted(cookieFileName)
            ? cookieFileName
            : Path.Combine(pluginDir, cookieFileName);

        if (!File.Exists(cookiePath))
        {
            if (_config.CreateBilibiliCookieFileIfMissing)
            {
                File.WriteAllText(cookiePath, string.Empty, Encoding.UTF8);
                BotLog.Info($"MyParser 已创建 Bilibili Cookie 文件：{cookiePath}");
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(_config.BilibiliCookie))
        {
            return;
        }

        var cookie = File.ReadAllText(cookiePath, Encoding.UTF8).Trim().TrimStart('\ufeff');
        if (string.IsNullOrWhiteSpace(cookie))
        {
            BotLog.Info($"MyParser BilibiliCookie 为空；可发送 {_config.BilibiliLoginCommand} 扫码登录，或编辑文件后重启：{cookiePath}");
            return;
        }

        if (!BilibiliParser.LooksLikeBilibiliCookie(cookie))
        {
            BotLog.Warning($"MyParser 忽略无效 BilibiliCookie 文件：{cookiePath}。请确保文件内容包含 SESSDATA/bili_jct 等 Cookie。");
            return;
        }

        _config.BilibiliCookie = cookie;
        BotLog.Info($"MyParser 已从插件目录读取 BilibiliCookie：{cookiePath}");
    }

    private void LoadXiaohongshuCookieFromPluginDirectory()
    {
        var pluginDir = Path.GetDirectoryName(Context.Config.ConfigPath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(pluginDir);

        var cookieFileName = string.IsNullOrWhiteSpace(_config.XiaohongshuCookieFileName)
            ? "xiaohongshu_cookie.txt"
            : _config.XiaohongshuCookieFileName.Trim();
        var cookiePath = Path.IsPathRooted(cookieFileName)
            ? cookieFileName
            : Path.Combine(pluginDir, cookieFileName);

        if (!File.Exists(cookiePath))
        {
            if (_config.CreateXiaohongshuCookieFileIfMissing)
            {
                File.WriteAllText(cookiePath, string.Empty, Encoding.UTF8);
                BotLog.Info($"MyParser 已创建小红书 Cookie 文件：{cookiePath}");
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(_config.XiaohongshuCookie))
        {
            return;
        }

        var cookie = File.ReadAllText(cookiePath, Encoding.UTF8).Trim().TrimStart('\ufeff');
        if (string.IsNullOrWhiteSpace(cookie))
        {
            BotLog.Info($"MyParser XiaohongshuCookie 为空；可发送 {_config.XiaohongshuLoginCommand} 扫码登录，或编辑文件后重启：{cookiePath}");
            return;
        }

        _config.XiaohongshuCookie = cookie;
        BotLog.Info($"MyParser 已从插件目录读取 XiaohongshuCookie：{cookiePath}");
    }

    private static bool LooksLikeDouyinCookie(string cookie)
    {
        return cookie.Contains("sessionid=", StringComparison.OrdinalIgnoreCase)
               && cookie.Contains("ttwid=", StringComparison.OrdinalIgnoreCase)
               && cookie.Contains(';');
    }

    protected override Task OnUnloadAsync()
    {
        _douyinMessageHandler?.Dispose();
        _douyinMessageHandler = null;
        _bilibiliMessageHandler?.Dispose();
        _bilibiliMessageHandler = null;
        _douyinProvider?.Dispose();
        _douyinProvider = null;
        _bilibiliProvider?.Dispose();
        _bilibiliProvider = null;
        _bilibiliArticleProvider = null;
        _xiaohongshuMessageHandler?.Dispose();
        _xiaohongshuMessageHandler = null;
        _xiaohongshuProvider?.Dispose();
        _xiaohongshuProvider = null;
        _providerRegistry = null;
        BotLog.Info("MyParser 已卸载。");
        return Task.CompletedTask;
    }

    private Task HandleHelpAsync(IncomingMessage message)
    {
        var help = "MyParser\n"
                   + "当前支持：抖音视频 / 图集 / LivePhoto、Bilibili 视频/专栏/图文、小红书视频/图文/评论卡片\n\n"
                   + "用法：\n"
                   + $"1. {_config.ParseCommandPrefix} <抖音/Bilibili/小红书 分享链接>\n"
                   + "2. 直接发送抖音/Bilibili/小红书链接可自动解析\n"
                   + $"3. {_config.BilibiliLoginCommand}：Bilibili 扫码登录并保存 Cookie\n"
                   + $"4. {_config.XiaohongshuLoginCommand}：小红书扫码登录并保存 Cookie\n"
                   + $"5. {_config.DouyinCookieCheckCommand} / {_config.BilibiliCookieCheckCommand} / {_config.XiaohongshuCookieCheckCommand}：检查 Cookie 有效性\n\n"
                   + "Cookie 文件：插件目录/douyin_cookie.txt、bilibili_cookie.txt、xiaohongshu_cookie.txt\n"
                   + "Bilibili 说明：需要登录态；视频/音频流会分别下载，并用本地 ffmpeg 合并后发送。\n"
                   + "小红书说明：需要自行搭建 xhshow sign 服务，并在运行时配置 sign URL/token。";
        return Context.Message.ReplyAsync(message, help);
    }

    private bool ShouldAutoParse(IncomingMessage message)
    {
        if (TryBuildBilibiliPageLinkFromReply(message, out _))
        {
            return _config.AutoParseBilibiliLinks;
        }

        var text = GetPlainText(message);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith(_config.ParseCommandPrefix, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("#parser", StringComparison.OrdinalIgnoreCase)
                || IsPluginResultMessage(trimmed)
                || IsBilibiliPageTemplateLink(trimmed)
                || trimmed.StartsWith(_config.BilibiliLoginCommand, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(_config.XiaohongshuLoginCommand, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(_config.DouyinCookieCheckCommand, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(_config.BilibiliCookieCheckCommand, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(_config.XiaohongshuCookieCheckCommand, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var provider = _providerRegistry?.FindProvider(message, out _);
        return provider?.Id switch
        {
            "douyin" => _config.AutoParseDouyinLinks,
            "bilibili" => _config.AutoParseBilibiliLinks,
            "bilibili-article" => _config.AutoParseBilibiliLinks,
            "bilibili-bangumi" => _config.AutoParseBilibiliLinks,
            "bilibili-live" => _config.AutoParseBilibiliLinks,
            "xiaohongshu" => _config.AutoParseXiaohongshuLinks,
            _ => false,
        };
    }

    private Task HandleAutoParseAsync(IncomingMessage message)
    {
        if (TryBuildBilibiliPageLinkFromReply(message, out var pageLink))
        {
            return DispatchParseAsync(message, pageLink);
        }

        return _providerRegistry?.FindProvider(message, out var parseText) is not null && !string.IsNullOrWhiteSpace(parseText)
            ? DispatchParseAsync(message, parseText)
            : Task.CompletedTask;
    }

    private Task HandleParseCommandAsync(IncomingMessage message)
    {
        var text = GetPlainText(message);
        var content = text.Length <= _config.ParseCommandPrefix.Length
            ? string.Empty
            : text[_config.ParseCommandPrefix.Length..].Trim();

        return DispatchParseAsync(message, string.IsNullOrWhiteSpace(content) ? text : content);
    }

    private async Task HandleBilibiliLoginAsync(IncomingMessage message)
    {
        if (!await EnsurePrivateAdminCommandAsync(message, _config.BilibiliLoginCommand))
        {
            return;
        }

        if (_bilibiliMessageHandler is not null)
        {
            await _bilibiliMessageHandler.HandleLoginAsync(message);
        }
    }

    private async Task HandleXiaohongshuLoginAsync(IncomingMessage message)
    {
        if (!await EnsurePrivateAdminCommandAsync(message, _config.XiaohongshuLoginCommand))
        {
            return;
        }

        if (_xiaohongshuMessageHandler is not null)
        {
            await _xiaohongshuMessageHandler.HandleLoginAsync(message);
        }
    }

    private async Task HandleDouyinCookieCheckAsync(IncomingMessage message)
    {
        if (!await EnsurePrivateAdminCommandAsync(message, _config.DouyinCookieCheckCommand))
        {
            return;
        }

        if (_douyinProvider is null)
        {
            await Context.Message.ReplyAsync(message, "Douyin 解析器尚未初始化。");
            return;
        }

        try
        {
            var status = await _douyinProvider.Parser.CheckLoginStatusAsync();
            await Context.Message.ReplyAsync(message, "DouyinCookie 状态：" + status);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser DouyinCookie 手动检查失败：{ex}");
            await Context.Message.ReplyAsync(message, "DouyinCookie 状态检查失败：" + ex.Message);
        }
    }

    private async Task HandleBilibiliCookieCheckAsync(IncomingMessage message)
    {
        if (!await EnsurePrivateAdminCommandAsync(message, _config.BilibiliCookieCheckCommand))
        {
            return;
        }

        if (_bilibiliProvider is null)
        {
            await Context.Message.ReplyAsync(message, "Bilibili 解析器尚未初始化。");
            return;
        }

        try
        {
            var status = await _bilibiliProvider.Parser.CheckLoginStatusAsync();
            var detail = status.IsLogin
                ? $"有效 / 已登录：{status.UserName ?? "未知用户"} (mid={status.Mid})"
                : "无效 / 未登录：" + status.Message;
            await Context.Message.ReplyAsync(message, "BilibiliCookie 状态：" + detail);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser BilibiliCookie 手动检查失败：{ex}");
            await Context.Message.ReplyAsync(message, "BilibiliCookie 状态检查失败：" + ex.Message);
        }
    }

    private async Task HandleXiaohongshuCookieCheckAsync(IncomingMessage message)
    {
        if (!await EnsurePrivateAdminCommandAsync(message, _config.XiaohongshuCookieCheckCommand))
        {
            return;
        }

        if (_xiaohongshuProvider is null)
        {
            await Context.Message.ReplyAsync(message, "小红书解析器尚未初始化。");
            return;
        }

        try
        {
            var status = await _xiaohongshuProvider.Parser.CheckLoginStatusAsync();
            var detail = status.IsLogin
                ? $"有效 / 已登录：{status.UserName ?? "未知用户"}" + (string.IsNullOrWhiteSpace(status.UserId) ? string.Empty : $" ({status.UserId})")
                : status.NeedVerify ? "触发安全验证：" + status.Message : "无效 / 未登录：" + status.Message;
            await Context.Message.ReplyAsync(message, "XiaohongshuCookie 状态：" + detail);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser XiaohongshuCookie 手动检查失败：{ex}");
            await Context.Message.ReplyAsync(message, "XiaohongshuCookie 状态检查失败：" + ex.Message);
        }
    }

    private async Task<bool> EnsurePrivateAdminCommandAsync(IncomingMessage message, string command)
    {
        switch (message)
        {
            case FriendIncomingMessage friend when Context.IsAdmin(friend.SenderId):
                return true;
            case FriendIncomingMessage:
                await Context.Message.ReplyAsync(message, $"{command} 仅允许机器人 Owner/Admin 私信使用。");
                return false;
            case GroupIncomingMessage:
                await Context.Message.ReplyAsync(message, $"{command} 涉及账号登录凭据，仅允许机器人 Owner/Admin 私信机器人使用，请不要在群内触发。");
                return false;
            case TempIncomingMessage:
                await Context.Message.ReplyAsync(message, $"{command} 仅允许机器人 Owner/Admin 私信使用，不支持临时会话。");
                return false;
            default:
                await Context.Message.ReplyAsync(message, $"{command} 仅允许机器人 Owner/Admin 私信使用。");
                return false;
        }
    }

    private static bool TryBuildBilibiliPageLinkFromReply(IncomingMessage message, out string pageLink)
    {
        pageLink = string.Empty;
        var text = GetPlainText(message).Trim();
        if (!int.TryParse(text, out var page) || page <= 0)
        {
            return false;
        }

        var reply = message switch
        {
            GroupIncomingMessage group => group.GetReply(),
            FriendIncomingMessage friend => friend.GetReply(),
            _ => null,
        };
        if (reply is null)
        {
            return false;
        }

        var repliedText = string.Concat(reply.Segments.OfType<TextIncomingSegment>().Select(i => i.Text)).Trim();
        if (!IsBilibiliPageTemplateLink(repliedText))
        {
            return false;
        }

        pageLink = repliedText + page;
        return true;
    }

    private static bool IsBilibiliPageTemplateLink(string text)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            text.Trim(),
            @"^https?://www\.bilibili\.com/video/BV[0-9A-Za-z]{10}/?\?p=\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool IsPluginResultMessage(string text)
    {
        return text.StartsWith("Bilibili 视频解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Bilibili 直播解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Bilibili 图文解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Bilibili 专栏解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Douyin 解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("抖音解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("小红书解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Xiaohongshu", StringComparison.OrdinalIgnoreCase);
    }

    private Task DispatchParseAsync(IncomingMessage message, string text)
    {
        if (IsBilibiliPageTemplateLink(text))
        {
            return Task.CompletedTask;
        }

        var provider = _providerRegistry?.FindProvider(text);
        return provider?.Id switch
        {
            "douyin" => _douyinMessageHandler?.ParseAndReplyAsync(message, text) ?? Task.CompletedTask,
            "bilibili" => _bilibiliMessageHandler?.ParseAndReplyAsync(message, text) ?? Task.CompletedTask,
            "bilibili-article" => _bilibiliMessageHandler?.ParseAndReplyAsync(message, text) ?? Task.CompletedTask,
            "bilibili-bangumi" => _bilibiliMessageHandler?.ParseAndReplyAsync(message, text) ?? Task.CompletedTask,
            "bilibili-live" => _bilibiliMessageHandler?.ParseAndReplyAsync(message, text) ?? Task.CompletedTask,
            "xiaohongshu" => _xiaohongshuMessageHandler?.ParseAndReplyAsync(message, text) ?? Task.CompletedTask,
            _ => Context.Message.ReplyAsync(message, "未找到可处理该链接的解析提供商。"),
        };
    }

    private static string GetPlainText(IncomingMessage message) => message switch
    {
        FriendIncomingMessage friend => friend.GetPlainText(),
        GroupIncomingMessage group => group.GetPlainText(),
        TempIncomingMessage temp => string.Concat(temp.Segments.OfType<TextIncomingSegment>().Select(i => i.Text)),
        _ => string.Empty,
    };
}

