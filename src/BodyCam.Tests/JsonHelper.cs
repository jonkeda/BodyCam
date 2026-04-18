using System.Text.Json;

namespace BodyCam.Tests;

/// <summary>
/// Helpers for converting JSON strings to JsonElement in tests.
/// </summary>
internal static class JsonHelper
{
    public static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
