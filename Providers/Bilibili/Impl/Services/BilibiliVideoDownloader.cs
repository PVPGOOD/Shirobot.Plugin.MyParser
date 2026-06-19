using System.Diagnostics;
using System.Text;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Infrastructure;
using Shirobot.Plugin.MyParser.Providers.Bilibili.Models;
using Shirobot.Plugin.MyParser.Services;
using ShiroBot.SDK.Abstractions;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Impl.Services;

internal sealed class BilibiliVideoDownloader(PluginConfig config, HttpClient http)
{
    private readonly DownloadProgressLogger _progressLogger = new(config.LogDownloadProgress, 2, "MyParser", "bvid");

    public async Task<(string FileUri, string LocalPath)> DownloadAndMuxAsync(BilibiliParseResult result, CancellationToken cancellationToken = default)
    {
        var video = result.SelectedVideo ?? throw new BilibiliParseException("没有可下载的视频流。");
        var audio = result.SelectedAudio ?? throw new BilibiliParseException("没有可下载的音频流。");
        var cacheKey = $"bilibili:{result.Bvid}:p{result.Page}:cid{result.Cid}:v{video.QualityId}:{video.CodecName}:a{audio.StreamId}";
        var downloaded = await MyParserRuntime.GetOrAddVideoDownloadAsync(cacheKey, () => DownloadAndMuxCoreAsync(result, video, audio, cancellationToken));
        result.LocalVideoPath = downloaded.LocalPath;
        result.LocalVideoFileUri = downloaded.FileUri;
        return downloaded;
    }

    private async Task<(string FileUri, string LocalPath)> DownloadAndMuxCoreAsync(BilibiliParseResult result, BilibiliMediaStream video, BilibiliMediaStream audio, CancellationToken cancellationToken)
    {
        var ffmpeg = ResolveFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            throw new BilibiliParseException("未找到 ffmpeg。请在配置 FfmpegPath 中填写 ffmpeg.exe 路径，或将 ffmpeg 加入 PATH。");
        }

        var dir = ResolveDownloadDirectory();
        Directory.CreateDirectory(dir);
        var title = SanitizeFileName(string.IsNullOrWhiteSpace(result.Title) ? result.Bvid : result.Title!, 80);
        var unique = $"{SanitizeFileName(result.Bvid, 32)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var videoPath = Path.Combine(dir, $"{unique}_video.m4s");
        var audioPath = Path.Combine(dir, $"{unique}_audio.m4s");
        var outputPath = GetAvailableOutputPath(dir, $"{title}.mp4");

        BotLog.Info($"MyParser Bilibili 音视频流并发下载开始: bvid={result.Bvid}, video={Path.GetFileName(videoPath)}, audio={Path.GetFileName(audioPath)}, output={Path.GetFileName(outputPath)}");
        try
        {
            await Task.WhenAll(
                DownloadStreamAsync(video, videoPath, result, "视频流", cancellationToken),
                DownloadStreamAsync(audio, audioPath, result, "音频流", cancellationToken));
            BotLog.Info($"MyParser Bilibili 音视频流并发下载完成，开始 ffmpeg 合并: bvid={result.Bvid}");
            await MuxAsync(ffmpeg, videoPath, audioPath, outputPath, cancellationToken);
            await ValidateMuxedVideoAsync(outputPath, cancellationToken);
        }
        finally
        {
            TryDelete(videoPath);
            TryDelete(audioPath);
        }

        result.LocalVideoPath = outputPath;
        result.LocalVideoFileUri = new Uri(outputPath).AbsoluteUri;
        return (result.LocalVideoFileUri, outputPath);
    }

    private async Task DownloadStreamAsync(BilibiliMediaStream stream, string path, BilibiliParseResult result, string label, CancellationToken cancellationToken)
    {
        var urls = stream.UrlCandidates.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToArray();
        if (urls.Length == 0)
        {
            throw new BilibiliParseException($"{label}没有可用下载地址。");
        }

        var maxBytes = config.MaxVideoDownloadMegabytes <= 0
            ? long.MaxValue
            : config.MaxVideoDownloadMegabytes * 1024L * 1024L;
        const long minParallelBytes = 1;
        var segmentCount = Math.Clamp(config.ParallelDownloadThreads, 1, 64);
        Exception? lastError = null;

        foreach (var url in urls)
        {
            try
            {
                var downloader = new MyParser.Services.Downloader(http, _progressLogger);
                var request = new HttpRangeDownloadRequest(
                    url,
                    path,
                    result.Bvid,
                    maxBytes,
                    true,
                    minParallelBytes,
                    segmentCount,
                    (method, range) => CreateMediaRequest(method, url, result, range),
                    statusCode => new BilibiliParseException($"{label}下载 HTTP {(int)statusCode}"),
                    bytes => new BilibiliParseException($"{label}文件过大：{bytes / 1024 / 1024}MB > {config.MaxVideoDownloadMegabytes}MB"),
                    () => new BilibiliParseException($"{label}文件超过限制：{config.MaxVideoDownloadMegabytes}MB"),
                    (index, statusCode) => new BilibiliParseException($"{label}分片 {index} 不支持 Range：HTTP {(int)statusCode}"),
                    (index, contentRange) => new BilibiliParseException($"{label}分片 {index} Content-Range 不匹配：{contentRange}"),
                    (index, copied, expected) => new BilibiliParseException($"{label}分片 {index} 大小不匹配：{copied} != {expected}"),
                    (total, expected) => new BilibiliParseException($"{label}分片合并大小不一致：{total} != {expected}"),
                    ex => BotLog.Warning($"MyParser Bilibili {label}并发下载失败，回退普通下载: bvid={result.Bvid}, error={ex.Message}"));
                var total = await downloader.DownloadAsync(request, cancellationToken);
                if (total <= 0)
                {
                    throw new BilibiliParseException($"{label}下载到空文件。");
                }

                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or BilibiliParseException)
            {
                lastError = ex;
                TryDelete(path);
            }
        }

        throw new BilibiliParseException($"{label}下载失败：{lastError?.Message ?? "无可用地址"}");
    }

    private HttpRequestMessage CreateMediaRequest(HttpMethod method, string url, BilibiliParseResult result, string? range)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("User-Agent", BilibiliConstants.UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", result.SourceUrl ?? $"https://www.bilibili.com/video/{result.Bvid}/");
        request.Headers.TryAddWithoutValidation("Origin", BilibiliConstants.Origin);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (!string.IsNullOrWhiteSpace(range))
        {
            request.Headers.TryAddWithoutValidation("Range", range);
        }

        if (!string.IsNullOrWhiteSpace(MyParserRuntime.BilibiliCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", MyParserRuntime.BilibiliCookie);
        }

        return request;
    }

    private static async Task MuxAsync(string ffmpeg, string videoPath, string audioPath, string outputPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(videoPath);
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(audioPath);
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("0:v:0");
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("1:a:0");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("copy");
        psi.ArgumentList.Add("-movflags");
        psi.ArgumentList.Add("+faststart");
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi) ?? throw new BilibiliParseException("ffmpeg 启动失败。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            if (detail.Length > 2000)
            {
                detail = detail[^2000..];
            }

            throw new BilibiliParseException("ffmpeg 合并失败：" + detail);
        }
    }

    private static async Task ValidateMuxedVideoAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1024)
        {
            throw new BilibiliParseException("ffmpeg 输出文件为空或过小。");
        }

        await using var file = File.OpenRead(path);
        var header = new byte[Math.Min(4096, (int)Math.Min(file.Length, 4096))];
        var read = await file.ReadAsync(header, cancellationToken);
        var ascii = Encoding.ASCII.GetString(header, 0, read);
        if (!ascii.Contains("ftyp", StringComparison.Ordinal))
        {
            throw new BilibiliParseException("ffmpeg 输出文件不像 MP4，可能合并失败。");
        }
    }

    private string ResolveDownloadDirectory()
    {
        if (string.IsNullOrWhiteSpace(MyParserRuntime.BilibiliDownloadDirectory))
        {
            return Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser", "bilibili");
        }

        return Path.IsPathRooted(MyParserRuntime.BilibiliDownloadDirectory)
            ? MyParserRuntime.BilibiliDownloadDirectory
            : Path.Combine(AppContext.BaseDirectory, MyParserRuntime.BilibiliDownloadDirectory);
    }

    private string? ResolveFfmpegPath()
    {
        if (!string.IsNullOrWhiteSpace(config.FfmpegPath) && File.Exists(config.FfmpegPath))
        {
            return config.FfmpegPath;
        }

        var executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, executableName),
            Path.Combine(AppContext.BaseDirectory, "vendor", "ffmpeg", "bin", executableName),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return OperatingSystem.IsWindows()
            ? FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg")
            : FindOnPath("ffmpeg") ?? FindOnPath("ffmpeg.exe");
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore invalid PATH entries
            }
        }

        return null;
    }

    private static string SanitizeFileName(string value, int maxLength)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        value = value.ReplaceLineEndings(" ").Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "bilibili_video";
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string GetAvailableOutputPath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return Path.Combine(directory, $"{name}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{extension}");
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
            // ignore cleanup errors
        }
    }
}
