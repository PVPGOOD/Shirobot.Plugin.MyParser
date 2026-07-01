using MyParser.Provider.Douyin.Infrastructure;
using MyParser.Provider.Douyin.Models;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ShiroBot.SDK.Abstractions;
using MyParser.Provider.Douyin.Abstractions;
using static MyParser.Provider.Douyin.Utilities.DouyinAwemeExtractor;
using static MyParser.Provider.Douyin.Utilities.DouyinCoverSelector;
using static MyParser.Provider.Douyin.Infrastructure.DouyinRequestHeaders;
using static MyParser.Provider.Douyin.Utilities.DouyinParseHelpers;
using static MyParser.Provider.Douyin.Utilities.DouyinQueryBuilder;
using static MyParser.Provider.Douyin.Utilities.DouyinUrlParser;

namespace MyParser.Provider.Douyin.Services;

public sealed class DouyinParseService(HttpClient http, IReadOnlyList<IDouyinWorkParser> workParsers)
{
    public async Task<DouyinParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        var inputUrl = ExtractDouyinUrl(text) ?? throw new DouyinParseException("未检测到抖音链接。请发送 v.douyin.com 或 douyin.com 链接。");
        var resolvedUrl = await ResolveUrlAsync(inputUrl, cancellationToken);
        if (IsLiveUrl(resolvedUrl))
        {
            return DouyinParseResult.IgnoredLive(resolvedUrl);
        }

        var awemeId = ExtractAwemeId(resolvedUrl) ?? throw new DouyinParseException("未能从链接中提取作品 ID。可能不是公开视频/图集链接。");

        using var detail = await FetchAwemeDetailAsync(awemeId, resolvedUrl, cancellationToken);
        var result = ParseAwemeDetail(detail, awemeId, resolvedUrl);
        result = await TryApplyUserProfileAsync(result, cancellationToken);
        result = await TryApplyPublishCoverAsync(result, cancellationToken);
        return await TryApplySearchCoverAsync(result, cancellationToken);
    }

    private static bool IsLiveUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "live.douyin.com", StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Host, "webcast.amemv.com", StringComparison.OrdinalIgnoreCase)
               || uri.AbsolutePath.Contains("/webcast/", StringComparison.OrdinalIgnoreCase)
               || uri.AbsolutePath.Contains("/douyin/webcast/", StringComparison.OrdinalIgnoreCase)
               || uri.Query.Contains("enter_from=live", StringComparison.OrdinalIgnoreCase)
               || uri.Query.Contains("share_previous_page=live", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveUrlAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyDefaultHeaders(request, DouyinConstants.HomeUrl);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null)
        {
            return MakeAbsolute(response.Headers.Location, new Uri(url)).ToString();
        }

        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
        if (ExtractAwemeId(finalUrl) is not null)
        {
            return finalUrl;
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var id = ExtractAwemeId(html);
        return id is null ? finalUrl : $"https://www.douyin.com/video/{id}";
    }

    private async Task<JsonDocument> FetchAwemeDetailAsync(string awemeId, string originalUrl, CancellationToken cancellationToken)
    {
        var referer = originalUrl.Contains("/note/", StringComparison.OrdinalIgnoreCase)
            ? $"https://www.douyin.com/note/{awemeId}"
            : $"https://www.douyin.com/video/{awemeId}";

        var uifid = TryGetCookieValue("UIFID") ?? TryGetCookieValue("UIFID_TEMP");
        var msToken = TryGetCookieValue("msToken");
        var verifyFp = TryGetCookieValue("s_v_web_id") ?? TryGetCookieValue("verifyFp") ?? TryGetCookieValue("fp");
        if (string.IsNullOrWhiteSpace(uifid))
        {
            BotLog.Warning("MyParser 抖音 Cookie 缺少 UIFID/UIFID_TEMP，无法生成 x-secsdk-web-signature，详情接口会被判定为 anonymous 强制登录。请从已打开抖音页面的浏览器 Cookie 中复制完整 DouyinCookie。");
        }
        else if (string.IsNullOrWhiteSpace(msToken) || string.IsNullOrWhiteSpace(verifyFp))
        {
            BotLog.Warning($"MyParser 抖音游客 Cookie 安全态不完整: has_uifid=True, has_msToken={!string.IsNullOrWhiteSpace(msToken)}, has_verifyFp={!string.IsNullOrWhiteSpace(verifyFp)}。可能触发 CUSTOM_强登_模型，请从 Network 请求 Cookie 中复制包含 msToken 和 s_v_web_id/verifyFp 的完整值。");
        }

        var query = BuildHjDetailQuery(
            awemeId,
            msToken,
            await GetWebIdAsync(referer, cancellationToken),
            uifid,
            verifyFp);
        var unsignedUrl = "https://www.douyin.com/aweme/v1/web/aweme/detail/?" + query;
        var aBogus = ABogusSigner.Generate(query, DouyinConstants.UserAgent);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedUrl = unsignedUrl + "&a_bogus=" + Uri.EscapeDataString(aBogus) + "&timestamp=" + timestamp;
        signedUrl = WebSecSdkSigner.SignUrl(signedUrl, uifid);
        var hjSignedUrl = signedUrl.Replace("https://www.douyin.com/", "https://www-hj.douyin.com/", StringComparison.OrdinalIgnoreCase);

        var doc = await TryGetAwemeDetailJsonAsync(signedUrl, referer, awemeId, "detail-video", cancellationToken, uifid);
        if (doc is not null)
        {
            return doc;
        }

        doc = await TryGetAwemeDetailJsonAsync(hjSignedUrl, referer, awemeId, "detail-video-hj", cancellationToken, uifid);
        if (doc is not null)
        {
            return doc;
        }

        if (!referer.Contains("/note/", StringComparison.OrdinalIgnoreCase))
        {
            var noteReferer = $"https://www.douyin.com/note/{awemeId}";
            doc = await TryGetAwemeDetailJsonAsync(signedUrl, noteReferer, awemeId, "detail-note", cancellationToken, uifid);
            if (doc is not null)
            {
                return doc;
            }

            doc = await TryGetAwemeDetailJsonAsync(hjSignedUrl, noteReferer, awemeId, "detail-note-hj", cancellationToken, uifid);
            if (doc is not null)
            {
                return doc;
            }
        }

        try
        {
            var ssrDoc = await FetchSharePageDataAsync(awemeId, cancellationToken);
            if (TryGetAwemeDetail(ssrDoc.RootElement, out _))
            {
                return ssrDoc;
            }

            ssrDoc.Dispose();
        }
        catch (DouyinParseException ex)
        {
            BotLog.Warning($"MyParser 抖音分享页备用解析失败: aweme_id={awemeId}, error={ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(uifid))
        {
            throw new DouyinParseException("DouyinCookie 缺少 UIFID 或 UIFID_TEMP，无法生成 x-secsdk-web-signature，抖音详情接口返回强制登录。请在浏览器打开抖音页面后复制完整 Cookie（至少包含 UIFID/UIFID_TEMP、msToken、s_v_web_id）再重试。");
        }

        throw new DouyinParseException("抖音详情接口和分享页均未返回作品数据。请检查 Cookie 是否有效，或稍后重试。");
    }

    private async Task<JsonDocument?> TryGetAwemeDetailJsonAsync(string signedUrl, string referer, string awemeId, string source, CancellationToken cancellationToken, string? uifid)
    {
        try
        {
            var doc = await GetJsonAsync(signedUrl, referer, cancellationToken, uifid);
            if (TryGetAwemeDetail(doc.RootElement, out _))
            {
                return doc;
            }

            BotLog.Warning($"MyParser 抖音详情接口响应缺少作品数据: aweme_id={awemeId}, source={source}");
            doc.Dispose();
        }
        catch (DouyinParseException ex)
        {
            BotLog.Warning($"MyParser 抖音详情接口失败，尝试备用解析: aweme_id={awemeId}, source={source}, error={ex.Message}");
        }

        return null;
    }

    private async Task<JsonDocument> FetchSharePageDataAsync(string awemeId, CancellationToken cancellationToken)
    {
        var (_, doc) = await FetchSharePageHtmlAndDataAsync(awemeId, cancellationToken);
        return doc;
    }

    private async Task<(string Html, JsonDocument Doc)> FetchSharePageHtmlAndDataAsync(string awemeId, CancellationToken cancellationToken)
    {
        var shareUrl = $"https://www.iesdouyin.com/share/video/{awemeId}/";
        using var request = new HttpRequestMessage(HttpMethod.Get, shareUrl);
        ApplySharePageHeaders(request);

        using var response = await http.SendAsync(request, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new DouyinParseException($"抖音分享页请求失败：HTTP {(int)response.StatusCode}");
        }

        var match = Regex.Match(html, @"window\._ROUTER_DATA\s*=\s*(.*?)</script>", RegexOptions.Singleline);
        if (!match.Success)
        {
            throw new DouyinParseException("抖音分享页未找到 ROUTER_DATA。 ");
        }

        return (html, JsonDocument.Parse(match.Groups[1].Value.Trim()));
    }

    private async Task<DouyinParseResult> TryApplyUserProfileAsync(DouyinParseResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.AuthorId) || !result.AuthorId.StartsWith("MS4w", StringComparison.Ordinal))
        {
            return result;
        }

        try
        {
            var query = BuildUserProfileQuery(result.AuthorId);
            var unsignedUrl = "https://www.douyin.com/aweme/v1/web/user/profile/other/?" + query;
            var aBogus = ABogusSigner.Generate(query, DouyinConstants.UserAgent);
            var signedUrl = unsignedUrl + "&a_bogus=" + Uri.EscapeDataString(aBogus);
            using var doc = await GetJsonAsync(signedUrl, "https://www.douyin.com/user/" + Uri.EscapeDataString(result.AuthorId), cancellationToken);
            var root = doc.RootElement;
            if (GetInt(root, "status_code") != 0 || !TryGetProperty(root, "user", out var user))
            {
                return result;
            }

            var followerCount = GetLong(user, "follower_count");
            var region = GetString(user, "ip_location") ?? GetString(user, "region");
            var avatar = ExtractAuthorAvatarUrl(user);
            BotLog.Info($"MyParser 抖音作者资料补全: aweme_id={result.AwemeId}, follower={followerCount}, region={region ?? ""}, avatar={(string.IsNullOrWhiteSpace(avatar) ? "" : "ok")}");
            return result with
            {
                AuthorFollowerCount = followerCount > 0 ? followerCount : result.AuthorFollowerCount,
                AuthorRegion = string.IsNullOrWhiteSpace(region) ? result.AuthorRegion : region,
                AuthorAvatarUrl = string.IsNullOrWhiteSpace(avatar) ? result.AuthorAvatarUrl : avatar,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or DouyinParseException or TaskCanceledException)
        {
            BotLog.Warning($"MyParser 抖音作者资料补全失败: aweme_id={result.AwemeId}, error={ex.Message}");
            return result;
        }
    }

    private async Task<DouyinParseResult> TryApplyPublishCoverAsync(DouyinParseResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.AuthorId) || !result.AuthorId.StartsWith("MS4w", StringComparison.Ordinal))
        {
            BotLog.Info($"MyParser 抖音发布列表封面跳过: aweme_id={result.AwemeId}, reason=missing_sec_uid");
            return result;
        }

        try
        {
            var publishCover = await TryFetchPublishCoverUrlAsync(result.AuthorId, result.AwemeId, cancellationToken);
            if (string.IsNullOrWhiteSpace(publishCover))
            {
                BotLog.Info($"MyParser 抖音发布列表封面未命中: aweme_id={result.AwemeId}, sec_uid={result.AuthorId}");
                return result;
            }

            if (string.Equals(publishCover, result.CoverUrl, StringComparison.Ordinal))
            {
                BotLog.Info($"MyParser 抖音发布列表封面已是当前封面: aweme_id={result.AwemeId}, url={publishCover}");
                return result;
            }

            if (!string.IsNullOrWhiteSpace(result.CoverUrl))
            {
                BotLog.Info($"MyParser 抖音发布列表封面命中但保留详情封面: aweme_id={result.AwemeId}, keep={result.CoverUrl}, publish={publishCover}");
                return result with { CoverSource = "detail" };
            }

            BotLog.Info($"MyParser 抖音详情封面为空，使用发布列表封面: aweme_id={result.AwemeId}, new={publishCover}");
            return result with { CoverUrl = publishCover, CoverSource = "publish" };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or DouyinParseException or TaskCanceledException)
        {
            BotLog.Warning($"MyParser 抖音发布列表封面获取失败: aweme_id={result.AwemeId}, error={ex.Message}");
            return result;
        }
    }

    private async Task<DouyinParseResult> TryApplySearchCoverAsync(DouyinParseResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.Title))
        {
            return result;
        }

        if (!string.IsNullOrWhiteSpace(result.CoverSource) && result.CoverSource.StartsWith("publish", StringComparison.Ordinal))
        {
            BotLog.Info($"MyParser 抖音默认使用发布列表封面: aweme_id={result.AwemeId}, source={result.CoverSource}, skip_search_override=true, url={result.CoverUrl}");
            return result;
        }

        try
        {
            var searchCover = await TryFetchSearchCoverUrlAsync(result.Title, result.AwemeId, cancellationToken);
            if (string.IsNullOrWhiteSpace(searchCover))
            {
                BotLog.Info($"MyParser 抖音搜索封面未命中: aweme_id={result.AwemeId}");
                return result;
            }

            if (CoverUrlScore(searchCover) <= CoverUrlScore(result.CoverUrl))
            {
                BotLog.Info($"MyParser 抖音搜索封面未优于当前封面: aweme_id={result.AwemeId}, current_score={CoverUrlScore(result.CoverUrl)}, search_score={CoverUrlScore(searchCover)}");
                return result;
            }

            BotLog.Info($"MyParser 抖音封面使用搜索高清图: aweme_id={result.AwemeId}, old={result.CoverUrl}, new={searchCover}");
            return result with { CoverUrl = searchCover, CoverSource = "search" };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or DouyinParseException or TaskCanceledException)
        {
            BotLog.Warning($"MyParser 抖音搜索封面获取失败: aweme_id={result.AwemeId}, error={ex.Message}");
            return result;
        }
    }

    private static string? TryGetCookieValue(string name)
    {
        if (string.IsNullOrWhiteSpace(MyParserRuntime.DouyinCookie))
        {
            return null;
        }

        foreach (var part in MyParserRuntime.DouyinCookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            if (string.Equals(part[..index].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return part[(index + 1)..].Trim();
            }
        }

        return null;
    }

    private async Task<string?> GetWebIdAsync(string referer, CancellationToken cancellationToken)
    {
        var cookieWebId = TryGetCookieValue("webid") ?? TryGetCookieValue("ttwid");
        if (!string.IsNullOrWhiteSpace(cookieWebId) && cookieWebId.All(char.IsDigit))
        {
            return cookieWebId;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://mcs.zijieapi.com/webid?aid=6383&sdk_version=5.1.24_dy");
            request.Headers.TryAddWithoutValidation("User-Agent", DouyinConstants.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            request.Headers.TryAddWithoutValidation("Referer", DouyinConstants.HomeUrl);
            request.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"146\", \"Not-A.Brand\";v=\"24\", \"Google Chrome\";v=\"146\"");
            request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                app_id = 6383,
                url = referer,
                user_agent = DouyinConstants.UserAgent,
                referer = string.Empty,
                user_unique_id = string.Empty,
            }), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (TryGetProperty(doc.RootElement, "web_id", out var webId) && webId.ValueKind == JsonValueKind.String)
            {
                var value = webId.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    BotLog.Info($"MyParser 抖音 webid 获取成功: webid={value}");
                    return value;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            BotLog.Warning($"MyParser 抖音 webid 获取失败: error={ex.Message}");
        }

        return null;
    }

    private async Task<string?> TryFetchSearchCoverUrlAsync(string title, string awemeId, CancellationToken cancellationToken)
    {
        var keyword = BuildSearchKeyword(title);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        var query = BuildSearchItemQuery(keyword);
        var unsignedUrl = "https://www.douyin.com/aweme/v1/web/search/item/?" + query;
        var aBogus = ABogusSigner.Generate(query, DouyinConstants.UserAgent);
        var signedUrl = unsignedUrl + "&a_bogus=" + Uri.EscapeDataString(aBogus);

        using var doc = await GetJsonAsync(signedUrl, "https://www.douyin.com/search/" + Uri.EscapeDataString(keyword) + "?type=video", cancellationToken);
        var root = doc.RootElement;
        var statusCode = GetInt(root, "status_code");
        if (statusCode != 0)
        {
            BotLog.Warning($"MyParser 抖音搜索封面接口异常: aweme_id={awemeId}, status_code={statusCode}, status_msg={GetString(root, "status_msg") ?? GetString(root, "message") ?? string.Empty}");
            return null;
        }

        if (!TryGetProperty(root, "data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (!TryGetProperty(item, "aweme_info", out var aweme) || aweme.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!string.Equals(GetString(aweme, "aweme_id"), awemeId, StringComparison.Ordinal))
            {
                continue;
            }

            var cover = ExtractSearchCoverUrl(aweme);
            BotLog.Info($"MyParser 抖音搜索封面命中: aweme_id={awemeId}, keyword={keyword}, cover={cover ?? string.Empty}");
            return cover;
        }

        return null;
    }

    private async Task<string?> TryFetchPublishCoverUrlAsync(string secUserId, string awemeId, CancellationToken cancellationToken)
    {
        long maxCursor = 0;
        for (var page = 1; page <= 3; page++)
        {
            var query = BuildUserPostQuery(secUserId, awemeId, maxCursor);
            var unsignedUrl = "https://www.douyin.com/aweme/v1/web/aweme/post/?" + query;
            var aBogus = ABogusSigner.Generate(query, DouyinConstants.UserAgent);
            var signedUrl = unsignedUrl + "&a_bogus=" + Uri.EscapeDataString(aBogus);
            var referer = $"https://www.douyin.com/user/{Uri.EscapeDataString(secUserId)}?vid={Uri.EscapeDataString(awemeId)}";

            using var doc = await GetJsonAsync(signedUrl, referer, cancellationToken);
            var root = doc.RootElement;
            var statusCode = GetInt(root, "status_code");
            if (statusCode != 0)
            {
                BotLog.Warning($"MyParser 抖音发布列表封面接口异常: aweme_id={awemeId}, page={page}, status_code={statusCode}, status_msg={GetString(root, "status_msg") ?? GetString(root, "message") ?? string.Empty}");
                return null;
            }

            if (TryGetProperty(root, "aweme_list", out var awemeList) && awemeList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in awemeList.EnumerateArray())
                {
                    var itemId = GetString(item, "aweme_id");
                    if (!string.Equals(itemId, awemeId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var publishCover = ExtractPublishCoverUrl(item);
                    BotLog.Info($"MyParser 抖音发布列表封面命中: aweme_id={awemeId}, page={page}, cover={publishCover ?? string.Empty}");
                    return publishCover;
                }
            }

            var hasMore = GetBool(root, "has_more");
            var nextCursor = GetLong(root, "max_cursor");
            if (!hasMore || nextCursor <= 0 || nextCursor == maxCursor)
            {
                break;
            }

            maxCursor = nextCursor;
        }

        return null;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, string referer, CancellationToken cancellationToken, string? uifid = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyDefaultHeaders(request, referer);
        if (!string.IsNullOrWhiteSpace(uifid))
        {
            request.Headers.TryAddWithoutValidation("uifid", uifid);
        }

        if (!string.IsNullOrWhiteSpace(MyParserRuntime.DouyinCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", MyParserRuntime.DouyinCookie);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new DouyinParseException($"抖音接口请求失败：HTTP {(int)response.StatusCode}");
        }

        var contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
        var whaleAbortData = TryGetHeaderValue(response, "x-whale-throughput-abort-data");
        var whaleAbortText = TryDecodeBase64Utf8(whaleAbortData);
        if (body.Length == 0)
        {
            LogNonJsonResponse(url, referer, response, contentType, whaleAbortText, whaleAbortData, body);
            if (IsForceLoginAbort(whaleAbortText) || IsForceLoginAbort(whaleAbortData))
            {
                throw new DouyinParseException(BuildForceLoginMessage());
            }

            throw new DouyinParseException("抖音接口返回空响应，可能被风控拦截。请配置或更新有效 DouyinCookie 后重试。 ");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            LogNonJsonResponse(url, referer, response, contentType, whaleAbortText, whaleAbortData, body);

            if (IsForceLoginAbort(whaleAbortText) || IsForceLoginAbort(whaleAbortData))
            {
                throw new DouyinParseException(BuildForceLoginMessage());
            }

            throw new DouyinParseException("抖音接口返回的不是有效 JSON：" + ex.Message);
        }
    }

    private static void LogNonJsonResponse(string url, string referer, HttpResponseMessage response, string contentType, string? whaleAbortText, string? whaleAbortData, string body)
    {
        var safeBody = body.Length <= 2048 ? body.ReplaceLineEndings(" ") : body[..2048].ReplaceLineEndings(" ") + "...(truncated)";
        BotLog.Warning(
            "MyParser 抖音接口返回非 JSON rawdata: "
            + $"url={url}, referer={referer}, http={(int)response.StatusCode}, content_type={contentType}, body_length={body.Length}, whale_abort={whaleAbortText ?? whaleAbortData ?? string.Empty}, rawdata={safeBody}");
    }

    private static string? TryGetHeaderValue(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    private static string? TryDecodeBase64Utf8(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsForceLoginAbort(string? whaleAbortText)
    {
        return !string.IsNullOrWhiteSpace(whaleAbortText)
               && whaleAbortText.Contains("anonymous", StringComparison.OrdinalIgnoreCase)
               && (whaleAbortText.Contains("\"id\":53", StringComparison.OrdinalIgnoreCase)
                   || whaleAbortText.Contains("\"id\": 53", StringComparison.OrdinalIgnoreCase)
                   || whaleAbortText.Contains("\"id\":296", StringComparison.OrdinalIgnoreCase)
                   || whaleAbortText.Contains("\"id\": 296", StringComparison.OrdinalIgnoreCase)
                   || whaleAbortText.Contains("强制登录", StringComparison.OrdinalIgnoreCase)
                   || whaleAbortText.Contains("强登", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildForceLoginMessage()
    {
        var hasUifid = TryGetCookieValue("UIFID") is not null || TryGetCookieValue("UIFID_TEMP") is not null;
        var hasMsToken = TryGetCookieValue("msToken") is not null;
        var hasVerifyFp = TryGetCookieValue("s_v_web_id") is not null || TryGetCookieValue("verifyFp") is not null || TryGetCookieValue("fp") is not null;
        if (hasUifid && (!hasMsToken || !hasVerifyFp))
        {
            return $"抖音接口触发强制登录/风控模型。当前 DouyinCookie 是游客 Cookie 但安全态不完整：has_msToken={hasMsToken}, has_s_v_web_id_or_verifyFp={hasVerifyFp}。请从浏览器 Network 请求头复制完整 Cookie（至少包含 UIFID/UIFID_TEMP、msToken、s_v_web_id/verifyFp）后重试。";
        }

        return "抖音接口要求登录后才能解析（服务端返回强制登录/风控模型）。请配置或更新有效 DouyinCookie 后重试。";
    }

    private DouyinParseResult ParseAwemeDetail(JsonDocument doc, string fallbackAwemeId, string sourceUrl)
    {
        if (!TryGetAwemeDetail(doc.RootElement, out var aweme))
        {
            throw new DouyinParseException("响应中缺少 aweme_detail。 ");
        }

        var parser = workParsers.FirstOrDefault(i => i.CanParse(aweme))
            ?? throw new DouyinParseException("暂不支持的抖音作品类型。");
        var result = parser.Parse(aweme, fallbackAwemeId, sourceUrl);
        BotLog.Info($"MyParser 抖音作品类型解析: aweme_id={result.AwemeId}, parser={parser.GetType().Name}, is_video={result.IsVideo}, is_gallery={result.IsGallery}");
        return result;
    }

    private static string BuildSearchKeyword(string title)
    {
        var keyword = Regex.Replace(title.ReplaceLineEndings(" "), @"#\S+", " ").Trim();
        keyword = Regex.Replace(keyword, @"\s+", " ");
        return keyword.Length <= 80 ? keyword : keyword[..80];
    }
}
