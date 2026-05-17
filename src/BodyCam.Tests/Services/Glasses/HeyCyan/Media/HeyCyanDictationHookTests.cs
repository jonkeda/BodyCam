using BodyCam.Services.Dictation;
using BodyCam.Services.Glasses.HeyCyan.Media;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Media;

public class HeyCyanDictationHookTests
{
    [Fact]
    public async Task StartAsync_with_null_registry_logs_and_returns()
    {
        var media = new FakeRecordedMediaService();
        var hook = new HeyCyanDictationHook(media, registry: null, NullLogger<HeyCyanDictationHook>.Instance);

        await hook.StartAsync(CancellationToken.None);

        // No exception — null registry is tolerated
        media.SubscriberCount.Should().Be(0, "hook should not subscribe when registry is null");
    }

    [Fact]
    public async Task StartAsync_with_registry_subscribes_to_AudioImported()
    {
        var media = new FakeRecordedMediaService();
        var registry = new FakeDictationRegistry();
        var hook = new HeyCyanDictationHook(media, registry, NullLogger<HeyCyanDictationHook>.Instance);

        await hook.StartAsync(CancellationToken.None);

        media.SubscriberCount.Should().Be(1, "hook should subscribe when registry is present");
    }

    [Fact]
    public async Task OnAudioImported_registers_voice_note_with_M16()
    {
        var media = new FakeRecordedMediaService();
        var registry = new FakeDictationRegistry();
        var hook = new HeyCyanDictationHook(media, registry, NullLogger<HeyCyanDictationHook>.Instance);

        await hook.StartAsync(CancellationToken.None);

        var item = new ImportedMediaItem(
            new RecordedMediaItem("test.opus", RecordedMediaKind.Audio, 1234, DateTimeOffset.UtcNow),
            "file:///data/audio/test.ogg",
            1234,
            TimeSpan.FromSeconds(1),
            Sha256: "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890");

        media.RaiseAudioImported(item);

        registry.Registrations.Should().HaveCount(1);
        registry.Registrations[0].SourceId.Should().Be("heycyan-voicenote");
        registry.Registrations[0].LocalUri.Should().Be("file:///data/audio/test.ogg");
        registry.Registrations[0].Sha256.Should().Be("abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890");
    }

    [Fact]
    public async Task OnAudioImported_deduplicates_by_sha256()
    {
        var media = new FakeRecordedMediaService();
        var registry = new FakeDictationRegistry();
        var hook = new HeyCyanDictationHook(media, registry, NullLogger<HeyCyanDictationHook>.Instance);

        await hook.StartAsync(CancellationToken.None);

        var sha = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var item1 = new ImportedMediaItem(
            new RecordedMediaItem("test1.opus", RecordedMediaKind.Audio, 1234, DateTimeOffset.UtcNow),
            "file:///data/audio/test1.ogg",
            1234,
            TimeSpan.FromSeconds(1),
            Sha256: sha);
        var item2 = new ImportedMediaItem(
            new RecordedMediaItem("test2.opus", RecordedMediaKind.Audio, 1234, DateTimeOffset.UtcNow),
            "file:///data/audio/test2.ogg",
            1234,
            TimeSpan.FromSeconds(1),
            Sha256: sha);

        media.RaiseAudioImported(item1);
        media.RaiseAudioImported(item2);

        registry.Registrations.Should().HaveCount(1, "same SHA-256 should be registered only once");
    }

    [Fact]
    public async Task OnAudioImported_skips_when_sha256_is_null()
    {
        var media = new FakeRecordedMediaService();
        var registry = new FakeDictationRegistry();
        var hook = new HeyCyanDictationHook(media, registry, NullLogger<HeyCyanDictationHook>.Instance);

        await hook.StartAsync(CancellationToken.None);

        var item = new ImportedMediaItem(
            new RecordedMediaItem("test.opus", RecordedMediaKind.Audio, 1234, DateTimeOffset.UtcNow),
            "file:///data/audio/test.ogg",
            1234,
            TimeSpan.FromSeconds(1),
            Sha256: null);

        media.RaiseAudioImported(item);

        registry.Registrations.Should().BeEmpty("null SHA-256 should be skipped");
    }

    [Fact]
    public async Task StopAsync_unsubscribes_from_AudioImported()
    {
        var media = new FakeRecordedMediaService();
        var registry = new FakeDictationRegistry();
        var hook = new HeyCyanDictationHook(media, registry, NullLogger<HeyCyanDictationHook>.Instance);

        await hook.StartAsync(CancellationToken.None);
        media.SubscriberCount.Should().Be(1);

        await hook.StopAsync(CancellationToken.None);

        media.SubscriberCount.Should().Be(0, "hook should unsubscribe on stop");
    }

    [Fact]
    public void Dispose_unsubscribes_from_AudioImported()
    {
        var media = new FakeRecordedMediaService();
        var registry = new FakeDictationRegistry();
        var hook = new HeyCyanDictationHook(media, registry, NullLogger<HeyCyanDictationHook>.Instance);

        hook.StartAsync(CancellationToken.None).Wait();
        media.SubscriberCount.Should().Be(1);

        hook.Dispose();

        media.SubscriberCount.Should().Be(0, "hook should unsubscribe on dispose");
    }

    // --- Fakes ---

    private sealed class FakeRecordedMediaService : IHeyCyanRecordedMediaService
    {
        public event EventHandler<ImportedMediaItem>? AudioImported;

        public int SubscriberCount => AudioImported?.GetInvocationList().Length ?? 0;

        public void RaiseAudioImported(ImportedMediaItem item) => AudioImported?.Invoke(this, item);

        public IAsyncEnumerable<RecordedMediaItem> EnumerateAsync(CancellationToken ct)
            => throw new NotImplementedException();

        public IAsyncEnumerable<ImportedMediaItem> ImportAllAsync(IProgress<RecordedMediaImportProgress>? progress, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ImportedMediaItem> ImportAsync(RecordedMediaItem item, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<bool> DeleteRemoteAsync(string fileName, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class FakeDictationRegistry : IDictationRegistry
    {
        public List<(string SourceId, string LocalUri, string Sha256)> Registrations { get; } = new();

        public void Register(IDictationSource source, string localUri, string sha256)
        {
            Registrations.Add((source.SourceId, localUri, sha256));
        }
    }
}
