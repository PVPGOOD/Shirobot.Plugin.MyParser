using System.Net;
using Avalonia.Media.Imaging;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.MyParser.Parsing;

public interface IParseProviderWithParser : IParseProvider
{
    object ParserObject { get; }
}

public interface IParserHttpClientAccessor
{
    HttpClient HttpClient { get; }
}

public interface IProviderLoginStatusProvider
{
    Task<ProviderLoginStatus> CheckLoginStatusAsync(CancellationToken cancellationToken = default);
}

public interface IQrLoginProvider
{
    Task<QrLoginSession> GenerateQrLoginSessionAsync(CancellationToken cancellationToken = default);
    Task<QrLoginPollResult> PollQrLoginAsync(QrLoginSession session, CancellationToken cancellationToken = default);
}

public interface IVideoDownloadGate
{
    void EnsureVideoDownloadAllowed();
}

public interface IProviderTextNormalizer
{
    string? NormalizeParseText(string text);
}

public interface IIncomingProviderTextNormalizer : IProviderTextNormalizer
{
    string? NormalizeParseText(IncomingMessage message);
}

public interface ICookieValidator
{
    bool LooksLikeCookie(string cookie);
}

public interface IMyParserProviderModule
{
    string Id { get; }
    IReadOnlyList<IParseProvider> CreateProviders(PluginConfig config);
}

public interface IProviderMessageHandlerFactory
{
    IProviderMessageHandler? CreateMessageHandler(ProviderMessageHandlerContext context);
}

public interface IProviderMessageHandler : IDisposable
{
    string ProviderId { get; }
    Task ParseAndReplyAsync(IncomingMessage message, string text, bool silentProviderMismatch = false);
    Task HandleLoginAsync(IncomingMessage message);
}

public interface IProviderRuntimeModule
{
    IReadOnlyList<string> ProviderIds { get; }
    IReadOnlyList<string> CommandPrefixes { get; }
    void LoadRuntime(ProviderRuntimeContext context);
    void ReloadRuntime(ProviderRuntimeContext context);
    Task LogRuntimeStatusAsync(ProviderRuntimeContext context);
    IReadOnlyList<ProviderCommandDescriptor> CreateCommands(ProviderCommandContext context);
    string? GetHelpText(PluginConfig config);
    bool IsPluginResultMessage(string text);
    bool IsAutoParseEnabled(PluginConfig config);
}

public interface IProviderReplyParseTextBuilder
{
    string? TryBuildParseText(IncomingMessage message);
    bool IsDeferredParseText(string text);
}

public interface IProviderHostServices
{
    Task ReactAsync(IncomingMessage message, string faceId, string platformName);
    Task RemoveReactionAsync(IncomingMessage message, string faceId, string platformName);
    Task<SendMessageResult> ReplyTextAsync(PluginConfig config, IncomingMessage message, string text);
    Task SendImageAsync(IncomingMessage message, ImageOutgoingSegment segment);
    Task RunLoggedBackgroundAsync(string description, Func<Task> action);
    string ResolveCookiePath(string fileName);
    Task<string> UploadLocalVideoFileAsync(PluginConfig config, IncomingMessage message, string? localVideoPath, string platformName, string mediaId);
    string GetMessageScene(IncomingMessage message);
    string GetUriMode(string uri);
    string PreviewUri(string? uri, int maxLength = 180);
    string RegisterLocalVideoFile(string path);
    void UnregisterLocalVideoFile(string? path);
    void DeleteLocalVideoIfConfigured(PluginConfig config, string? localPath, string provider);
    void CleanupStartupResidues(PluginConfig config);
    HttpClient CreateImageHttpClient();
    Task<(string Uri, string? LocalPath)> BuildRemoteImageAsync(
        HttpClient http,
        string platformName,
        string? imageUrl,
        string? referer,
        string filePrefix,
        string localDirectory,
        Action<HttpRequestMessage>? configureRequest = null,
        long maxBytes = 10 * 1024L * 1024L);
    Task<(string FileUri, string LocalPath)> DownloadProviderVideoAsync(
        PluginConfig config,
        ProviderVideoDownloadRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TResult>> SelectParallelOrderedAsync<TSource, TResult>(
        IEnumerable<TSource> source,
        int maxConcurrency,
        Func<TSource, Task<TResult>> selector);
    Bitmap? DecodeBase64ImageForRender(string uri);
    Bitmap? DecodeImageFileForRender(string path);
    Task<long> DownloadAsync(
        HttpRangeDownloadRequest request,
        bool logProgress,
        int intervalSeconds,
        string logPrefix,
        string identifierName,
        CancellationToken cancellationToken = default);
    Task<(long? ContentLength, bool AcceptRanges)> ProbeDownloadAsync(
        HttpRangeDownloadRequest request,
        bool logProgress,
        int intervalSeconds,
        string logPrefix,
        string identifierName,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderMessageHandlerContext(
    IBotContext BotContext,
    PluginConfig Config,
    ParseProviderRegistry ProviderRegistry,
    IParseProvider PrimaryProvider,
    IProviderHostServices HostServices);

public sealed record ProviderRuntimeContext(
    IBotContext BotContext,
    PluginConfig Config,
    IProviderHostServices HostServices,
    IParseProvider? PrimaryProvider = null);

public sealed record ProviderCommandContext(
    IBotContext BotContext,
    PluginConfig Config,
    IProviderHostServices HostServices,
    IParseProvider? PrimaryProvider,
    IProviderMessageHandler? MessageHandler);

public sealed record ProviderCommandDescriptor(
    string Command,
    Func<IncomingMessage, Task> HandleAsync,
    bool AdminOnly = false);

public sealed record ProviderLoginStatus(bool IsLogin, string? UserName, string? UserId, string Message, bool NeedVerify = false);

public sealed record QrLoginSession(string Id, string Url, object? State = null);

public sealed record QrLoginPollResult(
    int Code,
    string Message,
    bool IsLogin,
    bool IsExpired = false,
    bool IsWaitingConfirmation = false,
    bool NeedVerify = false,
    string? UserName = null,
    object? State = null);

public enum ProviderVideoValidationKind
{
    None,
    Mp4,
    Mp4OrWebM,
}

public sealed record ProviderVideoDownloadRequest(
    string PlatformId,
    string PlatformDisplayName,
    string MediaId,
    string CacheKey,
    IReadOnlyList<string> CandidateUrls,
    string DownloadDirectory,
    string FileNamePrefix,
    string FileExtension,
    Func<HttpMethod, string, string?, HttpRequestMessage> CreateRequest,
    ProviderVideoValidationKind ValidationKind = ProviderVideoValidationKind.Mp4,
    string IdentifierName = "media_id");

public sealed record HttpRangeDownloadRequest(
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
