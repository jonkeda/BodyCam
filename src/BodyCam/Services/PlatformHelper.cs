namespace BodyCam.Services;

public static class PlatformHelper
{
    public static string GetPlatformSuffix()
    {
#if ANDROID
        return "android";
#elif IOS
        return "ios";
#else
        return "windows";
#endif
    }

    /// <summary>
    /// Converts a base keyword name to a platform-specific .ppn file path.
    /// e.g. "hey-bodycam" → "wakewords/hey-bodycam_en_windows.ppn"
    /// </summary>
    public static string GetKeywordPath(string baseName)
    {
        var platform = GetPlatformSuffix();
        return $"wakewords/{baseName}_en_{platform}.ppn";
    }
}
