using BodyCam.Services.Dictation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Optional hook that feeds HeyCyan voice notes into the M16 dictation pipeline.
/// Only instantiated when FeedVoiceNotesToDictation is true.
/// Null-tolerant: if M16 is absent (IDictationRegistry is null), this hook is a no-op.
/// </summary>
public sealed class HeyCyanDictationHook : IHostedService, IDisposable
{
    private readonly IHeyCyanRecordedMediaService _media;
    private readonly IDictationRegistry? _registry;
    private readonly ILogger<HeyCyanDictationHook> _log;
    private readonly HeyCyanDictationSource _source = new();
    private readonly HashSet<string> _seenHashes = new();

    public HeyCyanDictationHook(
        IHeyCyanRecordedMediaService media,
        IDictationRegistry? registry,
        ILogger<HeyCyanDictationHook> log)
    {
        _media = media;
        _registry = registry;
        _log = log;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_registry is null)
        {
            _log.LogDebug("HeyCyanDictationHook: IDictationRegistry is null (M16 not present), hook is no-op");
            return Task.CompletedTask;
        }

        _log.LogInformation("HeyCyanDictationHook: subscribing to AudioImported events");
        _media.AudioImported += OnAudioImported;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _media.AudioImported -= OnAudioImported;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _media.AudioImported -= OnAudioImported;
    }

    private void OnAudioImported(object? sender, ImportedMediaItem e)
    {
        if (_registry is null)
        {
            return;
        }

        // Skip if no SHA-256 (shouldn't happen for audio, but defensive)
        if (e.Sha256 is null)
        {
            _log.LogWarning("AudioImported event has null SHA-256, skipping: {Uri}", e.LocalUri);
            return;
        }

        // Dedup: skip if already registered this SHA-256
        if (!_seenHashes.Add(e.Sha256))
        {
            _log.LogDebug("Voice note {Sha256Prefix}... already registered, skipping", e.Sha256[..12]);
            return;
        }

        // Register with M16
        _log.LogInformation("Registering voice note {Sha256Prefix}... with M16 dictation: {Uri}",
            e.Sha256[..12], e.LocalUri);

        try
        {
            _registry.Register(_source, e.LocalUri, e.Sha256);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to register voice note {Sha256Prefix}... with M16", e.Sha256[..12]);
            // Remove from seen set so we can retry on next import
            _seenHashes.Remove(e.Sha256);
        }
    }
}
