namespace BodyCam.Services.WakeWord;

/// <summary>
/// Resolves platform-specific Porcupine keyword (.ppn) file paths.
/// </summary>
public static class KeywordPathResolver
{
    /// <summary>
    /// Returns the platform-specific .ppn file path for a keyword base name.
    /// </summary>
    /// <param name="baseName">Base keyword name, e.g. "hey-bodycam"</param>
    /// <returns>Full resource path, e.g. "wakewords/hey-bodycam_en_windows.ppn"</returns>
    public static string Resolve(string baseName)
    {
        var platform = GetPlatformSuffix();
        return $"wakewords/{baseName}_en_{platform}.ppn";
    }

    internal static string GetPlatformSuffix()
    {
#if WINDOWS
        return "windows";
#elif ANDROID
        return "android";
#elif IOS
        return "ios";
#elif MACCATALYST
        return "mac";
#else
        return "windows";
#endif
    }
}
