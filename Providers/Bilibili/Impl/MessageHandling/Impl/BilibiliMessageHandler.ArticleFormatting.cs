
using System.Text;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Models;


namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Impl.MessageHandling.Impl;

internal sealed partial class BilibiliMessageHandler
{
private string FormatBilibiliArticleResult(BilibiliArticleParseResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.IsOpus ? "Bilibili 图文解析成功" : "Bilibili 专栏解析成功");
        sb.AppendLine(result.IsOpus ? $"Opus：{result.OpusId}" : $"CV：cv{result.Cvid}");
        if (!string.IsNullOrWhiteSpace(result.Title)) sb.AppendLine($"标题：{TrimLine(result.Title, 140)}");
        if (!string.IsNullOrWhiteSpace(result.AuthorName)) sb.AppendLine($"作者：{result.AuthorName}");
        if (result.PublishTime is not null) sb.AppendLine($"发布时间：{result.PublishTime:yyyy-MM-dd HH:mm}");
        if (result.Words > 0) sb.AppendLine($"字数：{result.Words}");
        if (result.Categories.Count > 0) sb.AppendLine($"分类：{string.Join(" / ", result.Categories)}");
        if (!string.IsNullOrWhiteSpace(result.Summary)) sb.AppendLine($"摘要：{TrimLine(result.Summary, 220)}");
        else if (!string.IsNullOrWhiteSpace(result.PlainText)) sb.AppendLine($"摘要：{TrimLine(result.PlainText, 220)}");
        sb.AppendLine($"图片数：{result.ImageUrls.Count}");
        sb.AppendLine($"数据：{FormatCount(result.ViewCount)}阅读 / {FormatCount(result.LikeCount)}赞 / {FormatCount(result.CoinCount)}投币 / {FormatCount(result.FavoriteCount)}收藏 / {FormatCount(result.ReplyCount)}评论");
        if (!string.IsNullOrWhiteSpace(result.SourceUrl)) sb.AppendLine($"链接：{result.SourceUrl}");
        return sb.ToString().TrimEnd();
    }
}
