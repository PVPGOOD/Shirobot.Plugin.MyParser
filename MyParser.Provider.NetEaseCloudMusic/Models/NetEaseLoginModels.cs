namespace MyParser.Provider.NetEaseCloudMusic.Models;

public sealed record NetEaseQrLoginSession(string Key, string Url);

public sealed record NetEaseQrLoginPollResult(
    int Code,
    string Message,
    bool IsLogin,
    bool IsExpired,
    bool IsWaitingConfirmation,
    string? Cookie);
