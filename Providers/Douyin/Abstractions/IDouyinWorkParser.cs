using Shirobot.Plugin.MyParser.Providers.Douyin.Models;
using System.Text.Json;

namespace Shirobot.Plugin.MyParser.Providers.Douyin.Abstractions;

internal interface IDouyinWorkParser
{
    bool CanParse(JsonElement aweme);
    DouyinParseResult Parse(JsonElement aweme, string fallbackAwemeId, string sourceUrl);
}
