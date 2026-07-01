using System.Text;
using Avalonia.Media.Imaging;
using Shirobot.Plugin.MyParser.MessageHandling;
using Shirobot.Plugin.MyParser.Parsing;
using Shirobot.Plugin.MyParser.Services;
using Shirobot.Plugin.MyParser.Utility;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.MyParser;

internal sealed class ProviderHostServices(IBotContext context) : IProviderHostServices, IDisposable
{
    private LocalVideoHttpServer? _localVideoHttpServer;

    public Task ReactAsync(IncomingMessage message, string faceId, string platformName)
    {
        return ProviderMessageUtilities.ReactAsync(context, message, faceId, platformName);
    }

    public Task RemoveReactionAsync(IncomingMessage message, string faceId, string platformName)
    {
        return ProviderMessageUtilities.RemoveReactionAsync(context, message, faceId, platformName);
    }

    public Task<SendMessageResult> ReplyTextAsync(PluginConfig config, IncomingMessage message, string text)
    {
        return ProviderMessageUtilities.ReplyTextAsync(context, config, message, text);
    }

    public Task SendImageAsync(IncomingMessage message, ImageOutgoingSegment segment)
    {
        return ProviderMessageUtilities.SendImageAsync(context, message, segment);
    }

    public Task RunLoggedBackgroundAsync(string description, Func<Task> action)
    {
        return ProviderMessageUtilities.RunLoggedBackgroundAsync(description, action);
    }

    public string ResolveCookiePath(string fileName)
    {
        return ProviderMessageUtilities.ResolveCookiePath(context, fileName);
    }

    public Task<string> UploadLocalVideoFileAsync(PluginConfig config, IncomingMessage message, string? localVideoPath, string platformName, string mediaId)
    {
        return ProviderMessageUtilities.UploadLocalVideoFileAsync(context, config, message, localVideoPath, platformName, mediaId);
    }

    public Task<string> UploadLocalFileAsync(PluginConfig config, IncomingMessage message, string? localPath, string platformName, string mediaId, bool preferBase64 = false)
    {
        return ProviderMessageUtilities.UploadLocalFileAsync(context, config, message, localPath, platformName, mediaId, preferBase64);
    }

    public string GetMessageScene(IncomingMessage message) => ProviderMessageUtilities.GetMessageScene(message);

    public string GetUriMode(string uri) => MediaUriUtilities.GetUriMode(uri);

    public string PreviewUri(string? uri, int maxLength = 180) => MediaUriUtilities.PreviewUri(uri, maxLength);

    public string RegisterLocalVideoFile(string path) => GetLocalVideoHttpServer().RegisterFile(path);

    public void UnregisterLocalVideoFile(string? path) => _localVideoHttpServer?.UnregisterFile(path);

    public void DeleteLocalVideoIfConfigured(PluginConfig config, string? localPath, string provider)
    {
        LocalMediaCleanup.DeleteLocalVideoIfConfigured(config, localPath, provider);
    }

    public void CleanupStartupResidues(PluginConfig config)
    {
        LocalMediaCleanup.CleanupStartupResidues(config);
    }

    public HttpClient CreateImageHttpClient() => RemoteImageFetchService.CreateImageHttpClient();

    public Task<(string Uri, string? LocalPath)> BuildRemoteImageAsync(
        HttpClient http,
        string platformName,
        string? imageUrl,
        string? referer,
        string filePrefix,
        string localDirectory,
        Action<HttpRequestMessage>? configureRequest = null,
        long maxBytes = 10 * 1024L * 1024L)
    {
        return RemoteImageFetchService.BuildRemoteImageAsync(http, platformName, imageUrl, referer, filePrefix, localDirectory, configureRequest, maxBytes);
    }

    public Task<(string FileUri, string LocalPath)> DownloadProviderVideoAsync(
        PluginConfig config,
        ProviderVideoDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        return MyParserRuntime.GetOrAddVideoDownloadAsync(request.CacheKey, async () =>
        {
            var candidates = request.CandidateUrls.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToArray();
            if (candidates.Length == 0)
            {
                throw new InvalidOperationException($"{request.PlatformDisplayName} 没有可下载的视频地址。");
            }

            Exception? lastError = null;
            foreach (var url in candidates)
            {
                try
                {
                    return await DownloadProviderVideoCandidateAsync(config, request, url, cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException)
                {
                    lastError = ex;
                }
            }

            throw new InvalidOperationException($"{request.PlatformDisplayName} 视频下载失败：{lastError?.Message ?? "无可用地址"}");
        });
    }

    public Task<IReadOnlyList<TResult>> SelectParallelOrderedAsync<TSource, TResult>(
        IEnumerable<TSource> source,
        int maxConcurrency,
        Func<TSource, Task<TResult>> selector)
    {
        return MessageFetchConcurrency.SelectParallelOrderedAsync(source, maxConcurrency, selector);
    }

    public Bitmap? DecodeBase64ImageForRender(string uri) => RenderBitmapUtilities.DecodeBase64ImageForRender(uri);

    public Bitmap? DecodeImageFileForRender(string path) => RenderBitmapUtilities.DecodeImageFileForRender(path);

    public Task<long> DownloadAsync(
        HttpRangeDownloadRequest request,
        bool logProgress,
        int intervalSeconds,
        string logPrefix,
        string identifierName,
        CancellationToken cancellationToken = default)
    {
        var downloader = new Downloader(new HttpClient(), new DownloadProgressLogger(logProgress, intervalSeconds, logPrefix, identifierName));
        return downloader.DownloadAsync(request, cancellationToken);
    }

    public Task<(long? ContentLength, bool AcceptRanges)> ProbeDownloadAsync(
        HttpRangeDownloadRequest request,
        bool logProgress,
        int intervalSeconds,
        string logPrefix,
        string identifierName,
        CancellationToken cancellationToken = default)
    {
        var downloader = new Downloader(new HttpClient(), new DownloadProgressLogger(logProgress, intervalSeconds, logPrefix, identifierName));
        return downloader.ProbeAsync(request, cancellationToken);
    }

    private async Task<(string FileUri, string LocalPath)> DownloadProviderVideoCandidateAsync(
        PluginConfig config,
        ProviderVideoDownloadRequest request,
        string url,
        CancellationToken cancellationToken)
    {
        var maxBytes = config.MaxVideoDownloadMegabytes <= 0
            ? long.MaxValue
            : config.MaxVideoDownloadMegabytes * 1024L * 1024L;
        var dir = ResolveDownloadDirectory(request.DownloadDirectory, request.PlatformId);
        Directory.CreateDirectory(dir);
        var extension = string.IsNullOrWhiteSpace(request.FileExtension) ? "mp4" : request.FileExtension.Trim('.');
        var path = Path.Combine(dir, $"{request.FileNamePrefix}_{SanitizeFileName(request.MediaId)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{extension}");
        var segmentCount = Math.Clamp(config.ParallelDownloadThreads, 1, 64);
        var downloadRequest = new HttpRangeDownloadRequest(
            url,
            path,
            request.MediaId,
            maxBytes,
            true,
            1,
            segmentCount,
            (method, range) => request.CreateRequest(method, url, range),
            statusCode => new InvalidOperationException($"HTTP {(int)statusCode}"),
            bytes => new InvalidOperationException($"视频文件过大：{bytes / 1024 / 1024}MB > {config.MaxVideoDownloadMegabytes}MB"),
            () => new InvalidOperationException($"视频文件超过限制：{config.MaxVideoDownloadMegabytes}MB"),
            (index, statusCode) => new InvalidOperationException($"分片 {index} 不支持 Range：HTTP {(int)statusCode}"),
            (index, contentRange) => new InvalidOperationException($"分片 {index} Content-Range 不匹配：{contentRange}"),
            (index, copied, expected) => new InvalidOperationException($"分片 {index} 大小不匹配：{copied} != {expected}"),
            (total, expected) => new InvalidOperationException($"分片合并大小不一致：{total} != {expected}"),
            ex => BotLog.Warning($"MyParser {request.PlatformDisplayName} 下载进度: {request.IdentifierName}={request.MediaId}, 并发下载失败，回退普通下载：{ex.Message}"));

        var total = await DownloadAsync(downloadRequest, config.LogDownloadProgress, 2, "MyParser", request.IdentifierName, cancellationToken);
        if (total == 0)
        {
            throw new InvalidDataException("下载到空文件");
        }

        await ValidateDownloadedVideoAsync(path, total, request.ValidationKind, cancellationToken);
        return (new Uri(path).AbsoluteUri, path);
    }

    private static string ResolveDownloadDirectory(string configuredDirectory, string platformId)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser", platformId);
        }

        return Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.Combine(AppContext.BaseDirectory, configuredDirectory);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value;
    }

    private static async Task ValidateDownloadedVideoAsync(
        string path,
        long totalBytes,
        ProviderVideoValidationKind validationKind,
        CancellationToken cancellationToken)
    {
        if (validationKind == ProviderVideoValidationKind.None)
        {
            return;
        }

        if (totalBytes < 1024)
        {
            throw new InvalidDataException($"下载文件过小：{totalBytes} bytes");
        }

        await using var file = File.OpenRead(path);
        var header = new byte[Math.Min(4096, (int)Math.Min(file.Length, 4096))];
        var read = await file.ReadAsync(header, cancellationToken);
        var ascii = Encoding.ASCII.GetString(header, 0, read);
        var isMp4 = ascii.Contains("ftyp", StringComparison.Ordinal);
        var isWebM = ascii.Contains("webm", StringComparison.OrdinalIgnoreCase);
        var valid = validationKind switch
        {
            ProviderVideoValidationKind.Mp4 => isMp4,
            ProviderVideoValidationKind.Mp4OrWebM => isMp4 || isWebM,
            _ => true,
        };

        if (valid)
        {
            return;
        }

        var sample = ascii.ReplaceLineEndings(" ");
        if (sample.Length > 120)
        {
            sample = sample[..120];
        }

        throw new InvalidDataException($"下载文件不像视频，可能是风控页或错误内容：{sample}");
    }

    private LocalVideoHttpServer GetLocalVideoHttpServer()
    {
        return _localVideoHttpServer ??= new LocalVideoHttpServer();
    }

    public void Dispose()
    {
        _localVideoHttpServer?.Dispose();
        _localVideoHttpServer = null;
    }
}
