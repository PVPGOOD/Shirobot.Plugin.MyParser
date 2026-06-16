namespace Shirobot.Plugin.MyParser.Utility;

internal static class MediaUriUtilities
{
    public static string GetUriMode(string uri)
    {
        if (uri.StartsWith("base64://", StringComparison.OrdinalIgnoreCase)) return "base64";
        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return "file";
        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "http";
        return "unknown";
    }

    public static string PreviewUri(string? uri, int maxLength = 180)
    {
        if (string.IsNullOrWhiteSpace(uri)) return string.Empty;
        if (uri.StartsWith("base64://", StringComparison.OrdinalIgnoreCase)) return "base64://...";
        return uri.Length <= maxLength ? uri : uri[..maxLength] + "...";
    }

    public static string GuessImageExtension(string? contentType, string url, byte[] bytes)
    {
        if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase)) return ".png";
        if (string.Equals(contentType, "image/webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
        if (string.Equals(contentType, "image/gif", StringComparison.OrdinalIgnoreCase)) return ".gif";
        if (bytes.Length >= 12
            && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
        {
            return ".webp";
        }

        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? ext
            : ".jpg";
    }
}
