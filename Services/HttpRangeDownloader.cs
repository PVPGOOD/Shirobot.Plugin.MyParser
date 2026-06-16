using System.Diagnostics;
using System.Net;

namespace Shirobot.Plugin.MyParser.Services;

internal sealed class HttpRangeDownloader(HttpClient http, DownloadProgressLogger progressLogger)
{
    public async Task<long> DownloadAsync(HttpRangeDownloadRequest request, CancellationToken cancellationToken = default)
    {
        var probe = await ProbeAsync(request, cancellationToken);
        if (request.MaxBytes != long.MaxValue && probe.ContentLength is > 0 && probe.ContentLength > request.MaxBytes)
        {
            throw request.CreateTooLargeException(probe.ContentLength.Value);
        }

        var canParallel = request.EnableParallel
                          && probe.AcceptRanges
                          && probe.ContentLength is > 0
                          && probe.ContentLength.Value >= request.MinParallelBytes
                          && probe.ContentLength.Value > request.SegmentCount;

        var stopwatch = Stopwatch.StartNew();
        progressLogger.LogStart(request.MediaId, request.Path, probe.ContentLength, canParallel ? $"parallel/{request.SegmentCount}" : "stream");

        long total;
        if (canParallel)
        {
            try
            {
                total = await DownloadParallelAsync(request, probe.ContentLength!.Value, stopwatch, cancellationToken);
            }
            catch (Exception ex)
            {
                request.OnParallelDownloadFailed?.Invoke(ex);
                if (File.Exists(request.Path))
                {
                    File.Delete(request.Path);
                }

                stopwatch.Restart();
                progressLogger.LogStart(request.MediaId, request.Path, probe.ContentLength, "stream-fallback");
                total = await DownloadStreamAsync(request, stopwatch, cancellationToken);
            }
        }
        else
        {
            total = await DownloadStreamAsync(request, stopwatch, cancellationToken);
        }

        progressLogger.LogComplete(request.MediaId, request.Path, total, stopwatch.Elapsed);
        return total;
    }

    public async Task<long> DownloadStreamAsync(HttpRangeDownloadRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        using var httpRequest = request.CreateRequest(HttpMethod.Get, "bytes=0-");
        using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw request.CreateHttpException(response.StatusCode);
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (request.MaxBytes != long.MaxValue && contentLength is > 0 && contentLength > request.MaxBytes)
        {
            throw request.CreateTooLargeException(contentLength.Value);
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(request.Path);
        var buffer = new byte[128 * 1024];
        long total = 0;
        var nextLogAt = TimeSpan.Zero;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (request.MaxBytes != long.MaxValue && total > request.MaxBytes)
            {
                throw request.CreateExceededLimitException();
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progressLogger.LogProgress("stream", request.MediaId, total, contentLength, stopwatch.Elapsed, ref nextLogAt);
        }

        return total;
    }

    public async Task<long> DownloadParallelAsync(HttpRangeDownloadRequest request, long contentLength, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        if (request.MaxBytes != long.MaxValue && contentLength > request.MaxBytes)
        {
            throw request.CreateTooLargeException(contentLength);
        }

        var tempDir = request.Path + ".parts";
        Directory.CreateDirectory(tempDir);
        try
        {
            var partSize = (long)Math.Ceiling(contentLength / (double)request.SegmentCount);
            long downloaded = 0;
            long nextLogAtTicks = 0;
            var tasks = Enumerable.Range(0, request.SegmentCount).Select(async index =>
            {
                var start = index * partSize;
                var end = Math.Min(contentLength - 1, start + partSize - 1);
                if (start > end)
                {
                    return (Index: index, Path: string.Empty, Length: 0L);
                }

                var partPath = Path.Combine(tempDir, $"part_{index:D2}");
                using var httpRequest = request.CreateRequest(HttpMethod.Get, $"bytes={start}-{end}");
                using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    throw request.CreateRangeNotSupportedException(index, response.StatusCode);
                }

                var expectedLength = end - start + 1;
                var contentRange = response.Content.Headers.ContentRange;
                if (contentRange is not null && (contentRange.From != start || contentRange.To != end))
                {
                    throw request.CreateContentRangeMismatchException(index, contentRange.ToString());
                }

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(partPath);
                var copied = await CopyExactlyAsync(input, output, expectedLength, delta =>
                {
                    var current = Interlocked.Add(ref downloaded, delta);
                    progressLogger.LogProgressThreadSafe("parallel", request.MediaId, current, contentLength, stopwatch.Elapsed, ref nextLogAtTicks);
                }, cancellationToken);
                if (copied != expectedLength)
                {
                    throw request.CreatePartSizeMismatchException(index, copied, expectedLength);
                }

                return (Index: index, Path: partPath, Length: expectedLength);
            }).ToArray();

            var parts = (await Task.WhenAll(tasks)).Where(i => !string.IsNullOrWhiteSpace(i.Path)).OrderBy(i => i.Index).ToArray();
            await using var final = File.Create(request.Path);
            long total = 0;
            foreach (var part in parts)
            {
                await using var input = File.OpenRead(part.Path);
                total += input.Length;
                if (request.MaxBytes != long.MaxValue && total > request.MaxBytes)
                {
                    throw request.CreateExceededLimitException();
                }

                await input.CopyToAsync(final, cancellationToken);
            }

            if (total != contentLength)
            {
                throw request.CreateMergedSizeMismatchException(total, contentLength);
            }

            return total;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // ignore cleanup errors
            }
        }
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

    private static async Task<long> CopyExactlyAsync(Stream input, Stream output, long expectedLength, Action<long> onBytesCopied, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (total < expectedLength)
        {
            var need = (int)Math.Min(buffer.Length, expectedLength - total);
            var read = await input.ReadAsync(buffer.AsMemory(0, need), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            onBytesCopied(read);
        }

        return total;
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
