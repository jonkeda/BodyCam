using BodyCam.Services.Glasses.HeyCyan;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;

/// <summary>
/// Fake HeyCyan media transfer for unit testing HeyCyanGlassesDeviceManager and camera provider.
/// </summary>
public sealed class FakeHeyCyanMediaTransfer : IHeyCyanMediaTransfer
{
    public bool IsWarm => false;

    public Task<IReadOnlyList<HeyCyanMediaEntry>> ListAsync(CancellationToken ct)
    {
        var entries = new List<HeyCyanMediaEntry>
        {
            new("photo1.jpg", 1024, DateTimeOffset.UtcNow.AddHours(-1), HeyCyanMediaKind.Photo),
            new("video1.mp4", 2048, DateTimeOffset.UtcNow.AddHours(-2), HeyCyanMediaKind.Video)
        };
        return Task.FromResult<IReadOnlyList<HeyCyanMediaEntry>>(entries);
    }

    public Task<byte[]> DownloadAsync(string fileName, CancellationToken ct)
    {
        // Return fake image data
        return Task.FromResult(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // Fake JPEG header
    }

    public Task<Stream> OpenAsync(string fileName, CancellationToken ct)
    {
        var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        return Task.FromResult<Stream>(new MemoryStream(data));
    }

    public Task ExitAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => default;
}
