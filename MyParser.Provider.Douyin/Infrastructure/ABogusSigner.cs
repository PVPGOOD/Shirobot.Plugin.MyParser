using System.Security.Cryptography;
using System.Text;

namespace MyParser.Provider.Douyin.Infrastructure;

public static class ABogusSigner
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    public static string Generate(string queryString, string userAgent)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var payload = $"{queryString}|{userAgent}|{timestamp}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var random = RandomNumberGenerator.GetBytes(16);
        var bytes = hash.Concat(random).ToArray();

        var chars = new char[64];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[bytes[i % bytes.Length] & 0x3f];
        }

        return new string(chars);
    }
}
