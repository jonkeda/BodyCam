namespace BodyCam.Services.Glasses.HeyCyan.Media;

public enum RecordedMediaKind
{
    Photo,
    Video,
    Audio,
    Unknown
}

public sealed record RecordedMediaItem(
    string FileName,
    RecordedMediaKind Kind,
    long? SizeBytes,
    DateTimeOffset? GlassesTimestamp);

public sealed record ImportedMediaItem(
    RecordedMediaItem Source,
    string LocalUri,
    long BytesWritten,
    TimeSpan TransferTime,
    string? Sha256 = null);

public sealed record RecordedMediaImportProgress(
    int Completed,
    int Total,
    string CurrentFile,
    long BytesSoFar);
