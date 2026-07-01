using System.Text.Json;
using static MyParser.Provider.Douyin.Infrastructure.DouyinRequestHeaders;
using static MyParser.Provider.Douyin.Utilities.DouyinParseHelpers;

namespace MyParser.Provider.Douyin.Services;

public sealed class DouyinLoginStatusChecker(HttpClient http)
{
    public async Task<string> CheckLoginStatusAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(MyParserRuntime.DouyinCookie))
        {
            return "未配置 Cookie";
        }

        var hasSession = MyParserRuntime.DouyinCookie.Contains("sessionid=", StringComparison.OrdinalIgnoreCase);
        var hasTtwid = MyParserRuntime.DouyinCookie.Contains("ttwid=", StringComparison.OrdinalIgnoreCase);
        var hasUifid = MyParserRuntime.DouyinCookie.Contains("UIFID=", StringComparison.OrdinalIgnoreCase)
                       || MyParserRuntime.DouyinCookie.Contains("UIFID_TEMP=", StringComparison.OrdinalIgnoreCase);
        if (!hasSession)
        {
            return hasUifid || hasTtwid
                ? $"游客 Cookie / 未登录; has_uifid={hasUifid}; has_ttwid={hasTtwid}; cookie_length={MyParserRuntime.DouyinCookie.Length}"
                : $"Cookie 格式可能无效：缺少 sessionid，且未找到 UIFID/UIFID_TEMP/ttwid; cookie_length={MyParserRuntime.DouyinCookie.Length}";
        }

        if (!hasTtwid)
        {
            return $"登录 Cookie 可能不完整：缺少 ttwid; has_uifid={hasUifid}; cookie_length={MyParserRuntime.DouyinCookie.Length}";
        }

        var url = "https://www.douyin.com/webcast/user/me/?aid=1128&t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyDefaultHeaders(request, "https://www.douyin.com/");
        request.Headers.TryAddWithoutValidation("Cookie", MyParserRuntime.DouyinCookie);

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"HTTP {(int)response.StatusCode}; cookie_length={MyParserRuntime.DouyinCookie.Length}";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var statusCode = GetInt(root, "status_code");
            var message = GetString(root, "message") ?? GetString(root, "status_msg") ?? string.Empty;
            var hasUser = TryGetProperty(root, "data", out var data) && data.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
            return statusCode == 0
                ? $"有效/已登录; status_code=0; has_data={hasUser}; cookie_length={MyParserRuntime.DouyinCookie.Length}"
                : $"可能失效/未登录; status_code={statusCode}; message={message}; cookie_length={MyParserRuntime.DouyinCookie.Length}";
        }
        catch (JsonException)
        {
            var sample = body.Length > 120 ? body[..120] : body;
            return $"返回非 JSON; cookie_length={MyParserRuntime.DouyinCookie.Length}; sample={sample.ReplaceLineEndings(" ")}";
        }
    }
}
