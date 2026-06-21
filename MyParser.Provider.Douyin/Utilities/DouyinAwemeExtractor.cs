using System.Text.Json;
using static MyParser.Provider.Douyin.Utilities.DouyinParseHelpers;

namespace MyParser.Provider.Douyin.Utilities;

public static class DouyinAwemeExtractor
{
    public static bool TryGetAwemeDetail(JsonElement root, out JsonElement aweme)
    {
        if (TryGetProperty(root, "aweme_detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
        {
            aweme = detail;
            return true;
        }

        if (TryGetProperty(root, "loaderData", out var loaderData) && loaderData.ValueKind == JsonValueKind.Object)
        {
            foreach (var pageKey in new[] { "video_(id)/page", "note_(id)/page" })
            {
                if (!TryGetProperty(loaderData, pageKey, out var page)
                    || !TryGetProperty(page, "videoInfoRes", out var videoInfo)
                    || !TryGetProperty(videoInfo, "item_list", out var itemList)
                    || itemList.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var first = itemList.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    aweme = first;
                    return true;
                }
            }
        }

        aweme = default;
        return false;
    }
}
