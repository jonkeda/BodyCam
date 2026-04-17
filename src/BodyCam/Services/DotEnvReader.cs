namespace BodyCam.Services;

public static class DotEnvReader
{
    public static string? Read(string key)
    {
        // Try .env file in base directory and walking up to repo root
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var envPath = Path.Combine(dir, ".env");
            if (File.Exists(envPath))
                return ReadKey(envPath, key);
            dir = Path.GetDirectoryName(dir);
        }

        // Fall back to environment variable
        return Environment.GetEnvironmentVariable(key);
    }

    private static string? ReadKey(string envPath, string key)
    {
        foreach (var line in File.ReadLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || !trimmed.Contains('='))
                continue;

            var eqIndex = trimmed.IndexOf('=');
            var envKey = trimmed[..eqIndex].Trim();
            var envVal = trimmed[(eqIndex + 1)..].Trim();

            if (envKey == key && envVal.Length > 0)
                return envVal;
        }

        return null;
    }
}
