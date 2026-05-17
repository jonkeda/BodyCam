using System.Globalization;
using System.Text.RegularExpressions;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Parses the plaintext /files/media.config response from HeyCyan glasses.
/// Format: one filename per line, e.g.:
///   IMG_20260430_123045.jpg
///   VID_20260430_123100.mp4
///   REC_20260430_123200.opus
/// </summary>
internal static partial class MediaConfigParser
{
    public static IReadOnlyList<HeyCyanMediaEntry> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<HeyCyanMediaEntry>();

        var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var entries = new List<HeyCyanMediaEntry>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var name = line.Trim();
            var kind = ClassifyByExtension(name);
            var timestamp = TryParseTimestamp(name) ?? DateTimeOffset.UtcNow;

            // Size is unknown from media.config — set to -1.
            // Caller can HEAD the file if size is needed.
            entries.Add(new HeyCyanMediaEntry(name, Size: -1, timestamp, kind));
        }

        return entries;
    }

    private static HeyCyanMediaKind ClassifyByExtension(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => HeyCyanMediaKind.Photo,
            ".mp4" => HeyCyanMediaKind.Video,
            ".opus" => HeyCyanMediaKind.Audio,
            _ => HeyCyanMediaKind.Other,
        };
    }

    /// <summary>
    /// Try to parse a timestamp from the filename.
    /// Common patterns: IMG_20260430_123045.jpg, VID_20260430_123100.mp4, etc.
    /// If parsing fails, returns null (caller should use current time).
    /// </summary>
    private static DateTimeOffset? TryParseTimestamp(string name)
    {
        // Match pattern: {prefix}_{YYYYMMDD}_{HHMMSS}.{ext}
        var match = TimestampPattern().Match(name);
        if (!match.Success)
            return null;

        var datePart = match.Groups[1].Value; // YYYYMMDD
        var timePart = match.Groups[2].Value; // HHMMSS

        if (datePart.Length != 8 || timePart.Length != 6)
            return null;

        var year = int.Parse(datePart.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var month = int.Parse(datePart.AsSpan(4, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(datePart.AsSpan(6, 2), CultureInfo.InvariantCulture);
        var hour = int.Parse(timePart.AsSpan(0, 2), CultureInfo.InvariantCulture);
        var minute = int.Parse(timePart.AsSpan(2, 2), CultureInfo.InvariantCulture);
        var second = int.Parse(timePart.AsSpan(4, 2), CultureInfo.InvariantCulture);

        try
        {
            return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"_(\d{8})_(\d{6})\.", RegexOptions.Compiled)]
    private static partial Regex TimestampPattern();
}
