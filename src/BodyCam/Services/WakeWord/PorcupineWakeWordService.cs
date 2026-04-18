using Pv;

namespace BodyCam.Services.WakeWord;

/// <summary>
/// Wake word detection using the Picovoice Porcupine engine.
/// Runs on-device with ~10mW power draw and &lt;50ms latency.
/// </summary>
public sealed class PorcupineWakeWordService : IWakeWordService, IDisposable
{
    private readonly IAudioInputService _audioInput;
    private Porcupine? _porcupine;
    private PorcupineAudioAdapter? _adapter;
    private List<WakeWordEntry> _entries = [];
    private string? _accessKey;

    public bool IsListening { get; private set; }
    public event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    public PorcupineWakeWordService(IAudioInputService audioInput)
    {
        _audioInput = audioInput;
    }

    public void RegisterKeywords(IEnumerable<WakeWordEntry> entries)
    {
        _entries = entries.ToList();
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsListening) return Task.CompletedTask;
        if (_entries.Count == 0) return Task.CompletedTask;

        var accessKey = ResolveAccessKey();
        if (string.IsNullOrWhiteSpace(accessKey))
            throw new InvalidOperationException(
                "Picovoice AccessKey not configured. Set PICOVOICE_ACCESS_KEY environment variable, " +
                "add it to .env, or configure in Settings.");

        var keywordPaths = _entries.Select(e => e.KeywordPath).ToList();
        var sensitivities = _entries.Select(e => e.Sensitivity).ToList();

        Porcupine? porcupine = null;
        try
        {
            porcupine = Porcupine.FromKeywordPaths(
                accessKey, keywordPaths, sensitivities: sensitivities);

            _adapter = new PorcupineAudioAdapter(porcupine.FrameLength);
            _porcupine = porcupine;
            porcupine = null; // Prevent dispose in finally — ownership transferred

            _audioInput.AudioChunkAvailable += OnAudioChunk;
            IsListening = true;
        }
        finally
        {
            porcupine?.Dispose(); // Only disposes if ownership was NOT transferred
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsListening) return Task.CompletedTask;

        _audioInput.AudioChunkAvailable -= OnAudioChunk;

        _adapter?.Reset();
        _adapter = null;

        _porcupine?.Dispose();
        _porcupine = null;

        IsListening = false;

        return Task.CompletedTask;
    }

    private void OnAudioChunk(object? sender, byte[] chunk)
    {
        if (_porcupine is null || _adapter is null) return;

        foreach (var frame in _adapter.Process(chunk))
        {
            int keywordIndex = _porcupine.Process(frame);
            if (keywordIndex >= 0 && keywordIndex < _entries.Count)
            {
                var entry = _entries[keywordIndex];
                WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs
                {
                    Action = entry.Action,
                    Keyword = entry.Label,
                    ToolName = entry.ToolName,
                });
            }
        }
    }

    private string? ResolveAccessKey()
    {
        if (_accessKey is not null)
            return _accessKey;

        // 1. Environment variable
        _accessKey = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY");
        if (!string.IsNullOrWhiteSpace(_accessKey))
            return _accessKey;

        // 2. .env file
        _accessKey = DotEnvReader.Read("PICOVOICE_ACCESS_KEY");
        if (!string.IsNullOrWhiteSpace(_accessKey))
            return _accessKey;

        // 3. SecureStorage (set via Settings page)
        try
        {
            _accessKey = SecureStorage.Default.GetAsync("picovoice_access_key")
                .GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // SecureStorage may not be available in test environments
        }

        return _accessKey;
    }

    public void Dispose()
    {
        if (IsListening)
            StopAsync().GetAwaiter().GetResult();
    }
}
