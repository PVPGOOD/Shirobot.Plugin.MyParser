using System.Net;
using Shirobot.Plugin.MyParser.Utility;
using ShiroBot.SDK.Abstractions;

namespace Shirobot.Plugin.MyParser.Providers.Common.MessageHandling;

internal static class RemoteImageFetchService
{
    private const long DefaultMaxImageBytes = 10 * 1024L * 1024L;

    public static async Task<(string Uri, string? LocalPath)> BuildRemoteImageAsync(
        HttpClient http,
        string platformName,
        string? imageUrl,
        string? referer,
        string filePrefix,
        string localDirectory,
        Action<HttpRequestMessage>? configureRequest = null,
        long maxBytes = DefaultMaxImageBytes)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return (string.Empty, null);
        }

        try
        {
            BotLog.Info($"MyParser {platformName} 图片下载开始: prefix={filePrefix}, source_url={imageUrl}, referer={referer}");
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            configureRequest?.Invoke(request);

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > 0 && contentLength > maxBytes)
            {
                BotLog.Warning($"MyParser {platformName} 图片过大，回退原始 URL: url={imageUrl}, image_mb={contentLength.Value / 1024d / 1024d:F2}, limit_mb={maxBytes / 1024d / 1024d:F0}");
                return (imageUrl, null);
            }

            await using var input = await response.Content.ReadAsStreamAsync();
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBytes)
                {
                    BotLog.Warning($"MyParser {platformName} 图片下载超过限制，回退原始 URL: url={imageUrl}, limit_mb={maxBytes / 1024d / 1024d:F0}");
                    return (imageUrl, null);
                }

                output.Write(buffer, 0, read);
            }

            var bytes = output.ToArray();
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var extension = MediaUriUtilities.GuessImageExtension(contentType, imageUrl, bytes);
            Directory.CreateDirectory(localDirectory);
            var localPath = Path.Combine(localDirectory, $"{SanitizeLocalFileName(filePrefix)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{extension}");
            await File.WriteAllBytesAsync(localPath, bytes);

            var uri = "base64://" + Convert.ToBase64String(bytes);
            BotLog.Info($"MyParser {platformName} 图片下载完成: source_url={imageUrl}, content_type={contentType}, bytes={total}, local_path={localPath}");
            return (uri, localPath);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser {platformName} 图片转 base64/本地文件失败，回退原始 URL: url={imageUrl}, error={ex.Message}");
            return (imageUrl, null);
        }
    }

    public static string SanitizeLocalFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value;
    }

    public static HttpClient CreateImageHttpClient()
    {
        return new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
        });
    }
}
