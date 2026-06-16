namespace Shirobot.Plugin.MyParser.Utility;

internal static class MemorySafetyUtilities
{
    private const int HardBase64LimitMegabytes = 128;

    public static bool CanUseBase64ForFile(long fileSizeBytes, int configuredMaxMegabytes)
    {
        if (fileSizeBytes <= 0 || configuredMaxMegabytes <= 0)
        {
            return false;
        }

        var configuredLimit = configuredMaxMegabytes * 1024L * 1024L;
        var hardLimit = HardBase64LimitMegabytes * 1024L * 1024L;
        return fileSizeBytes <= Math.Min(configuredLimit, hardLimit);
    }
}
