using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Net.Codecrete.QrCodeGenerator;
using SilkSharp;
using SilkSharp.Codec;
using MyParser.Provider.NetEaseCloudMusic.Infrastructure;
using MyParser.Provider.NetEaseCloudMusic.Models;
using MyParser.Provider.NetEaseCloudMusic.Parsing;
using MyParser.Provider.NetEaseCloudMusic.Views;
using ShiroBot.AvaloniaSdk;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace MyParser.Provider.NetEaseCloudMusic.MessageHandling;

internal sealed partial class NetEaseMessageHandler(ProviderMessageHandlerContext context) : ProviderMessageHandlerBase(context)
{
    public override string ProviderId => "neteasecloudmusic";

    public override async Task HandleLoginAsync(IncomingMessage message)
    {
        try
        {
            if (PrimaryProvider is not IQrLoginProvider qrLoginProvider)
            {
                await ReplyAsync(message, "网易云音乐 provider 不支持扫码登录。");
                return;
            }

            var session = await qrLoginProvider.GenerateQrLoginSessionAsync().ConfigureAwait(false);
            await ReplyAsync(message,
                "网易云音乐扫码登录\n"
                + "请使用网易云音乐手机 App 扫描下面二维码，并在 3 分钟内确认登录。\n"
                + $"如果二维码图片无法显示，请打开：{session.Url}");
            await SendQrImageAsync(message, session.Url, $"netease_qr_{session.Id}").ConfigureAwait(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token).ConfigureAwait(false);
                var poll = await qrLoginProvider.PollQrLoginAsync(session, cts.Token).ConfigureAwait(false);
                switch (poll)
                {
                    case { IsLogin: true }:
                        SaveNetEaseCookieToPluginDirectory();
                        await ReplyAsync(message, "网易云音乐登录成功，Cookie 已保存到插件 cookies/netease.txt。");
                        return;
                    case { IsExpired: true }:
                        await ReplyAsync(message, "网易云音乐登录二维码已过期，请重新发送 #wyy-login。");
                        return;
                    case { IsWaitingConfirmation: true }:
                        BotLog.Info("MyParser 网易云音乐二维码已扫码，等待确认。");
                        break;
                    default:
                        BotLog.Info($"MyParser 网易云音乐二维码轮询: code={poll.Code}, message={poll.Message}");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            await ReplyAsync(message, "网易云音乐登录二维码已超时，请重新发送 #wyy-login。");
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 网易云音乐扫码登录失败：{ex}");
            await ReplyAsync(message, "网易云音乐扫码登录失败：" + ex.Message);
        }
    }

    public override async Task ParseAndReplyAsync(IncomingMessage message, string text, bool silentProviderMismatch = false)
    {
        await ReactAsync(message, "351", "网易云音乐");
        try
        {
            var media = await ProviderRegistry.ParseAsync(text).ConfigureAwait(false);
            if (media.ProviderPayload is not NetEaseParseResult result)
            {
                await ReplyAsync(message, $"{media.ProviderName} 已识别，但返回类型未接入发送流程。");
                await ReactAsync(message, "9", "网易云音乐");
                return;
            }

            await SendCoverCardMessageAsync(message, result).ConfigureAwait(false);
            await SendLyricCardMessageAsync(message, result).ConfigureAwait(false);
            await SendRecordAsync(message, result).ConfigureAwait(false);
            await ReactAsync(message, "426", "网易云音乐");
        }
        catch (NetEaseParseException ex) when (silentProviderMismatch && ex.Message.Contains("无法从输入中提取", StringComparison.OrdinalIgnoreCase))
        {
            await RemoveReactionAsync(message, "351", "网易云音乐");
            BotLog.Info("MyParser 网易云自动解析忽略非目标链接: " + ex.Message);
        }
        catch (NetEaseParseException ex)
        {
            await ReactAsync(message, "9", "网易云音乐");
            await ReplyAsync(message, "网易云音乐解析失败：" + ex.Message);
        }
        catch (TaskCanceledException)
        {
            await ReactAsync(message, "9", "网易云音乐");
            await ReplyAsync(message, "网易云音乐解析超时，请稍后重试。若经常失败，请检查 Cookie/网络。");
        }
        catch (Exception ex)
        {
            await ReactAsync(message, "9", "网易云音乐");
            BotLog.Error("MyParser 网易云音乐解析异常：" + ex);
            await ReplyAsync(message, "网易云音乐解析异常：" + ex.Message);
        }
    }

    private async Task SendQrImageAsync(IncomingMessage message, string text, string fileName)
    {
        var qrFile = await BuildQrImageAsync(text, fileName).ConfigureAwait(false);
        var segment = new ImageOutgoingSegment(qrFile.Uri);
        switch (message)
        {
            case GroupIncomingMessage group:
                await BotContext.Message.SendGroupMessageAsync(group.Group.GroupId, segment).ConfigureAwait(false);
                break;
            case FriendIncomingMessage friend:
                await BotContext.Message.SendPrivateMessageAsync(friend.SenderId, segment).ConfigureAwait(false);
                break;
            default:
                await BotContext.Message.ReplyAsync(message, segment).ConfigureAwait(false);
                break;
        }
    }

    private static async Task<(string Uri, string Path)> BuildQrImageAsync(string text, string fileName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser", "neteasecloudmusic", "qr");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName + ".png");
        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        var png = qr.ToPngBitmap(border: 4, scale: 8);
        await File.WriteAllBytesAsync(path, png).ConfigureAwait(false);
        return ("base64://" + Convert.ToBase64String(png), path);
    }

    private void SaveNetEaseCookieToPluginDirectory()
    {
        var path = ResolveCookiePath("netease.txt");
        File.WriteAllText(path, MyParserRuntime.NetEaseCloudMusicCookie, Encoding.UTF8);
    }

    private async Task SendCoverCardMessageAsync(IncomingMessage message, NetEaseParseResult result)
    {
        if (string.IsNullOrWhiteSpace(result.CoverUrl))
        {
            return;
        }

        try
        {
            var cardUri = await BuildCoverCardUriAsync(result).ConfigureAwait(false);
            var segment = new ImageOutgoingSegment(cardUri);
            var stopwatch = Stopwatch.StartNew();
            BotLog.Info($"MyParser 网易云音乐封面卡片 ImageSegment 发送开始: song_id={result.SongId}, scene={GetMessageScene(message)}, uri_preview={HostServices.PreviewUri(cardUri)}");

            switch (message)
            {
                case GroupIncomingMessage group:
                {
                    var response = await BotContext.Message.SendGroupMessageAsync(group.Group.GroupId, segment);
                    BotLog.Info($"MyParser 网易云音乐封面卡片 ImageSegment 发送接口完成: song_id={result.SongId}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                    break;
                }
                case FriendIncomingMessage friend:
                {
                    var response = await BotContext.Message.SendPrivateMessageAsync(friend.SenderId, segment);
                    BotLog.Info($"MyParser 网易云音乐封面卡片 ImageSegment 发送接口完成: song_id={result.SongId}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                    break;
                }
                default:
                {
                    var response = await BotContext.Message.ReplyAsync(message, segment);
                    BotLog.Info($"MyParser 网易云音乐封面卡片 ImageSegment 发送接口完成: song_id={result.SongId}, scene=reply, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 网易云音乐封面卡片发送失败，跳过: song_id={result.SongId}, cover_url={result.CoverUrl}, error={ex.Message}");
        }
    }

    private async Task SendLyricCardMessageAsync(IncomingMessage message, NetEaseParseResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Lyric) && string.IsNullOrWhiteSpace(result.TranslatedLyric))
        {
            return;
        }

        try
        {
            var cardUri = await BuildLyricCardUriAsync(result).ConfigureAwait(false);
            var segment = new ImageOutgoingSegment(cardUri);
            var stopwatch = Stopwatch.StartNew();
            BotLog.Info($"MyParser 网易云音乐歌词卡片 ImageSegment 发送开始: song_id={result.SongId}, scene={GetMessageScene(message)}, uri_preview={HostServices.PreviewUri(cardUri)}");

            switch (message)
            {
                case GroupIncomingMessage group:
                {
                    var response = await BotContext.Message.SendGroupMessageAsync(group.Group.GroupId, segment);
                    BotLog.Info($"MyParser 网易云音乐歌词卡片 ImageSegment 发送接口完成: song_id={result.SongId}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                    break;
                }
                case FriendIncomingMessage friend:
                {
                    var response = await BotContext.Message.SendPrivateMessageAsync(friend.SenderId, segment);
                    BotLog.Info($"MyParser 网易云音乐歌词卡片 ImageSegment 发送接口完成: song_id={result.SongId}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                    break;
                }
                default:
                {
                    var response = await BotContext.Message.ReplyAsync(message, segment);
                    BotLog.Info($"MyParser 网易云音乐歌词卡片 ImageSegment 发送接口完成: song_id={result.SongId}, scene=reply, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 网易云音乐歌词卡片发送失败，跳过: song_id={result.SongId}, error={ex.Message}");
        }
    }

    private async Task<string> BuildCoverCardUriAsync(NetEaseParseResult result)
    {
        using var imageHttp = HostServices.CreateImageHttpClient();
        var coverImage = await HostServices.BuildRemoteImageAsync(
            imageHttp,
            "网易云音乐",
            result.CoverUrl,
            result.SourceUrl,
            $"netease_cover_{result.SongId}",
            Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser", "neteasecloudmusic", "covers"),
            request => request.Headers.Referrer = new Uri("https://music.163.com/")).ConfigureAwait(false);

        if (BotContext.Render is null)
        {
            BotLog.Warning($"MyParser Avalonia 渲染服务不可用，直接发送网易云原始封面: song_id={result.SongId}");
            return coverImage.Uri;
        }

        try
        {
            var coverBitmap = !string.IsNullOrWhiteSpace(coverImage.LocalPath)
                ? HostServices.DecodeImageFileForRender(coverImage.LocalPath)
                : HostServices.DecodeBase64ImageForRender(coverImage.Uri);

            var vm = new NetEaseMusicCardViewModel
            {
                Cover = coverBitmap,
                Title = result.Title,
                Artists = result.Artists,
                Album = "《" + result.Album + "》",
                QualityText = GetQualityDisplayName(result.Quality),
                SizeText = ProviderTextUtilities.FormatSize(result.FileSize),
                BitrateText = result.Bitrate is > 0 ? $"{result.Bitrate / 1000}kbps" : "码率--",
                SongIdText = $"ID {result.SongId}",
                SourceText = "网易云音乐",
            };
            var png = await BotContext.RenderControlPngAsync<NetEaseMusicCard>(vm, new ControlRenderOptions(RenderTheme.Auto)).ConfigureAwait(false);
            var uri = "base64://" + Convert.ToBase64String(png);
            BotLog.Info($"MyParser 网易云音乐封面卡片渲染完成: song_id={result.SongId}, cover_url={result.CoverUrl}, png_kb={png.Length / 1024d:F1}, mode=base64");
            return uri;
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 网易云音乐封面卡片渲染失败，直接发送原始封面: song_id={result.SongId}, cover_url={result.CoverUrl}, error={ex.Message}");
            return coverImage.Uri;
        }
    }

    private async Task<string> BuildLyricCardUriAsync(NetEaseParseResult result)
    {
        if (BotContext.Render is null)
        {
            throw new InvalidOperationException("Avalonia 渲染服务不可用。");
        }

        using var imageHttp = HostServices.CreateImageHttpClient();
        var coverImage = string.IsNullOrWhiteSpace(result.CoverUrl)
            ? (Uri: string.Empty, LocalPath: (string?)null)
            : await HostServices.BuildRemoteImageAsync(
                imageHttp,
                "网易云音乐",
                result.CoverUrl,
                result.SourceUrl,
                $"netease_lyric_cover_{result.SongId}",
                Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser", "neteasecloudmusic", "covers"),
                request => request.Headers.Referrer = new Uri("https://music.163.com/")).ConfigureAwait(false);

        var coverBitmap = !string.IsNullOrWhiteSpace(coverImage.LocalPath)
            ? HostServices.DecodeImageFileForRender(coverImage.LocalPath)
            : HostServices.DecodeBase64ImageForRender(coverImage.Uri);
        var lyricText = BuildLyricCardText(result);
        var vm = new NetEaseLyricCardViewModel
        {
            Cover = coverBitmap,
            Title = result.Title,
            Artists = result.Artists,
            Album = "《" + result.Album + "》",
            LyricText = lyricText,
            CardHeight = CalculateLyricCardHeight(lyricText),
            SourceText = "网易云音乐 · 歌词",
        };
        var png = await BotContext.RenderControlPngAsync<NetEaseLyricCard>(vm, new ControlRenderOptions(RenderTheme.Auto)).ConfigureAwait(false);
        BotLog.Info($"MyParser 网易云音乐歌词卡片渲染完成: song_id={result.SongId}, png_kb={png.Length / 1024d:F1}, mode=base64");
        return "base64://" + Convert.ToBase64String(png);
    }

    private static string BuildLyricCardText(NetEaseParseResult result)
    {
        var lines = MergeLyrics(result.Lyric, result.TranslatedLyric)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => ProviderTextUtilities.TrimLine(i, 58))
            .Take(42)
            .ToArray();
        return lines.Length == 0 ? "暂无歌词" : string.Join(Environment.NewLine, lines);
    }

    private static double CalculateLyricCardHeight(string lyricText)
    {
        var lines = lyricText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var visualLines = lines.Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / 34d)));
        return Math.Clamp(100 + visualLines * 18 + 34, 220, 980);
    }

    private static IEnumerable<string> MergeLyrics(string? lyric, string? translatedLyric)
    {
        var original = ParseLyricLines(lyric);
        var translated = ParseLyricDictionary(translatedLyric);
        foreach (var item in original)
        {
            var text = item.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (translated.TryGetValue(item.TimeKey, out var t) && !string.IsNullOrWhiteSpace(t) && !string.Equals(text, t, StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{text} / {t}";
            }
            else
            {
                yield return text;
            }
        }
    }

    private static Dictionary<string, string> ParseLyricDictionary(string? lyric)
    {
        return ParseLyricLines(lyric)
            .GroupBy(i => i.TimeKey, StringComparer.Ordinal)
            .ToDictionary(i => i.Key, i => i.First().Text, StringComparer.Ordinal);
    }

    private static List<LyricLine> ParseLyricLines(string? lyric)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lyric))
        {
            return result;
        }

        foreach (var rawLine in lyric.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var matches = LyricTimeRegex().Matches(line);
            if (matches.Count == 0)
            {
                continue;
            }

            var lastMatch = matches[^1];
            var text = line[(lastMatch.Index + lastMatch.Length)..].Trim();
            text = LyricMetadataRegex().Replace(text, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match match in matches)
            {
                var timeKey = NormalizeLyricTimeKey(match.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(timeKey))
                {
                    result.Add(new LyricLine(timeKey, text));
                }
            }
        }

        return result
            .GroupBy(i => i.TimeKey, StringComparer.Ordinal)
            .Select(i => i.First())
            .ToList();
    }

    private static string NormalizeLyricTimeKey(string time)
    {
        var parts = time.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var minutes)) return string.Empty;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return string.Empty;
        var totalCentiseconds = (int)Math.Round((minutes * 60 + seconds) * 100, MidpointRounding.AwayFromZero);
        return (totalCentiseconds / 100).ToString(CultureInfo.InvariantCulture) + "." + (totalCentiseconds % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"\[(\d{1,3}:\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex LyricTimeRegex();

    [GeneratedRegex(@"\[(?:ar|ti|al|by|offset|kana|tool|ve|re):[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex LyricMetadataRegex();

    private sealed record LyricLine(string TimeKey, string Text);

    private static string GetQualityDisplayName(string quality)
    {
        return quality switch
        {
            "standard" => "标准音质",
            "exhigh" => "极高音质",
            "lossless" => "无损音质",
            "hires" => "Hi-Res",
            "sky" => "沉浸环绕声",
            "jyeffect" => "高清环绕声",
            "jymaster" => "超清母带",
            "dolby" => "杜比全景声",
            _ => string.IsNullOrWhiteSpace(quality) ? "音质--" : quality,
        };
    }

    private async Task SendRecordAsync(IncomingMessage message, NetEaseParseResult result)
    {
        var localPath = await DownloadAudioToLocalAsync(result).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var variants = await BuildSilkRecordVariantsAsync(localPath, result).ConfigureAwait(false);
            foreach (var variant in variants)
            {
                var recordUri = await BuildRecordUriAsync(variant.Path).ConfigureAwait(false);
                var segment = new RecordOutgoingSegment(recordUri);
                BotLog.Info($"MyParser 网易云音乐 SILK RecordSegment 发送开始: song_id={result.SongId}, variant={variant.Name}, scene={GetMessageScene(message)}, silk_path={variant.Path}, file_kb={new FileInfo(variant.Path).Length / 1024d:F1}, uri_mode={HostServices.GetUriMode(recordUri)}, uri_preview={HostServices.PreviewUri(recordUri)}");
                await SendRecordSegmentAsync(message, segment, result.SongId, variant.Name, stopwatch).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }

            BotLog.Info($"MyParser 网易云音乐 SILK RecordSegment 发送完成: song_id={result.SongId}, variants={variants.Count}, elapsed={stopwatch.Elapsed:mm\\:ss}");
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser 网易云音乐 SILK RecordSegment 发送失败，回退 MyParser 文件上传: song_id={result.SongId}, error={ex}");
            try
            {
                var uploadInfo = await HostServices.UploadLocalFileAsync(Config, message, localPath, "网易云音乐", result.SongId.ToString(), preferBase64: true).ConfigureAwait(false);
                BotLog.Info($"MyParser 网易云音乐文件上传完成: song_id={result.SongId}, {uploadInfo}");
                await ReplyAsync(message, $"网易云音乐语音发送失败，已上传为文件：{result.Title} - {result.Artists}\n{uploadInfo}");
            }
            catch (Exception uploadEx)
            {
                BotLog.Warning($"MyParser 网易云音乐文件上传失败，回退文本: song_id={result.SongId}, error={uploadEx.Message}");
                await ReplyAsync(message, FormatResult(result));
            }
        }
    }

    private async Task SendRecordSegmentAsync(IncomingMessage message, RecordOutgoingSegment segment, long songId, string variantName, Stopwatch stopwatch)
    {
        switch (message)
        {
            case GroupIncomingMessage group:
            {
                var response = await BotContext.Message.SendGroupMessageAsync(group.Group.GroupId, segment);
                BotLog.Info($"MyParser 网易云音乐 SILK RecordSegment 发送接口完成: song_id={songId}, variant={variantName}, scene=group, group_id={group.Group.GroupId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureRecordSendAccepted(response.MessageSeq, "group");
                break;
            }
            case FriendIncomingMessage friend:
            {
                var response = await BotContext.Message.SendPrivateMessageAsync(friend.SenderId, segment);
                BotLog.Info($"MyParser 网易云音乐 SILK RecordSegment 发送接口完成: song_id={songId}, variant={variantName}, scene=friend, user_id={friend.SenderId}, message_seq={response.MessageSeq}, time={response.Time}, elapsed={stopwatch.Elapsed:mm\\:ss}");
                EnsureRecordSendAccepted(response.MessageSeq, "friend");
                break;
            }
            default:
                await BotContext.Message.ReplyAsync(message, segment);
                break;
        }
    }

    private async Task SendTextForRecordAsync(IncomingMessage message, string text)
    {
        switch (message)
        {
            case GroupIncomingMessage group:
                await BotContext.Message.SendGroupMessageAsync(group.Group.GroupId, new TextOutgoingSegment(text));
                break;
            case FriendIncomingMessage friend:
                await BotContext.Message.SendPrivateMessageAsync(friend.SenderId, new TextOutgoingSegment(text));
                break;
            default:
                await ReplyAsync(message, text);
                break;
        }
    }

    private static void EnsureRecordSendAccepted(long messageSeq, string scene)
    {
        if (messageSeq <= 0)
        {
            throw new InvalidOperationException($"RecordSegment 发送未返回有效 message_seq={messageSeq}，scene={scene}。可能是适配器拒绝了语音文件 URI 或音频格式。");
        }
    }

    private async Task<IReadOnlyList<SilkRecordVariant>> BuildSilkRecordVariantsAsync(string localPath, NetEaseParseResult result)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser", "neteasecloudmusic", "silk");
        Directory.CreateDirectory(directory);
        var safeBaseName = ProviderTextUtilities.SanitizeFileName($"{result.Artists} - {result.Title}_{result.SongId}", 120);

        var mobile = new SilkRecordVariant(
            "mobile-best",
            "手机最优",
            "pcm_rate=48000 silk_rate=100000 max=24000 packet=20 tencent=true filter=soxr",
            Path.Combine(directory, safeBaseName + "_mobile_48k_100k_soxr_full.silk"),
            100000);
        var pc = new SilkRecordVariant(
            "pc-best",
            "电脑最优",
            // QQ 电脑端对语音体积/码流兼容性较敏感，实测超过约 270KB 容易无法播放。
            // 35k 在多数 59s 歌曲上会落在 260KB 左右，比 37.8k 更稳。
            "pcm_rate=48000 silk_rate=35000 max=24000 packet=20 tencent=true filter=soxr",
            Path.Combine(directory, safeBaseName + "_pc_48k_35000_soxr_full.silk"),
            35000);

        var variants = Config.SendNetEaseMobileBestRecord
            ? new[] { pc, mobile }
            : new[] { pc };

        foreach (var variant in variants)
        {
            if (File.Exists(variant.Path) && new FileInfo(variant.Path).Length > 0)
            {
                continue;
            }

            await EncodeSilkAsync(localPath, variant.Path, variant.SilkRate).ConfigureAwait(false);
        }

        return variants;
    }

    private async Task EncodeSilkAsync(string inputPath, string outputSilkPath, int silkRate)
    {
        var ffmpeg = ResolveFfmpegPath() ?? throw new InvalidOperationException("未找到 ffmpeg。请在配置 FfmpegPath 中填写 ffmpeg.exe 路径，或将 ffmpeg 加入 PATH。");
        // SilkSharp/native silkcodec 对非 ASCII 路径兼容性不好；这里使用纯 ASCII 临时路径，
        // 编码成功后再移动到包含歌名的最终缓存路径。
        var workDirectory = Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser", "neteasecloudmusic", "silk-work");
        Directory.CreateDirectory(workDirectory);
        var tempBaseName = "netease_" + Guid.NewGuid().ToString("N");
        var tempPcmPath = Path.Combine(workDirectory, tempBaseName + ".pcm");
        var tempSilkPath = Path.Combine(workDirectory, tempBaseName + ".silk");

        try
        {
            await ConvertToPcmAsync(ffmpeg, inputPath, tempPcmPath).ConfigureAwait(false);

            var encoder = new SilkEncoder
            {
                FS_API = 48000,
                Rate = silkRate,
                FS_MaxInternal = 24000,
                PacketLength = 20,
                Tencent = true,
                Complecity = SilkComplecity.High,
                Loss = 0,
                DTX = false,
                BandFEC = false,
            };
            await encoder.EncodeAsync(tempPcmPath, tempSilkPath).ConfigureAwait(false);

            if (!File.Exists(tempSilkPath) || new FileInfo(tempSilkPath).Length == 0)
            {
                throw new InvalidDataException("SILK 编码输出为空。");
            }

            if (File.Exists(outputSilkPath)) File.Delete(outputSilkPath);
            File.Move(tempSilkPath, outputSilkPath);
            BotLog.Info($"MyParser 网易云音乐 SILK 编码完成: input={inputPath}, output={outputSilkPath}, silk_rate={silkRate}, size_kb={new FileInfo(outputSilkPath).Length / 1024d:F1}, encoder=DrAbc.SilkSharp");
        }
        finally
        {
            TryDeleteFile(tempPcmPath);
            TryDeleteFile(tempSilkPath);
        }
    }

    private static async Task ConvertToPcmAsync(string ffmpeg, string inputPath, string outputPcmPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[]
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", inputPath,
            "-vn",
            "-af", "aresample=resampler=soxr:precision=28",
            "-ac", "1",
            "-ar", "48000",
            "-f", "s16le",
            outputPcmPath,
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg PCM 转换进程启动失败。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timeout = TimeSpan.FromMinutes(3);
        var waitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(waitTask, Task.Delay(timeout)).ConfigureAwait(false) != waitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException($"ffmpeg PCM 转换超时（>{timeout.TotalSeconds:F0}s）。");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg PCM 转换失败(exit={process.ExitCode}): {stderr}\n{stdout}");
        }

        if (!File.Exists(outputPcmPath) || new FileInfo(outputPcmPath).Length == 0)
        {
            throw new InvalidDataException("ffmpeg PCM 转换输出为空。");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private string? ResolveFfmpegPath()
    {
        if (!string.IsNullOrWhiteSpace(Config.FfmpegPath) && File.Exists(Config.FfmpegPath))
        {
            return Config.FfmpegPath;
        }

        var executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, executableName),
            Path.Combine(AppContext.BaseDirectory, "vendor", "ffmpeg", "bin", executableName),
            Path.Combine(Environment.CurrentDirectory, executableName),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return OperatingSystem.IsWindows()
            ? FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg")
            : FindOnPath("ffmpeg") ?? FindOnPath("ffmpeg.exe");
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var fullPath = Path.Combine(directory, fileName);
                if (File.Exists(fullPath)) return fullPath;
            }
            catch
            {
                // ignore invalid PATH entries
            }
        }
        return null;
    }

    private sealed record SilkRecordVariant(string Name, string DisplayName, string Description, string Path, int SilkRate);

    private static async Task<string> BuildRecordUriAsync(string localPath)
    {
        // Milky 的 record uri 支持 file/http/base64。这里用 base64:// 把本地文件内容交给适配器，
        // 避免外部 Milky/QQ 进程无法访问 ShiroBot 进程本地 file:// 路径导致“接口返回成功但群里无语音”。
        var bytes = await File.ReadAllBytesAsync(localPath).ConfigureAwait(false);
        return "base64://" + Convert.ToBase64String(bytes);
    }

    private static async Task<string> DownloadAudioToLocalAsync(NetEaseParseResult result)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyParser", "neteasecloudmusic");
        Directory.CreateDirectory(directory);
        var extension = NormalizeExtension(result.FileType);
        var fileName = ProviderTextUtilities.SanitizeFileName($"{result.Artists} - {result.Title}_{result.SongId}_{result.Quality}", 160) + extension;
        var path = Path.Combine(directory, fileName);

        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return path;
        }

        var tempPath = path + ".download";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using var http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        })
        {
            Timeout = TimeSpan.FromMinutes(5),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        using var request = NetEaseHttp.CreateAudioRequest(HttpMethod.Get, result.AudioUrl);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        await using (var output = File.Create(tempPath))
        {
            await input.CopyToAsync(output).ConfigureAwait(false);
        }

        if (new FileInfo(tempPath).Length == 0)
        {
            File.Delete(tempPath);
            throw new InvalidDataException("网易云音乐下载到空文件。");
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
        File.Move(tempPath, path);
        return path;
    }

    private static string NormalizeExtension(string? fileType)
    {
        var ext = string.IsNullOrWhiteSpace(fileType) ? ".mp3" : "." + fileType.Trim().TrimStart('.').ToLowerInvariant();
        return ext is ".mp3" or ".flac" or ".m4a" or ".mp4" or ".ogg" or ".opus" ? ext : ".mp3";
    }

    private static string FormatResult(NetEaseParseResult result)
    {
        return $"网易云音乐解析\n标题：{result.Title}\n歌手：{result.Artists}\n专辑：{result.Album}\n音质：{result.Quality}\n大小：{ProviderTextUtilities.FormatSize(result.FileSize)}\n链接：{result.AudioUrl}\n原链接：{result.SourceUrl}";
    }
}
