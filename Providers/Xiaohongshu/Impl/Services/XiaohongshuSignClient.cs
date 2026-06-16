using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Models;

namespace Shirobot.Plugin.MyParser.Providers.Xiaohongshu.Impl.Services;

internal sealed class XiaohongshuSignClient(MyParserConfig config, HttpClient http)
{
    public async Task<IReadOnlyDictionary<string, string>> SignAsync(
        string method,
        string uri,
        string cookies,
        IReadOnlyDictionary<string, object?>? payload = null,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.XiaohongshuSignServerUrl))
        {
            throw new XiaohongshuSignRequiredException("小红书解析需要 xhshow sign 服务。请自行搭建参考项目 xhshow-sign，并在运行时配置 XiaohongshuSignServerUrl / XiaohongshuSignServerToken。");
        }

        if (string.IsNullOrWhiteSpace(config.XiaohongshuSignServerToken))
        {
            throw new XiaohongshuSignRequiredException("小红书 xhshow sign 服务需要 token。请在运行时配置 XiaohongshuSignServerToken；不要提交到默认配置或仓库。");
        }

        var body = new SignRequest
        {
            Token = config.XiaohongshuSignServerToken,
            Method = method.ToUpperInvariant(),
            Uri = uri,
            Cookies = cookies,
            Payload = payload,
            Params = parameters,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, config.XiaohongshuSignServerUrl.Trim())
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        request.Headers.TryAddWithoutValidation("X-Sign-Token", config.XiaohongshuSignServerToken);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + config.XiaohongshuSignServerToken);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new XiaohongshuSignRequiredException($"xhshow sign 服务请求失败：HTTP {(int)response.StatusCode} {Trim(text, 240)}");
        }

        using var json = JsonDocument.Parse(text);
        if (json.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            throw new XiaohongshuSignRequiredException("xhshow sign 服务返回失败：" + Trim(text, 240));
        }

        var headersElement = json.RootElement.TryGetProperty("headers", out var headers)
            ? headers
            : json.RootElement;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in headersElement.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String && item.Name.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
            {
                result[item.Name] = item.Value.GetString() ?? string.Empty;
            }
        }

        if (!result.ContainsKey("x-s") || !result.ContainsKey("x-s-common"))
        {
            throw new XiaohongshuSignRequiredException("xhshow sign 服务没有返回 x-s/x-s-common。请检查服务版本和 token。");
        }

        return result;
    }

    private static string Trim(string value, int max)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= max ? value : value[..max] + "…";
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class SignRequest
    {
        [JsonPropertyName("token")]
        public string Token { get; init; } = string.Empty;

        [JsonPropertyName("method")]
        public string Method { get; init; } = string.Empty;

        [JsonPropertyName("uri")]
        public string Uri { get; init; } = string.Empty;

        [JsonPropertyName("cookies")]
        public string Cookies { get; init; } = string.Empty;

        [JsonPropertyName("payload")]
        public IReadOnlyDictionary<string, object?>? Payload { get; init; }

        [JsonPropertyName("params")]
        public IReadOnlyDictionary<string, object?>? Params { get; init; }
    }
}
