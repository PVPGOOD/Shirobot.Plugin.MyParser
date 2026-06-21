using Shirobot.Plugin.MyParser.Parsing;
using MyParser.Provider.Xiaohongshu.Parsing;
using MyParser.Provider.Xiaohongshu.MessageHandling;

namespace MyParser.Provider.Xiaohongshu;

public sealed class XiaohongshuProviderModule : MyParserProviderModuleBase, IProviderMessageHandlerFactory
{
    public override string Id => "xiaohongshu";

    public override string DisplayName => "小红书";

    public override IReadOnlyList<IParseProvider> CreateProviders(PluginConfig config)
    {
        return [new XiaohongshuParseProvider(new XiaohongshuParser(config))];
    }

    public IProviderMessageHandler? CreateMessageHandler(ProviderMessageHandlerContext context)
    {
        return new XiaohongshuMessageHandler(context.BotContext, context.Config, context.ProviderRegistry, context.PrimaryProvider, context.HostServices);
    }
}
