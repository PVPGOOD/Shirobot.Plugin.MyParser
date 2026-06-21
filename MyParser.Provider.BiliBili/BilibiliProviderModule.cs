using ShiroBot.Model.Common;
using Shirobot.Plugin.MyParser.Parsing;
using MyParser.Provider.BiliBili.Parsing;
using MyParser.Provider.BiliBili.MessageHandling;
using MyParser.Provider.BiliBili.Services;
using MyParser.Provider.BiliBili.Utilities;

namespace MyParser.Provider.BiliBili;

public sealed class BilibiliProviderModule : MyParserProviderModuleBase, IProviderMessageHandlerFactory, IIncomingProviderTextNormalizer, ICookieValidator
{
    public override string Id => "bilibili";

    public override string DisplayName => "Bilibili";

    public override IReadOnlyList<IParseProvider> CreateProviders(PluginConfig config)
    {
        var parser = new BilibiliParser(config);
        return
        [
            new BilibiliArticleParseProvider(parser),
            new BilibiliBangumiParseProvider(new BilibiliBangumiParser(parser.HttpClient, config)),
            new BilibiliLiveParseProvider(new BilibiliLiveParser(parser.HttpClient, config)),
            new BilibiliParseProvider(parser),
        ];
    }

    public IProviderMessageHandler? CreateMessageHandler(ProviderMessageHandlerContext context)
    {
        return new BilibiliMessageHandler(context.BotContext, context.Config, context.ProviderRegistry, context.PrimaryProvider, context.HostServices);
    }

    public string? NormalizeParseText(string text)
    {
        return BilibiliUrlParser.NormalizeStandaloneBilibiliId(text)
               ?? BilibiliUrlParser.ExtractStrictBilibiliUrl(text);
    }

    public string? NormalizeParseText(IncomingMessage message)
    {
        var text = string.Concat(GetSegments(message).OfType<TextIncomingSegment>().Select(i => i.Text));
        return NormalizeParseText(text)
               ?? (BilibiliLightAppUrlExtractor.ExtractParseText(message) is { } extracted
                   ? BilibiliUrlParser.ExtractStrictBilibiliUrl(extracted)
                   : null);
    }

    public bool LooksLikeCookie(string cookie) => BilibiliParser.LooksLikeBilibiliCookie(cookie);

    private static IReadOnlyList<IncomingSegment> GetSegments(IncomingMessage message)
    {
        return message switch
        {
            FriendIncomingMessage friend => friend.Segments,
            GroupIncomingMessage group => group.Segments,
            TempIncomingMessage temp => temp.Segments,
            _ => [],
        };
    }
}
