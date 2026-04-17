using BodyCam.Services;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services;

public class MicrophoneCoordinatorTests
{
    [Fact]
    public async Task TransitionToActiveSession_StopsWakeWord()
    {
        var wakeWord = Substitute.For<IWakeWordService>();
        wakeWord.IsListening.Returns(true);
        var coordinator = new MicrophoneCoordinator(wakeWord);

        await coordinator.TransitionToActiveSessionAsync();

        await wakeWord.Received(1).StopAsync();
    }

    [Fact]
    public async Task TransitionToActiveSession_SkipsIfNotListening()
    {
        var wakeWord = Substitute.For<IWakeWordService>();
        wakeWord.IsListening.Returns(false);
        var coordinator = new MicrophoneCoordinator(wakeWord);

        await coordinator.TransitionToActiveSessionAsync();

        await wakeWord.DidNotReceive().StopAsync();
    }

    [Fact]
    public async Task TransitionToWakeWord_StartsWakeWord()
    {
        var wakeWord = Substitute.For<IWakeWordService>();
        wakeWord.IsListening.Returns(false);
        var coordinator = new MicrophoneCoordinator(wakeWord);

        await coordinator.TransitionToWakeWordAsync();

        await wakeWord.Received(1).StartAsync();
    }

    [Fact]
    public async Task TransitionToWakeWord_SkipsIfAlreadyListening()
    {
        var wakeWord = Substitute.For<IWakeWordService>();
        wakeWord.IsListening.Returns(true);
        var coordinator = new MicrophoneCoordinator(wakeWord);

        await coordinator.TransitionToWakeWordAsync();

        await wakeWord.DidNotReceive().StartAsync();
    }
}
