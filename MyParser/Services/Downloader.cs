using System.Diagnostics;
using System.Net;
using Shirobot.Plugin.MyParser.Parsing;

namespace Shirobot.Plugin.MyParser.Services;

internal sealed class Downloader(HttpClient http, DownloadProgressLogger progressLogger)
{
    private const int BufferSize = 64 * 1024;
    private readonly HttpClient _ = http;
    private static readonly HttpClient DownloadHttp = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.None,
        MaxConnectionsPerServer = 128,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public async Task<long> DownloadAsync(HttpRangeDownloadRequest request, CancellationToken cancellationToken = default)
    {
        var probe = await ProbeAsync(request, cancellationToken);
        if (request.MaxBytes != long.MaxValue && probe.ContentLength is > 0 && probe.ContentLength > request.MaxBytes)
        {
            throw request.CreateTooLargeException(probe.ContentLength.Value);
        }

        var segmentCount = Math.Clamp(request.SegmentCount, 1, 64);
        return probe.AcceptRanges && segmentCount > 1 && probe.ContentLength is > 0
            ? await DownloadRangesAsync(request, probe.ContentLength.Value, segmentCount, cancellationToken)
            : await DownloadStreamAsync(request, probe.ContentLength, cancellationToken);
    }

    public async Task<long> DownloadStreamAsync(HttpRangeDownloadRequest request, CancellationToken cancellationToken = default)
    {
        var probe = await ProbeAsync(request, cancellationToken);
        if (request.MaxBytes != long.MaxValue && probe.ContentLength is > 0 && probe.ContentLength > request.MaxBytes)
        {
            throw request.CreateTooLargeException(probe.ContentLength.Value);
        }

        return await DownloadStreamAsync(request, probe.ContentLength, cancellationToken);
    }

    private async Task<long> DownloadRangesAsync(HttpRangeDownloadRequest request, long contentLength, int segmentCount, CancellationToken cancellationToken)
    {
        if (request.MaxBytes != long.MaxValue && contentLength > request.MaxBytes)
        {
            throw request.CreateTooLargeException(contentLength);
        }

        var mode = $"http/range/{segmentCount}";
        var stopwatch = Stopwatch.StartNew();
        var tempPath = request.Path + ".download";
        var nextLogAtTicks = 0L;
        long downloaded = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Path)) ?? AppContext.BaseDirectory);
        CleanupFailedDownload(request.Path);
        progressLogger.LogStart(request.MediaId, request.Path, contentLength, mode);

        try
        {
            await PreallocateAsync(tempPath, contentLength, cancellationToken);
            var ranges = SplitRanges(contentLength, segmentCount);
            await Task.WhenAll(ranges.Select((range, index) => DownloadRangeAsync(
                request,
                tempPath,
                index,
                range.Start,
                range.End,
                contentLength,
                mode,
                stopwatch,
                bytes =>
                {
                    var total = Interlocked.Add(ref downloaded, bytes);
                    if (request.MaxBytes != long.MaxValue && total > request.MaxBytes)
                    {
                        throw request.CreateExceededLimitException();
                    }

                    progressLogger.LogProgressThreadSafe(mode, request.MediaId, total, contentLength, stopwatch.Elapsed, ref nextLogAtTicks);
                },
                cancellationToken)));

            var totalBytes = new FileInfo(tempPath).Length;
            if (totalBytes != contentLength)
            {
                throw request.CreateMergedSizeMismatchException(totalBytes, contentLength);
            }

            if (request.MaxBytes != long.MaxValue && totalBytes > request.MaxBytes)
            {
                throw request.CreateExceededLimitException();
            }

            File.Move(tempPath, request.Path, overwrite: true);
            progressLogger.LogComplete(request.MediaId, request.Path, totalBytes, stopwatch.Elapsed);
            return totalBytes;
        }
        catch
        {
            CleanupFailedDownload(request.Path);
            throw;
        }
    }

    private async Task DownloadRangeAsync(
        HttpRangeDownloadRequest request,
        string tempPath,
        int index,
        long start,
        long end,
        long contentLength,
        string mode,
        Stopwatch stopwatch,
        Action<long> onBytes,
        CancellationToken cancellationToken)
    {
        using var httpRequest = request.CreateRequest(HttpMethod.Get, $"bytes={start}-{end}");
        using var response = await DownloadHttp.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw request.CreateRangeNotSupportedException(index, response.StatusCode);
        }

        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw request.CreateRangeNotSupportedException(index, response.StatusCode);
        }

        var expected = end - start + 1;
        var contentRange = response.Content.Headers.ContentRange;
        if (contentRange is null
            || contentRange.From != start
            || contentRange.To != end
            || contentRange.Length != contentLength)
        {
            throw request.CreateContentRangeMismatchException(index, response.Content.Headers.ContentRange?.ToString() ?? string.Empty);
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, BufferSize, FileOptions.Asynchronous | FileOptions.RandomAccess);
        destination.Position = start;
        var buffer = new byte[BufferSize];
        long copied = 0;
        while (copied < expected)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, expected - copied)), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            onBytes(read);
        }

        await destination.FlushAsync(cancellationToken);
        if (copied != expected)
        {
            throw request.CreatePartSizeMismatchException(index, copied, expected);
        }
    }

    private async Task<long> DownloadStreamAsync(HttpRangeDownloadRequest request, long? contentLength, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var nextLogAt = TimeSpan.Zero;
        const string mode = "http/stream";
        var tempPath = request.Path + ".download";
        long downloaded = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Path)) ?? AppContext.BaseDirectory);
        CleanupFailedDownload(request.Path);
        progressLogger.LogStart(request.MediaId, request.Path, contentLength, mode);

        try
        {
            using var httpRequest = request.CreateRequest(HttpMethod.Get, null);
            using var response = await DownloadHttp.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw request.CreateHttpException(response.StatusCode);
            }

            var totalBytes = response.Content.Headers.ContentLength ?? contentLength;
            if (request.MaxBytes != long.MaxValue && totalBytes is > 0 && totalBytes > request.MaxBytes)
            {
                throw request.CreateTooLargeException(totalBytes.Value);
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[BufferSize];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read <= 0)
                    {
                        break;
                    }

                    downloaded += read;
                    if (request.MaxBytes != long.MaxValue && downloaded > request.MaxBytes)
                    {
                        throw request.CreateExceededLimitException();
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progressLogger.LogProgress(mode, request.MediaId, downloaded, totalBytes, stopwatch.Elapsed, ref nextLogAt);
                }

                await destination.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, request.Path, overwrite: true);
            progressLogger.LogComplete(request.MediaId, request.Path, downloaded, stopwatch.Elapsed);
            return downloaded;
        }
        catch
        {
            CleanupFailedDownload(request.Path);
            throw;
        }
    }

    public async Task<(long? ContentLength, bool AcceptRanges)> ProbeAsync(HttpRangeDownloadRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = request.CreateRequest(HttpMethod.Get, "bytes=0-0");
        using var response = await DownloadHttp.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

    private static async Task PreallocateAsync(string path, long length, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, BufferSize, FileOptions.Asynchronous | FileOptions.RandomAccess);
        file.SetLength(length);
        await file.FlushAsync(cancellationToken);
    }

    private static List<(long Start, long End)> SplitRanges(long contentLength, int segmentCount)
    {
        var ranges = new List<(long Start, long End)>(segmentCount);
        var chunkSize = contentLength / segmentCount;
        var start = 0L;
        for (var i = 0; i < segmentCount; i++)
        {
            var end = i == segmentCount - 1 ? contentLength - 1 : start + chunkSize - 1;
            ranges.Add((start, end));
            start = end + 1;
        }

        return ranges;
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

