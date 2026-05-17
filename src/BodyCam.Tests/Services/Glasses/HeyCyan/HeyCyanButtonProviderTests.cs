using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Input;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// Unit tests for <see cref="HeyCyanButtonProvider"/> — M33 Phase 4 Wave 1.
/// </summary>
public sealed class HeyCyanButtonProviderTests
{
    [Fact]
    public void ProviderId_IsHeyCyanGlasses()
    {
        var session = new FakeHeyCyanSession();
        var sut = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);

        sut.ProviderId.Should().Be("heycyan-glasses");
    }

    [Fact]
    public void ButtonId_IsGlassesButton()
    {
        // ButtonId is exposed via events, so we verify the constant directly
        HeyCyanButtonProvider.ButtonIdConst.Should().Be("glasses-button");
    }

    [Theory]
    [InlineData(HeyCyanState.Disconnected, false)]
    [InlineData(HeyCyanState.Scanning, false)]
    [InlineData(HeyCyanState.Connecting, false)]
    [InlineData(HeyCyanState.Connected, true)]
    [InlineData(HeyCyanState.TransferMode, true)]
    [InlineData(HeyCyanState.Disconnecting, false)]
    public void IsAvailable_ReflectsSessionState(HeyCyanState state, bool expectedAvailable)
    {
        var session = new FakeHeyCyanSession();
        var sut = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);

        // Transition to the target state
        switch (state)
        {
            case HeyCyanState.Connected:
                session.RaiseConnected("AA:BB:CC:DD:EE:FF");
                break;
            case HeyCyanState.TransferMode:
                session.RaiseConnected("AA:BB:CC:DD:EE:FF");
                session.RaiseTransferMode();
                break;
            case HeyCyanState.Disconnected:
                session.RaiseDisconnected();
                break;
            // Other states not directly testable via FakeHeyCyanSession
        }

        sut.IsAvailable.Should().Be(expectedAvailable);
    }

    [Theory]
    [InlineData(HeyCyanButtonGesture.Tap, ButtonGesture.SingleTap)]
    [InlineData(HeyCyanButtonGesture.DoubleTap, ButtonGesture.DoubleTap)]
    [InlineData(HeyCyanButtonGesture.LongPress, ButtonGesture.LongPress)]
    public async Task ButtonPressed_RaisesPreRecognizedGesture_WithCorrectMapping(
        HeyCyanButtonGesture input,
        ButtonGesture expected)
    {
        var session = new FakeHeyCyanSession();
        var sut = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await sut.StartAsync();

        ButtonGestureEvent? received = null;
        sut.PreRecognizedGesture += (_, evt) => received = evt;

        session.RaiseButtonPressed(input);

        received.Should().NotBeNull();
        received!.ProviderId.Should().Be("heycyan-glasses");
        received.ButtonId.Should().Be("glasses-button");
        received.Gesture.Should().Be(expected);
        received.TimestampMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ButtonPressed_NeverRaisesRawButtonEvent()
    {
        var session = new FakeHeyCyanSession();
        var sut = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await sut.StartAsync();

        var rawEventRaised = false;
        sut.RawButtonEvent += (_, _) => rawEventRaised = true;

        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);

        rawEventRaised.Should().BeFalse("RawButtonEvent should never be raised");
    }

    [Fact]
    public async Task StartAsync_SubscribesToButtonPressed()
    {
        var session = new FakeHeyCyanSession();
        var sut = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");

        var eventReceived = false;
        sut.PreRecognizedGesture += (_, _) => eventReceived = true;

        // Button press before start should not propagate
        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        eventReceived.Should().BeFalse("not yet started");

        await sut.StartAsync();
        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        eventReceived.Should().BeTrue("now started and subscribed");
    }

    [Fact]
    public async Task StopAsync_UnsubscribesFromButtonPressed()
    {
        var session = new FakeHeyCyanSession();
        var sut = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await sut.StartAsync();

        var eventCount = 0;
        sut.PreRecognizedGesture += (_, _) => eventCount++;

        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        eventCount.Should().Be(1);

        await sut.StopAsync();
        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        eventCount.Should().Be(1, "should not receive events after StopAsync");
    }

    [Fact]
    public async Task StopAsync_IsIdempotent()
    {
        var session = new FakeHeyCyanSession();
        var sut = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await sut.StartAsync();

        await sut.StopAsync();
        await sut.StopAsync(); // second call should not throw

        sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_TriggersStopAsync()
    {
        var session = new FakeHeyCyanSession();
        var sut = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await sut.StartAsync();

        var eventCount = 0;
        sut.PreRecognizedGesture += (_, _) => eventCount++;

        sut.Dispose();

        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        eventCount.Should().Be(0, "should not receive events after Dispose");
    }

    [Fact]
    public async Task StartAsync_IsIdempotent()
    {
        var session = new FakeHeyCyanSession();
        var sut = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");

        await sut.StartAsync();
        await sut.StartAsync(); // second call should be no-op

        sut.IsActive.Should().BeTrue();
    }

    [Fact]
    public void MapGesture_HandlesUnknownGesture_WithFallback()
    {
        var result = HeyCyanButtonProvider.MapGesture((HeyCyanButtonGesture)999);
        result.Should().Be(ButtonGesture.SingleTap, "unknown gesture should fall back to SingleTap");
    }
}
