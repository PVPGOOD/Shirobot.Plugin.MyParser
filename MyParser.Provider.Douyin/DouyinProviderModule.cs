using Shirobot.Plugin.MyParser.Parsing;
using MyParser.Provider.Douyin.Parsing;
using MyParser.Provider.Douyin.MessageHandling;
using ShiroBot.Model.Common;

namespace MyParser.Provider.Douyin;

public sealed class DouyinProviderModule : MyParserProviderModuleBase, IProviderMessageHandlerFactory, ICookieValidator, IProviderCookieStore, IProviderAutoParsePolicy, IProviderResultMessageClassifier, IProviderCommandContributor
{
    public override string Id => "douyin";

    public override string DisplayName => "抖音";

    public IReadOnlyList<ProviderCookieDescriptor> CookieDescriptors =>
    [
        new(
            Id,
            DisplayName,
            "douyin.txt",
            cookie => MyParserRuntime.DouyinCookie = cookie,
            LooksLikeCookie,
            EmptyHint: "可编辑文件后重启或等待热重载。",
            InvalidHint: "请确保文件内容是浏览器 Request Headers 中 Cookie: 后面的完整值。")
    ];

    public override IReadOnlyList<IParseProvider> CreateProviders(PluginConfig config)
    {
        return [new DouyinParseProvider(new DouyinParser(config))];
    }

    public IProviderMessageHandler? CreateMessageHandler(ProviderMessageHandlerContext context)
    {
        return new DouyinMessageHandler(context.BotContext, context.Config, context.ProviderRegistry, context.PrimaryProvider, context.HostServices);
    }

    public bool LooksLikeCookie(string cookie)
    {
        return cookie.Contains("sessionid=", StringComparison.OrdinalIgnoreCase)
               && cookie.Contains("ttwid=", StringComparison.OrdinalIgnoreCase)
               && cookie.Contains(';');
    }

    public bool IsAutoParseEnabled(PluginConfig config) => config.AutoParseDouyinLinks;

    public bool IsPluginResultMessage(string text)
    {
        return text.StartsWith("Douyin 解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("抖音解析", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ProviderCommandDescriptor> CreateCommands(ProviderCommandContext context)
    {
        return [new ProviderCommandDescriptor("#douyin-cookie-check", message => HandleCookieCheckAsync(context, message), AdminOnly: true)];
    }

    private static async Task HandleCookieCheckAsync(ProviderCommandContext context, IncomingMessage message)
    {
        if (context.PrimaryProvider is not IProviderLoginStatusProvider loginStatusProvider)
        {
            await context.BotContext.Message.ReplyAsync(message, "Douyin 解析器尚未初始化或不支持 Cookie 检查。");
            return;
        }

        var status = await loginStatusProvider.CheckLoginStatusAsync();
        var detail = status.IsLogin
            ? $"有效 / 已登录：{status.UserName ?? "未知用户"}" + (string.IsNullOrWhiteSpace(status.UserId) ? string.Empty : $" ({status.UserId})")
            : status.NeedVerify ? "触发安全验证：" + status.Message : "无效 / 未登录：" + status.Message;
        await context.BotContext.Message.ReplyAsync(message, "DouyinCookie 状态：" + detail);
    }
}
