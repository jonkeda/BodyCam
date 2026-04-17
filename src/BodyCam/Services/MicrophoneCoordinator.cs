namespace BodyCam.Services;

public class MicrophoneCoordinator : IMicrophoneCoordinator
{
    private readonly IWakeWordService _wakeWord;
    private static readonly TimeSpan MicReleaseDelay = TimeSpan.FromMilliseconds(50);

    public MicrophoneCoordinator(IWakeWordService wakeWord)
    {
        _wakeWord = wakeWord;
    }

    public async Task TransitionToActiveSessionAsync()
    {
        // Release mic from wake word engine
        if (_wakeWord.IsListening)
            await _wakeWord.StopAsync();

        // Brief delay for mic release on platforms that need it
        await Task.Delay(MicReleaseDelay);
    }

    public async Task TransitionToWakeWordAsync()
    {
        // Brief delay for mic release
        await Task.Delay(MicReleaseDelay);

        // Restart wake word listening
        if (!_wakeWord.IsListening)
            await _wakeWord.StartAsync();
    }
}
