using Shirobot.Plugin.MyParser.Parsing;
using MyParser.Provider.Douyin.Parsing;
using MyParser.Provider.Douyin.MessageHandling;

namespace MyParser.Provider.Douyin;

public sealed class DouyinProviderModule : MyParserProviderModuleBase, IProviderMessageHandlerFactory, ICookieValidator
{
    public override string Id => "douyin";

    public override string DisplayName => "抖音";

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
}
