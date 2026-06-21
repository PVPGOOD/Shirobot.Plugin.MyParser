using ShiroBot.Model.Common;

namespace Shirobot.Plugin.MyParser.Parsing;

public interface IIncomingMessageParseProvider : IParseProvider
{
    string? ExtractParseText(IncomingMessage message);
}
