using BodyCam.Services.Glasses.HeyCyan.Media;
using FluentAssertions;
using System.Text.Json;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Media;

public class RecordedMediaSidecarTests
{
    [Fact]
    public void RecordedMediaSidecar_serializes_to_json_with_correct_schema()
    {
        var sidecar = new RecordedMediaSidecar(
            Schema: 1,
            SourceFileName: "VID_0042.mp4",
            GlassesMacAddress: "AA:BB:CC:DD:EE:FF",
            ImportedAt: new DateTimeOffset(2026, 4, 30, 12, 0, 0, TimeSpan.Zero),
            GlassesTimestamp: new DateTimeOffset(2026, 4, 30, 11, 30, 0, TimeSpan.Zero),
            Duration: TimeSpan.FromSeconds(42.5),
            SizeBytes: 1024000,
            Sha256: "abcd1234");

        var json = JsonSerializer.Serialize(sidecar, BodyCam.Json.BodyCamJsonContext.Default.RecordedMediaSidecar);

        json.Should().Contain("\"schema\": 1");
        json.Should().Contain("\"sourceFileName\": \"VID_0042.mp4\"");
        json.Should().Contain("\"glassesMacAddress\": \"AA:BB:CC:DD:EE:FF\"");
        json.Should().Contain("\"sha256\": \"abcd1234\"");
    }

    [Fact]
    public void RecordedMediaSidecar_deserializes_from_json()
    {
        var json = """
        {
          "schema": 1,
          "sourceFileName": "VID_0042.mp4",
          "glassesMacAddress": "AA:BB:CC:DD:EE:FF",
          "importedAt": "2026-04-30T12:00:00+00:00",
          "glassesTimestamp": "2026-04-30T11:30:00+00:00",
          "duration": "00:00:42.5000000",
          "sizeBytes": 1024000,
          "sha256": "abcd1234"
        }
        """;

        var sidecar = JsonSerializer.Deserialize(json, BodyCam.Json.BodyCamJsonContext.Default.RecordedMediaSidecar);

        sidecar.Should().NotBeNull();
        sidecar!.Schema.Should().Be(1);
        sidecar.SourceFileName.Should().Be("VID_0042.mp4");
        sidecar.GlassesMacAddress.Should().Be("AA:BB:CC:DD:EE:FF");
        sidecar.Duration.Should().Be(TimeSpan.FromSeconds(42.5));
        sidecar.SizeBytes.Should().Be(1024000);
        sidecar.Sha256.Should().Be("abcd1234");
    }

    [Fact]
    public void RecordedMediaSidecar_handles_null_optional_fields()
    {
        var sidecar = new RecordedMediaSidecar(
            Schema: 1,
            SourceFileName: "VID_0042.mp4",
            GlassesMacAddress: "unknown",
            ImportedAt: DateTimeOffset.UtcNow,
            GlassesTimestamp: null,
            Duration: null,
            SizeBytes: 1024000,
            Sha256: "abcd1234");

        var json = JsonSerializer.Serialize(sidecar, BodyCam.Json.BodyCamJsonContext.Default.RecordedMediaSidecar);

        json.Should().Contain("\"glassesTimestamp\": null");
        json.Should().Contain("\"duration\": null");
    }

    [Fact]
    public async Task HashingStream_computes_sha256_correctly()
    {
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        using var source = new MemoryStream(testData);
        await using var hashing = new HashingStream(source);

        // Read all bytes
        using var dest = new MemoryStream();
        await hashing.CopyToAsync(dest);
        await hashing.DisposeAsync();

        // Verify content passed through
        dest.ToArray().Should().Equal(testData);

        // Verify hash matches System.Security.Cryptography
        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(testData)).ToLowerInvariant();
        hashing.GetHashHex().Should().Be(expectedHash);
    }

    [Fact]
    public async Task JsonSidecarWriter_writes_to_correct_location()
    {
        var probe = new FakeMediaDurationProbe();
        var testDir = Path.Combine(Path.GetTempPath(), "BodyCam.Tests", Guid.NewGuid().ToString());
        var writer = new JsonSidecarWriter(probe, Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonSidecarWriter>.Instance, testDir);

        var sidecar = new RecordedMediaSidecar(
            Schema: 1,
            SourceFileName: "VID_TEST.mp4",
            GlassesMacAddress: "AA:BB:CC:DD:EE:FF",
            ImportedAt: DateTimeOffset.UtcNow,
            GlassesTimestamp: null,
            Duration: null,
            SizeBytes: 100,
            Sha256: "testhash123");

        var sidecarPath = await writer.WriteAsync("content://test", sidecar, CancellationToken.None);

        sidecarPath.Should().EndWith("testhash123.bodycam.json");
        File.Exists(sidecarPath).Should().BeTrue();

        // Verify content
        var jsonText = await File.ReadAllTextAsync(sidecarPath);
        jsonText.Should().Contain("\"schema\": 1");
        jsonText.Should().Contain("\"sourceFileName\": \"VID_TEST.mp4\"");

        // Cleanup
        Directory.Delete(testDir, recursive: true);
    }

    [Fact]
    public async Task JsonSidecarWriter_overwrites_existing_sidecar()
    {
        var probe = new FakeMediaDurationProbe();
        var testDir = Path.Combine(Path.GetTempPath(), "BodyCam.Tests", Guid.NewGuid().ToString());
        var writer = new JsonSidecarWriter(probe, Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonSidecarWriter>.Instance, testDir);

        var sidecar1 = new RecordedMediaSidecar(
            Schema: 1,
            SourceFileName: "VID_OLD.mp4",
            GlassesMacAddress: "AA:BB:CC:DD:EE:FF",
            ImportedAt: DateTimeOffset.UtcNow.AddDays(-1),
            GlassesTimestamp: null,
            Duration: null,
            SizeBytes: 100,
            Sha256: "samehash");

        var sidecar2 = new RecordedMediaSidecar(
            Schema: 1,
            SourceFileName: "VID_NEW.mp4",
            GlassesMacAddress: "AA:BB:CC:DD:EE:FF",
            ImportedAt: DateTimeOffset.UtcNow,
            GlassesTimestamp: null,
            Duration: null,
            SizeBytes: 200,
            Sha256: "samehash"); // Same hash => same sidecar file

        var path1 = await writer.WriteAsync("content://old", sidecar1, CancellationToken.None);
        var path2 = await writer.WriteAsync("content://new", sidecar2, CancellationToken.None);

        path1.Should().Be(path2);

        // Verify second write overwrote first
        var jsonText = await File.ReadAllTextAsync(path2);
        jsonText.Should().Contain("\"sourceFileName\": \"VID_NEW.mp4\"");
        jsonText.Should().NotContain("VID_OLD.mp4");

        // Cleanup
        Directory.Delete(testDir, recursive: true);
    }

    [Fact]
    public async Task JsonSidecarWriter_calls_duration_probe()
    {
        var probe = new FakeMediaDurationProbe { DurationToReturn = TimeSpan.FromSeconds(123) };
        var testDir = Path.Combine(Path.GetTempPath(), "BodyCam.Tests", Guid.NewGuid().ToString());
        var writer = new JsonSidecarWriter(probe, Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonSidecarWriter>.Instance, testDir);

        var sidecar = new RecordedMediaSidecar(
            Schema: 1,
            SourceFileName: "VID_TEST.mp4",
            GlassesMacAddress: "AA:BB:CC:DD:EE:FF",
            ImportedAt: DateTimeOffset.UtcNow,
            GlassesTimestamp: null,
            Duration: null, // Not set
            SizeBytes: 100,
            Sha256: "probehash");

        var sidecarPath = await writer.WriteAsync("content://probe", sidecar, CancellationToken.None);

        probe.ProbeCallCount.Should().Be(1);
        probe.LastProbedUri.Should().Be("content://probe");

        // Verify duration was written
        var jsonText = await File.ReadAllTextAsync(sidecarPath);
        jsonText.Should().Contain("\"duration\": \"00:02:03\"");

        // Cleanup
        Directory.Delete(testDir, recursive: true);
    }

    [Fact]
    public async Task JsonSidecarWriter_skips_probe_if_duration_already_set()
    {
        var probe = new FakeMediaDurationProbe { DurationToReturn = TimeSpan.FromSeconds(999) };
        var testDir = Path.Combine(Path.GetTempPath(), "BodyCam.Tests", Guid.NewGuid().ToString());
        var writer = new JsonSidecarWriter(probe, Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonSidecarWriter>.Instance, testDir);

        var sidecar = new RecordedMediaSidecar(
            Schema: 1,
            SourceFileName: "VID_TEST.mp4",
            GlassesMacAddress: "AA:BB:CC:DD:EE:FF",
            ImportedAt: DateTimeOffset.UtcNow,
            GlassesTimestamp: null,
            Duration: TimeSpan.FromSeconds(42), // Already set
            SizeBytes: 100,
            Sha256: "noprobehash");

        var sidecarPath = await writer.WriteAsync("content://noprobe", sidecar, CancellationToken.None);

        probe.ProbeCallCount.Should().Be(0);

        // Verify original duration was preserved
        var jsonText = await File.ReadAllTextAsync(sidecarPath);
        jsonText.Should().Contain("\"duration\": \"00:00:42\"");

        // Cleanup
        Directory.Delete(testDir, recursive: true);
    }

    // Helpers

    private sealed class FakeMediaDurationProbe : IMediaDurationProbe
    {
        public TimeSpan? DurationToReturn { get; set; }
        public int ProbeCallCount { get; private set; }
        public string? LastProbedUri { get; private set; }

        public Task<TimeSpan?> ProbeAsync(string localUri, CancellationToken ct)
        {
            ProbeCallCount++;
            LastProbedUri = localUri;
            return Task.FromResult(DurationToReturn);
        }
    }
}
