using System.Web;

namespace Shirobot.Plugin.MyParser.Providers.Douyin.Utilities;

internal static class DouyinQueryBuilder
{
    public static string BuildUserProfileQuery(string secUserId)
    {
        var pairs = CreateCommonWebPairs();
        pairs["publish_video_strategy_type"] = "2";
        pairs["source"] = "channel_pc_web";
        pairs["sec_user_id"] = secUserId;
        return Build(pairs);
    }

    public static string BuildSearchItemQuery(string keyword)
    {
        var pairs = CreateCommonWebPairs();
        pairs["search_channel"] = "aweme_video_web";
        pairs["sort_type"] = "0";
        pairs["publish_time"] = "0";
        pairs["keyword"] = keyword;
        pairs["search_source"] = "normal_search";
        pairs["query_correct_type"] = "1";
        pairs["is_filter_search"] = "0";
        pairs["offset"] = "0";
        pairs["count"] = "12";
        AddNetworkFields(pairs);
        return Build(pairs);
    }

    public static string BuildUserPostQuery(string secUserId, string awemeId, long maxCursor)
    {
        var pairs = CreateCommonWebPairs();
        pairs["sec_user_id"] = secUserId;
        pairs["max_cursor"] = maxCursor.ToString();
        pairs["locate_item_id"] = awemeId;
        pairs["locate_query"] = "false";
        pairs["show_live_replay_strategy"] = "1";
        pairs["need_time_list"] = "0";
        pairs["time_list_query"] = "0";
        pairs["whale_cut_token"] = string.Empty;
        pairs["cut_version"] = "1";
        pairs["count"] = "18";
        pairs["publish_video_strategy_type"] = "2";
        AddNetworkFields(pairs);
        return Build(pairs);
    }

    public static string BuildDetailQuery(string awemeId)
    {
        var pairs = CreateCommonWebPairs();
        pairs["aweme_id"] = awemeId;
        return Build(pairs);
    }

    private static Dictionary<string, string?> CreateCommonWebPairs()
    {
        return new Dictionary<string, string?>
        {
            ["device_platform"] = "webapp",
            ["aid"] = "6383",
            ["channel"] = "channel_pc_web",
            ["pc_client_type"] = "1",
            ["version_code"] = "290100",
            ["version_name"] = "29.1.0",
            ["cookie_enabled"] = "true",
            ["browser_language"] = "zh-CN",
            ["browser_platform"] = "Win32",
            ["browser_name"] = "Chrome",
            ["browser_version"] = "130.0.0.0",
            ["browser_online"] = "true",
            ["engine_name"] = "Blink",
            ["engine_version"] = "130.0.0.0",
            ["os_name"] = "Windows",
            ["os_version"] = "10",
            ["platform"] = "PC",
            ["screen_width"] = "1920",
            ["screen_height"] = "1080",
            ["device_memory"] = "8",
            ["cpu_core_num"] = "8",
            ["msToken"] = string.Empty,
        };
    }

    private static void AddNetworkFields(Dictionary<string, string?> pairs)
    {
        pairs["downlink"] = "10";
        pairs["effective_type"] = "4g";
        pairs["round_trip_time"] = "50";
    }

    private static string Build(Dictionary<string, string?> pairs)
    {
        return string.Join("&", pairs.Select(kv => $"{HttpUtility.UrlEncode(kv.Key)}={HttpUtility.UrlEncode(kv.Value ?? string.Empty)}"));
    }
}
