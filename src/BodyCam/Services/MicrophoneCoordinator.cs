namespace BodyCam.Services;

public class MicrophoneCoordinator : IMicrophoneCoordinator
{
    private readonly IWakeWordService _wakeWord;
    private readonly AppSettings _settings;

    public MicrophoneCoordinator(IWakeWordService wakeWord, AppSettings settings)
    {
        _wakeWord = wakeWord;
        _settings = settings;
    }

    public async Task TransitionToActiveSessionAsync()
    {
        if (_wakeWord.IsListening)
            await _wakeWord.StopAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(_settings.MicReleaseDelayMs));
    }

    public async Task TransitionToWakeWordAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(_settings.MicReleaseDelayMs));

        if (!_wakeWord.IsListening)
            await _wakeWord.StartAsync();
    }
}
