using ShiroBot.Model.Common;

namespace Shirobot.Plugin.MyParser.Parsing;

internal interface IIncomingMessageParseProvider : IParseProvider
{
    string? ExtractParseText(IncomingMessage message);
}
