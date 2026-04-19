using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Input;
using BodyCam.Services.Logging;
using BodyCam.Tests.TestInfrastructure.Providers;
using BodyCam.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BodyCam.Tests.TestInfrastructure;

/// <summary>
/// Builds the BodyCam service graph for integration tests — no MAUI host needed.
/// Registers test providers and stubs for platform services.
/// </summary>
public sealed class BodyCamTestHost : IDisposable, IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public IServiceProvider Services => _provider;
    public TestMicProvider Mic { get; }
    public TestSpeakerProvider Speaker { get; }
    public TestCameraProvider Camera { get; }
    public TestButtonProvider Buttons { get; }

    public AudioInputManager AudioInput => _provider.GetRequiredService<AudioInputManager>();
    public AudioOutputManager AudioOutput => _provider.GetRequiredService<AudioOutputManager>();
    public CameraManager CameraManager => _provider.GetRequiredService<CameraManager>();
    public ButtonInputManager ButtonInput => _provider.GetRequiredService<ButtonInputManager>();
    public ToolDispatcher ToolDispatcher => _provider.GetRequiredService<ToolDispatcher>();

    private BodyCamTestHost(
        ServiceProvider provider,
        TestMicProvider mic,
        TestSpeakerProvider speaker,
        TestCameraProvider camera,
        TestButtonProvider buttons)
    {
        _provider = provider;
        Mic = mic;
        Speaker = speaker;
        Camera = camera;
        Buttons = buttons;
    }

    public static BodyCamTestHost Create(Action<IServiceCollection>? configure = null)
    {
        var mic = new TestMicProvider(TestAssets.SilencePcm());
        var speaker = new TestSpeakerProvider();
        var camera = new TestCameraProvider(TestAssets.MinimalJpeg);
        var buttons = new TestButtonProvider();

        var services = new ServiceCollection();

        // AppSettings
        services.AddSingleton(new AppSettings());

        // Settings stub — configure to select test providers
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveAudioInputProvider.Returns(mic.ProviderId);
        settings.ActiveAudioOutputProvider.Returns(speaker.ProviderId);
        settings.ActiveCameraProvider.Returns(camera.ProviderId);
        services.AddSingleton(settings);

        // Test providers
        services.AddSingleton<IAudioInputProvider>(mic);
        services.AddSingleton(mic);
        services.AddSingleton<IAudioOutputProvider>(speaker);
        services.AddSingleton(speaker);
        services.AddSingleton<ICameraProvider>(camera);
        services.AddSingleton(camera);
        services.AddSingleton<IButtonInputProvider>(buttons);
        services.AddSingleton(buttons);

        // Managers
        services.AddSingleton<AudioInputManager>();
        services.AddSingleton<IAudioInputService>(sp => sp.GetRequiredService<AudioInputManager>());
        services.AddSingleton<AudioOutputManager>();
        services.AddSingleton<IAudioOutputService>(sp => sp.GetRequiredService<AudioOutputManager>());
        services.AddSingleton<CameraManager>();
        services.AddSingleton<ButtonInputManager>();

        // Realtime client stub
        services.AddSingleton(Substitute.For<IRealtimeClient>());

        // Logging
        services.AddSingleton<InAppLogSink>();
        services.AddLogging(lb => lb.AddProvider(
            new InAppLoggerProvider(new InAppLogSink(), LogLevel.Debug)));

        // Memory store (in-memory temp file)
        var tempPath = Path.Combine(Path.GetTempPath(), $"bodycam-test-{Guid.NewGuid():N}.json");
        services.AddSingleton(new MemoryStore(tempPath));

        // Tools — register only test-safe tools (no MAUI dependencies)
        services.AddSingleton<ITool, SaveMemoryTool>();
        services.AddSingleton<ITool, RecallMemoryTool>();
        services.AddSingleton<ToolDispatcher>();

        // Allow test-specific registrations
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        return new BodyCamTestHost(provider, mic, speaker, camera, buttons);
    }

    public async Task InitializeAsync()
    {
        await AudioInput.InitializeAsync();
        await AudioOutput.InitializeAsync();
        await CameraManager.InitializeAsync();
        await ButtonInput.StartAsync();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await ButtonInput.StopAsync();
        if (_provider is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            _provider.Dispose();
    }
}
