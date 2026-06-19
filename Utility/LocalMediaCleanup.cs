namespace Shirobot.Plugin.MyParser.Utility;

internal static class LocalMediaCleanup
{
    public static void DeleteLocalVideoIfConfigured(PluginConfig config, string? localPath, string provider)
    {
        if (!config.DeleteLocalVideoAfterSend || string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        var delaySeconds = Math.Max(0, config.DeleteLocalVideoDelaySeconds);
        if (delaySeconds <= 0 && MyParserRuntime.IsCachedVideoPath(localPath))
        {
            // Give concurrent duplicate requests time to reuse/send the same cached file.
            delaySeconds = 30;
        }

        if (delaySeconds <= 0)
        {
            DeleteLocalVideoNow(config, localPath, provider);
            return;
        }

        var cancellationToken = MyParserRuntime.BackgroundCancellationToken;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                DeleteLocalVideoNow(config, localPath, provider);
            }
            catch (OperationCanceledException)
            {
                // Plugin unload cancels pending delayed cleanup so the assembly can be released.
            }
            catch
            {
                // Cleanup is best-effort and must never affect message sending.
            }
        }, cancellationToken);
    }

    private static void DeleteLocalVideoNow(PluginConfig config, string localPath, string provider)
    {
        try
        {
            var fullPath = Path.GetFullPath(localPath);
            if (!File.Exists(fullPath) || !IsUnderAllowedMediaRoot(config, fullPath))
            {
                return;
            }

            TryDeleteFile(fullPath);
            MyParserRuntime.RemoveCachedVideoPath(fullPath);
            if (string.Equals(provider, "bilibili", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(dir) && IsUnderAllowedMediaRoot(config, dir))
                {
                    if (IsBilibiliLiveClipDirectory(dir))
                    {
                        TryDeleteDirectoryRecursive(dir);
                        var parentDir = Path.GetDirectoryName(dir);
                        if (!string.IsNullOrWhiteSpace(parentDir))
                        {
                            TryDeleteDirectoryIfEmpty(parentDir);
                        }

                        return;
                    }

                    TryDeleteFile(Path.Combine(dir, "video.m4s"));
                    TryDeleteFile(Path.Combine(dir, "audio.m4s"));
                    TryDeleteDirectoryIfEmpty(dir);
                }
            }
        }
        catch
        {
            // Cleanup is best-effort and must never affect message sending.
        }
    }

    private static bool IsUnderAllowedMediaRoot(PluginConfig config, string path)
    {
        var roots = new[]
        {
            ResolveRoot(MyParserRuntime.DownloadDirectory, Path.Combine("downloads", "MyParser", "douyin")),
            ResolveRoot(MyParserRuntime.BilibiliDownloadDirectory, Path.Combine("downloads", "MyParser", "bilibili")),
            ResolveRoot(MyParserRuntime.XiaohongshuDownloadDirectory, Path.Combine("downloads", "MyParser", "xiaohongshu")),
            Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser"),
        };

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return roots
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => Path.GetFullPath(i).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .Any(root => normalizedPath.StartsWith(root, comparison));
    }

    private static bool IsBilibiliLiveClipDirectory(string path)
    {
        var dir = new DirectoryInfo(path);
        return string.Equals(dir.Parent?.Name, "live-clips", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRoot(string? configured, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        return Path.IsPathRooted(value) ? value : Path.Combine(AppContext.BaseDirectory, value);
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path, false);
        }
    }

    private static void TryDeleteDirectoryRecursive(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
