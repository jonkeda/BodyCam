using BodyCam.Services;
using BodyCam.Services.Audio;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.Services.Audio;

public sealed class AudioRoutePolicyServiceTests
{
    [Fact]
    public async Task DirectSpeakerWithoutPlatformAec_SelectsWebRtcApm()
    {
        var service = await CreatePolicyServiceAsync(
            inputCapabilities: AudioInputCapabilities.Default,
            outputCapabilities: DirectSpeaker());

        service.Current.AecMode.Should().Be(AecMode.WebRtcApm);
        service.Current.HasLocalPlayback.Should().BeTrue();
        service.Current.OutputCapabilities.NeedsEchoCancellation.Should().BeTrue();
    }

    [Fact]
    public async Task DirectSpeakerWithPlatformAec_SelectsPlatformNative()
    {
        var service = await CreatePolicyServiceAsync(
            inputCapabilities: new AudioInputCapabilities(
                HasPlatformEchoCancellation: true,
                PlatformEchoCancellationActive: true,
                EstimatedInputLatencyMs: 0),
            outputCapabilities: DirectSpeaker());

        service.Current.AecMode.Should().Be(AecMode.PlatformNative);
    }

    [Fact]
    public async Task IsolatedHeadset_BypassesAec()
    {
        var service = await CreatePolicyServiceAsync(
            inputCapabilities: AudioInputCapabilities.Default,
            outputCapabilities: Headset());

        service.Current.AecMode.Should().Be(AecMode.Off);
        service.Current.OutputCapabilities.IsAcousticallyIsolated.Should().BeTrue();
    }

    [Fact]
    public async Task SilentMode_BypassesAecAndLocalPlayback()
    {
        var service = await CreatePolicyServiceAsync(
            inputCapabilities: AudioInputCapabilities.Default,
            outputCapabilities: DirectSpeaker(),
            outputMode: "Silent");

        service.Current.AecMode.Should().Be(AecMode.Off);
        service.Current.HasLocalPlayback.Should().BeFalse();
        service.Current.OutputCapabilities.EchoPathKind.Should().Be(EchoPathKind.NoLocalPlayback);
    }

    [Fact]
    public async Task SameProviderId_ChangingCapabilities_ChangesPolicy()
    {
        var routeMonitor = new TestRouteMonitor();
        var settings = CreateSettings("Speak");
        var appSettings = new AppSettings();
        var mic = new MutableInputProvider("mic", AudioInputCapabilities.Default);
        var speaker = new MutableOutputProvider("same-output", DirectSpeaker());
        var input = new AudioInputManager([mic], settings, NullLogger<AudioInputManager>.Instance);
        var output = new AudioOutputManager([speaker], settings, appSettings, NullLogger<AudioOutputManager>.Instance);

        await input.SetActiveAsync(mic.ProviderId);
        await output.SetActiveAsync(speaker.ProviderId);

        var service = new AudioRoutePolicyService(
            input,
            output,
            routeMonitor,
            settings,
            appSettings,
            NullLogger<AudioRoutePolicyService>.Instance);

        service.Current.AecMode.Should().Be(AecMode.WebRtcApm);

        speaker.OutputCapabilities = Headset();
        speaker.RaiseOutputRouteChanged();

        service.Current.AecMode.Should().Be(AecMode.Off);
        service.Current.OutputCapabilities.EchoPathKind.Should().Be(EchoPathKind.IsolatedHeadset);
    }

    [Fact]
    public async Task DifferentProviderIds_WithSameCapabilities_DoNotChangePolicy()
    {
        var routeMonitor = new TestRouteMonitor();
        var settings = CreateSettings("Speak");
        var appSettings = new AppSettings();
        var mic = new MutableInputProvider("mic", AudioInputCapabilities.Default);
        var firstSpeaker = new MutableOutputProvider("first-output", DirectSpeaker());
        var secondSpeaker = new MutableOutputProvider("second-output", DirectSpeaker());
        var input = new AudioInputManager([mic], settings, NullLogger<AudioInputManager>.Instance);
        var output = new AudioOutputManager([firstSpeaker, secondSpeaker], settings, appSettings, NullLogger<AudioOutputManager>.Instance);

        await input.SetActiveAsync(mic.ProviderId);
        await output.SetActiveAsync(firstSpeaker.ProviderId);

        var service = new AudioRoutePolicyService(
            input,
            output,
            routeMonitor,
            settings,
            appSettings,
            NullLogger<AudioRoutePolicyService>.Instance);

        var firstPolicy = service.Current;

        await output.SetActiveAsync(secondSpeaker.ProviderId);
        var secondPolicy = service.Current;

        secondPolicy.AecMode.Should().Be(firstPolicy.AecMode);
        secondPolicy.OutputCapabilities.Should().Be(firstPolicy.OutputCapabilities);
        secondPolicy.Explanation.Should().Be(firstPolicy.Explanation);
    }

    private static async Task<AudioRoutePolicyService> CreatePolicyServiceAsync(
        AudioInputCapabilities inputCapabilities,
        AudioOutputCapabilities outputCapabilities,
        string outputMode = "Speak")
    {
        var routeMonitor = new TestRouteMonitor();
        var settings = CreateSettings(outputMode);
        var appSettings = new AppSettings();
        var mic = new MutableInputProvider("mic", inputCapabilities);
        var speaker = new MutableOutputProvider("speaker", outputCapabilities);
        var input = new AudioInputManager([mic], settings, NullLogger<AudioInputManager>.Instance);
        var output = new AudioOutputManager([speaker], settings, appSettings, NullLogger<AudioOutputManager>.Instance);

        await input.SetActiveAsync(mic.ProviderId);
        await output.SetActiveAsync(speaker.ProviderId);

        return new AudioRoutePolicyService(
            input,
            output,
            routeMonitor,
            settings,
            appSettings,
            NullLogger<AudioRoutePolicyService>.Instance);
    }

    private static ISettingsService CreateSettings(string outputMode)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.OutputMode.Returns(outputMode);
        return settings;
    }

    private static AudioOutputCapabilities DirectSpeaker(int latencyMs = 80) => new(
        EchoPathKind.DirectDeviceSpeaker,
        NeedsEchoCancellation: true,
        IsAcousticallyIsolated: false,
        SupportsRenderReference: true,
        EstimatedOutputLatencyMs: latencyMs);

    private static AudioOutputCapabilities Headset(int latencyMs = 40) => new(
        EchoPathKind.IsolatedHeadset,
        NeedsEchoCancellation: false,
        IsAcousticallyIsolated: true,
        SupportsRenderReference: false,
        EstimatedOutputLatencyMs: latencyMs);

    private sealed class MutableInputProvider : IAudioInputProvider
    {
        public MutableInputProvider(string providerId, AudioInputCapabilities inputCapabilities)
        {
            ProviderId = providerId;
            InputCapabilities = inputCapabilities;
        }

        public string DisplayName => ProviderId;
        public string ProviderId { get; }
        public AudioInputCapabilities InputCapabilities { get; }
        public bool IsAvailable => true;
        public bool IsCapturing { get; private set; }
        public event EventHandler<byte[]>? AudioChunkAvailable;
        public event EventHandler? Disconnected;
        public Task StartAsync(CancellationToken ct = default) { IsCapturing = true; return Task.CompletedTask; }
        public Task StopAsync() { IsCapturing = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MutableOutputProvider : IAudioOutputProvider
    {
        public MutableOutputProvider(string providerId, AudioOutputCapabilities outputCapabilities)
        {
            ProviderId = providerId;
            OutputCapabilities = outputCapabilities;
        }

        public string DisplayName => ProviderId;
        public string ProviderId { get; }
        public AudioOutputCapabilities OutputCapabilities { get; set; }
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public int EstimatedOutputLatencyMs => OutputCapabilities.EstimatedOutputLatencyMs;
        public event EventHandler? OutputRouteChanged;
        public event EventHandler? Disconnected;
        public Task StartAsync(int sampleRate, CancellationToken ct = default) { IsPlaying = true; return Task.CompletedTask; }
        public Task StopAsync() { IsPlaying = false; return Task.CompletedTask; }
        public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default) => Task.CompletedTask;
        public void ClearBuffer() { }
        public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void RaiseOutputRouteChanged() => OutputRouteChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class TestRouteMonitor : IRouteMonitor
    {
        public bool IsHeadphonesConnected { get; private set; }
        public bool IsBluetoothAudioConnected { get; private set; }
        public event EventHandler? RouteChanged;

        public void SetRoute(bool headphones, bool bluetooth)
        {
            IsHeadphonesConnected = headphones;
            IsBluetoothAudioConnected = bluetooth;
            RouteChanged?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
