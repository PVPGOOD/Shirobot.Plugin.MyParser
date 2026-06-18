using System.Diagnostics;
using System.Net;
using Downloader;

namespace Shirobot.Plugin.MyParser.Services;

internal sealed class Downloader(HttpClient http, DownloadProgressLogger progressLogger)
{
    public async Task<long> DownloadAsync(HttpRangeDownloadRequest request, CancellationToken cancellationToken = default)
    {
        var probe = await ProbeAsync(request, cancellationToken);
        if (request.MaxBytes != long.MaxValue && probe.ContentLength is > 0 && probe.ContentLength > request.MaxBytes)
        {
            throw request.CreateTooLargeException(probe.ContentLength.Value);
        }

        var segmentCount = Math.Clamp(request.SegmentCount, 1, 64);
        var mode = probe.AcceptRanges && segmentCount > 1 ? $"downloader/parallel/{segmentCount}" : "downloader/stream";
        var stopwatch = Stopwatch.StartNew();
        progressLogger.LogStart(request.MediaId, request.Path, probe.ContentLength, mode);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Path)) ?? AppContext.BaseDirectory);
        TryDelete(request.Path);
        TryDelete(request.Path + ".download");

        Exception? completedError = null;
        Exception? tooLargeError = null;
        var nextLogAtTicks = 0L;
        await using var downloader = new DownloadService(CreateDownloaderConfiguration(request, segmentCount, probe.AcceptRanges));

        downloader.DownloadProgressChanged += (_, e) =>
        {
            if (request.MaxBytes != long.MaxValue && e.ReceivedBytesSize > request.MaxBytes && tooLargeError is null)
            {
                tooLargeError = request.CreateExceededLimitException();
                downloader.CancelAsync();
                return;
            }

            progressLogger.LogProgressThreadSafe(
                mode,
                request.MediaId,
                e.ReceivedBytesSize,
                e.TotalBytesToReceive > 0 ? e.TotalBytesToReceive : probe.ContentLength,
                stopwatch.Elapsed,
                ref nextLogAtTicks);
        };

        downloader.DownloadFileCompleted += (_, e) =>
        {
            if (e.Error is not null)
            {
                completedError = e.Error;
            }
            else if (e.Cancelled && tooLargeError is null && !cancellationToken.IsCancellationRequested)
            {
                completedError = new OperationCanceledException("下载已取消。", cancellationToken);
            }
        };

        try
        {
            await downloader.DownloadFileTaskAsync(request.Url, request.Path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupFailedDownload(request.Path);
            throw;
        }

        if (tooLargeError is not null)
        {
            CleanupFailedDownload(request.Path);
            throw tooLargeError;
        }

        if (completedError is not null)
        {
            CleanupFailedDownload(request.Path);
            throw new IOException($"下载失败：{completedError.Message}", completedError);
        }

        if (!File.Exists(request.Path))
        {
            CleanupFailedDownload(request.Path);
            throw new IOException("下载失败：未生成目标文件。Downloader 未报告错误，但目标文件不存在。 ");
        }

        var total = new FileInfo(request.Path).Length;
        if (request.MaxBytes != long.MaxValue && total > request.MaxBytes)
        {
            CleanupFailedDownload(request.Path);
            throw request.CreateExceededLimitException();
        }

        progressLogger.LogComplete(request.MediaId, request.Path, total, stopwatch.Elapsed);
        return total;
    }

    public async Task<(long? ContentLength, bool AcceptRanges)> ProbeAsync(HttpRangeDownloadRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = request.CreateRequest(HttpMethod.Get, "bytes=0-0");
        using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw request.CreateHttpException(response.StatusCode);
        }

        long? contentLength = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength;
        var acceptRanges = response.StatusCode == HttpStatusCode.PartialContent
                           || response.Headers.AcceptRanges.Any(i => string.Equals(i, "bytes", StringComparison.OrdinalIgnoreCase))
                           || response.Content.Headers.ContentRange is not null;
        return (contentLength, acceptRanges);
    }

    private static DownloadConfiguration CreateDownloaderConfiguration(HttpRangeDownloadRequest request, int segmentCount, bool acceptRanges)
    {
        var headerRequest = request.CreateRequest(HttpMethod.Get, null);
        try
        {
            var requestConfig = BuildRequestConfiguration(headerRequest);
            return new DownloadConfiguration
            {
                ChunkCount = acceptRanges ? segmentCount : 1,
                ParallelCount = acceptRanges ? segmentCount : 1,
                ParallelDownload = acceptRanges && segmentCount > 1,
                MinimumSizeOfChunking = 1,
                MinimumChunkSize = 1,
                MaxTryAgainOnFailure = 3,
                ClearPackageOnCompletionWithFailure = true,
                FileExistPolicy = FileExistPolicy.Delete,
                EnableAutoResumeDownload = false,
                CheckDiskSizeBeforeDownload = true,
                DownloadFileExtension = ".download",
                RequestConfiguration = requestConfig,
            };
        }
        finally
        {
            headerRequest.Dispose();
        }
    }

    private static RequestConfiguration BuildRequestConfiguration(HttpRequestMessage request)
    {
        var config = new RequestConfiguration
        {
            Accept = "*/*",
            AllowAutoRedirect = true,
            KeepAlive = true,
        };

        foreach (var header in request.Headers)
        {
            var value = header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
                ? request.Headers.UserAgent.ToString()
                : string.Join(", ", header.Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            switch (header.Key.ToLowerInvariant())
            {
                case "accept":
                    config.Accept = value;
                    break;
                case "user-agent":
                    config.UserAgent = value;
                    break;
                case "referer":
                    config.Referer = value;
                    break;
                case "cookie":
                    AddHeader(config.Headers, "Cookie", value);
                    break;
                case "range":
                    break;
                default:
                    AddHeader(config.Headers, header.Key, value);
                    break;
            }
        }

        return config;
    }

    private static void AddHeader(WebHeaderCollection headers, string name, string value)
    {
        try
        {
            headers.Remove(name);
            headers.Add(name, value);
        }
        catch
        {
            // Some restricted headers must be mapped to RequestConfiguration properties.
            // Unknown/restricted leftovers are ignored rather than failing the download setup.
        }
    }

    private static void CleanupFailedDownload(string path)
    {
        TryDelete(path);
        TryDelete(path + ".download");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort
        }
    }
}

internal sealed record HttpRangeDownloadRequest(
    string Url,
    string Path,
    string MediaId,
    long MaxBytes,
    bool EnableParallel,
    long MinParallelBytes,
    int SegmentCount,
    Func<HttpMethod, string?, HttpRequestMessage> CreateRequest,
    Func<HttpStatusCode, Exception> CreateHttpException,
    Func<long, Exception> CreateTooLargeException,
    Func<Exception> CreateExceededLimitException,
    Func<int, HttpStatusCode, Exception> CreateRangeNotSupportedException,
    Func<int, string, Exception> CreateContentRangeMismatchException,
    Func<int, long, long, Exception> CreatePartSizeMismatchException,
    Func<long, long, Exception> CreateMergedSizeMismatchException,
    Action<Exception>? OnParallelDownloadFailed = null);
