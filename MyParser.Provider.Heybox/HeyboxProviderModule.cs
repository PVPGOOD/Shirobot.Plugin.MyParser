using MyParser.Provider.Heybox.MessageHandling;
using MyParser.Provider.Heybox.Parsing;

namespace MyParser.Provider.Heybox;

public sealed class HeyboxProviderModule : MyParserProviderModuleBase, IProviderMessageHandlerFactory, IProviderAutoParsePolicy, IProviderResultMessageClassifier
{
    public override string Id => "heybox";

    public override string DisplayName => "小黑盒";

    public override IReadOnlyList<IParseProvider> CreateProviders(PluginConfig config)
    {
        return [new HeyboxParseProvider(new HeyboxParser(config))];
    }

    public IProviderMessageHandler? CreateMessageHandler(ProviderMessageHandlerContext context)
    {
        return new HeyboxMessageHandler(context);
    }

    public bool IsAutoParseEnabled(PluginConfig config) => true;

    public bool IsPluginResultMessage(string text)
    {
        return text.StartsWith("小黑盒解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Heybox 解析", StringComparison.OrdinalIgnoreCase);
    }
}
