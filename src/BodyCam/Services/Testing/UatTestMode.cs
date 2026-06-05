using System.Runtime.CompilerServices;
using BodyCam.Services.AiProviders;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Camera.Commands;
using BodyCam.Services.Input;
using Microsoft.Extensions.AI;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

#pragma warning disable CS0067 // UAT providers expose interface events for test simulation and disconnect parity.

namespace BodyCam.Services.Testing;

public static class UatTestMode
{
    public const string TestModeVariable = "BODYCAM_TEST_MODE";
    public const string AssetsVariable = "BODYCAM_UAT_ASSETS";
    public const string ReportsVariable = "BODYCAM_UAT_REPORTS";
    public const string LiveApiVariable = "BODYCAM_UAT_LIVE_API";
    public const string HardwareVariable = "BODYCAM_UAT_HARDWARE";
    public const string ModeName = "uat";

    public static bool IsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(TestModeVariable),
            ModeName,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsLiveApiEnabled => IsEnabledFlag(LiveApiVariable);

    public static string AssetsDirectory =>
        Environment.GetEnvironmentVariable(AssetsVariable)
        ?? Path.Combine(FileSystem.AppDataDirectory, "uat-assets");

    public static string ReportsDirectory =>
        Environment.GetEnvironmentVariable(ReportsVariable)
        ?? Path.Combine(FileSystem.AppDataDirectory, "uat-reports");

    public static void ApplySettings(ISettingsService settings, AppSettings appSettings)
    {
        if (!IsEnabled)
            return;

        EnsureDirectories();

        settings.SetupCompleted = true;
        settings.OutputMode = "Silent";
        settings.DebugMode = false;
        settings.SendDiagnosticData = false;
        settings.SendCrashReports = false;
        settings.SendUsageData = false;
        settings.ConfirmExternalScanActions = false;
        settings.ActiveCameraProvider = UatCameraProvider.ProviderIdConst;
        settings.ActiveAudioInputProvider = UatSilentAudioInputProvider.ProviderIdConst;
        settings.ActiveAudioOutputProvider = UatCapturingAudioOutputProvider.ProviderIdConst;
        settings.DefaultTouchCommandMode = CameraCommandMode.FullAuto;
        settings.DefaultLookDetailLevel = LookDetailLevel.Overview;
        settings.DefaultReadDetailLevel = ReadDetailLevel.Full;

        settings.DeviceSettings = new BodyCam.Models.DeviceSettings
        {
            ActiveProfileId = "custom",
            Active =
            {
                CameraProviderId = UatCameraProvider.ProviderIdConst,
                AudioInputProviderId = UatSilentAudioInputProvider.ProviderIdConst,
                AudioOutputProviderId = UatCapturingAudioOutputProvider.ProviderIdConst
            },
            Custom =
            {
                CameraProviderId = UatCameraProvider.ProviderIdConst,
                AudioInputProviderId = UatSilentAudioInputProvider.ProviderIdConst,
                AudioOutputProviderId = UatCapturingAudioOutputProvider.ProviderIdConst
            }
        };

        appSettings.ProviderId = settings.ProviderId;
        appSettings.ChatModel = settings.ChatModel;
        appSettings.VisionModel = settings.VisionModel;
        appSettings.RealtimeModel = settings.RealtimeModel;
        appSettings.TranscriptionModel = settings.TranscriptionModel;
        appSettings.Voice = settings.Voice;
        appSettings.TurnDetection = settings.TurnDetection;
        appSettings.NoiseReduction = settings.NoiseReduction;
        appSettings.SystemInstructions = settings.SystemInstructions;
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AssetsDirectory);
        Directory.CreateDirectory(ReportsDirectory);
    }

    private static bool IsEnabledFlag(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class UatCameraProvider : ICameraProvider
{
    public const string ProviderIdConst = "uat-camera";
    private static readonly byte[] FallbackFrame =
        Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQAAAQABAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

    private readonly object _gate = new();
    private IReadOnlyList<byte[]> _frames = [FallbackFrame];
    private int _nextFrame;

    public string DisplayName => "UAT Camera";
    public string ProviderId => ProviderIdConst;
    public bool IsAvailable => true;
    public bool SupportsVideoRecording => false;
    public bool IsStarted { get; private set; }

    public event EventHandler? Disconnected;

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _frames = LoadFrames();
        IsStarted = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsStarted = false;
        return Task.CompletedTask;
    }

    public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<byte[]?>(NextFrame());
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            yield return NextFrame();
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }
    }

    public ValueTask DisposeAsync()
    {
        IsStarted = false;
        return ValueTask.CompletedTask;
    }

    private byte[] NextFrame()
    {
        lock (_gate)
        {
            var frames = _frames.Count == 0 ? [FallbackFrame] : _frames;
            var frame = frames[_nextFrame % frames.Count];
            _nextFrame = (_nextFrame + 1) % frames.Count;
            return frame.ToArray();
        }
    }

    private static IReadOnlyList<byte[]> LoadFrames()
    {
        try
        {
            var cameraDirectory = Path.Combine(UatTestMode.AssetsDirectory, "camera");
            if (!Directory.Exists(cameraDirectory))
                return [FallbackFrame];

            var files = Directory
                .EnumerateFiles(cameraDirectory, "*.jpg")
                .Concat(Directory.EnumerateFiles(cameraDirectory, "*.jpeg"))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var frames = files
                .Select(File.ReadAllBytes)
                .Where(bytes => bytes.Length > 0)
                .ToArray();

            return frames.Length == 0 ? [FallbackFrame] : frames;
        }
        catch
        {
            return [FallbackFrame];
        }
    }
}

public sealed class UatSilentAudioInputProvider : IAudioInputProvider
{
    public const string ProviderIdConst = "uat-mic";

    public string DisplayName => "UAT Silent Microphone";
    public string ProviderId => ProviderIdConst;
    public AudioInputCapabilities InputCapabilities => AudioInputCapabilities.Default;
    public bool IsAvailable => true;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsCapturing = false;
        return Task.CompletedTask;
    }

    public void InjectPcm(byte[] pcm) =>
        AudioChunkAvailable?.Invoke(this, pcm.ToArray());

    public ValueTask DisposeAsync()
    {
        IsCapturing = false;
        return ValueTask.CompletedTask;
    }
}

public sealed class UatCapturingAudioOutputProvider : IAudioOutputProvider
{
    public const string ProviderIdConst = "uat-speaker";
    private readonly List<byte[]> _chunks = [];

    public string DisplayName => "UAT Capturing Speaker";
    public string ProviderId => ProviderIdConst;
    public AudioOutputCapabilities OutputCapabilities => AudioOutputCapabilities.NoLocalPlayback;
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }
    public int EstimatedOutputLatencyMs => 0;
    public int SampleRate { get; private set; }
    public int ChunkCount { get; private set; }
    public long ByteCount { get; private set; }
    public IReadOnlyList<byte[]> CapturedChunks => _chunks.AsReadOnly();

    public event EventHandler? OutputRouteChanged;
    public event EventHandler? Disconnected;

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        SampleRate = sampleRate;
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var copy = pcmData.ToArray();
        _chunks.Add(copy);
        ChunkCount++;
        ByteCount += copy.Length;
        return Task.CompletedTask;
    }

    public void ClearBuffer()
    {
        _chunks.Clear();
        ChunkCount = 0;
        ByteCount = 0;
    }

    public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ClearBuffer();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsPlaying = false;
        ClearBuffer();
        return ValueTask.CompletedTask;
    }
}

public sealed class UatButtonInputProvider : IButtonInputProvider
{
    public const string ProviderIdConst = "uat-buttons";

    public string DisplayName => "UAT Buttons";
    public string ProviderId => ProviderIdConst;
    public bool IsAvailable => true;
    public bool IsActive { get; private set; }
    public IReadOnlyList<ButtonDescriptor> Buttons { get; } =
    [
        new("primary", "Primary", [ButtonGesture.SingleTap, ButtonGesture.DoubleTap, ButtonGesture.LongPress]),
        new("secondary", "Secondary", [ButtonGesture.SingleTap, ButtonGesture.DoubleTap])
    ];

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;
    public event EventHandler? Disconnected;

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    public void SimulateRaw(RawButtonEventType eventType, string buttonId = "primary") =>
        RawButtonEvent?.Invoke(this, new RawButtonEvent
        {
            ProviderId = ProviderId,
            EventType = eventType,
            ButtonId = buttonId,
            TimestampMs = Environment.TickCount64
        });

    public void SimulateGesture(ButtonGesture gesture, string buttonId = "primary") =>
        PreRecognizedGesture?.Invoke(this, new ButtonGestureEvent
        {
            ProviderId = ProviderId,
            Gesture = gesture,
            ButtonId = buttonId,
            TimestampMs = Environment.TickCount64
        });

    public void Dispose() => IsActive = false;
}

public sealed class UatChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var messages = chatMessages.ToArray();
        var text = CreateResponseText(messages);
        return Task.FromResult(new ChatResponse(new AiChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static string CreateResponseText(IReadOnlyList<AiChatMessage> messages)
    {
        if (messages.Any(ContainsImageContent))
        {
            var prompt = LastUserText(messages);
            if (prompt.Contains("text", StringComparison.OrdinalIgnoreCase)
                || prompt.Contains("read", StringComparison.OrdinalIgnoreCase))
            {
                return "UAT readable text.";
            }

            if (prompt.Contains("find", StringComparison.OrdinalIgnoreCase)
                || prompt.Contains("objects", StringComparison.OrdinalIgnoreCase))
            {
                return "UAT frame contains a desk, a doorway, and a high-contrast test marker.";
            }

            return "UAT frame shows a deterministic indoor scene with stable lighting.";
        }

        var userText = LastUserText(messages);
        if (string.IsNullOrWhiteSpace(userText))
            return "UAT scripted response.";

        return $"UAT scripted response: {userText}";
    }

    private static bool ContainsImageContent(AiChatMessage message) =>
        message.Contents.OfType<DataContent>()
            .Any(content => content.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true);

    private static string LastUserText(IReadOnlyList<AiChatMessage> messages)
    {
        var userMessage = messages.LastOrDefault(message => message.Role == ChatRole.User);
        if (userMessage is null)
            return string.Empty;

        var contentText = string.Join(
            " ",
            userMessage.Contents.OfType<TextContent>().Select(content => content.Text));
        return string.IsNullOrWhiteSpace(contentText) ? userMessage.Text ?? string.Empty : contentText;
    }
}

#pragma warning restore CS0067
