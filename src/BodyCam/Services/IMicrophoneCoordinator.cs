namespace BodyCam.Services;

/// <summary>
/// Coordinates microphone handoff between wake word engine and realtime session.
/// Only one can hold the mic at a time on most platforms.
/// </summary>
public interface IMicrophoneCoordinator
{
    Task TransitionToActiveSessionAsync();
    Task TransitionToWakeWordAsync();
}
