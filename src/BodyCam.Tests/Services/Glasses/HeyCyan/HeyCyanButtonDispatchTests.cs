using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Input;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// End-to-end button dispatch tests — M33 Phase 4 Wave 4.
/// Verifies that HeyCyanButtonProvider + ActionMap + ButtonInputManager
/// correctly dispatch mapped actions and respond to live remapping.
/// </summary>
public sealed class HeyCyanButtonDispatchTests
{
    [Fact]
    public async Task RemappedGesture_TriggersNewAction_LiveWithoutRestart()
    {
        var session  = new FakeHeyCyanSession();
        var provider = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        var actionMap = new ActionMap();
        HeyCyanButtonDefaults.SeedDefaults(actionMap);

        var manager = new ButtonInputManager(
            new IButtonInputProvider[] { provider },
            actionMap,
            NullLogger<ButtonInputManager>.Instance);

        session.RaiseConnected("AA:BB:CC:DD:EE:FF"); // Provider must be IsAvailable
        await manager.StartAsync();

        ButtonAction? triggered = null;
        manager.ActionTriggered += (_, e) => triggered = e.Action;

        // Default: SingleTap → ToggleConversation
        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        triggered.Should().Be(ButtonAction.ToggleConversation);

        // User remap: SingleTap → Photo
        actionMap.SetAction("heycyan-glasses:glasses-button", ButtonGesture.SingleTap, ButtonAction.Photo);
        triggered = null;
        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        triggered.Should().Be(ButtonAction.Photo);

        await manager.StopAsync();
        manager.Dispose();
    }

    [Fact]
    public async Task AllThreeGestures_DispatchCorrectDefaultActions()
    {
        var session  = new FakeHeyCyanSession();
        var provider = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        var actionMap = new ActionMap();
        HeyCyanButtonDefaults.SeedDefaults(actionMap);

        var manager = new ButtonInputManager(
            new IButtonInputProvider[] { provider },
            actionMap,
            NullLogger<ButtonInputManager>.Instance);

        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await manager.StartAsync();

        ButtonAction? triggered = null;
        manager.ActionTriggered += (_, e) => triggered = e.Action;

        // SingleTap → ToggleConversation
        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        triggered.Should().Be(ButtonAction.ToggleConversation);

        // DoubleTap → Photo
        triggered = null;
        session.RaiseButtonPressed(HeyCyanButtonGesture.DoubleTap);
        triggered.Should().Be(ButtonAction.Photo);

        // LongPress → EndSession
        triggered = null;
        session.RaiseButtonPressed(HeyCyanButtonGesture.LongPress);
        triggered.Should().Be(ButtonAction.EndSession);

        await manager.StopAsync();
        manager.Dispose();
    }

    [Fact]
    public async Task ButtonPressed_BeforeProviderStarted_DoesNotDispatch()
    {
        var session  = new FakeHeyCyanSession();
        var provider = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        var actionMap = new ActionMap();
        HeyCyanButtonDefaults.SeedDefaults(actionMap);

        var manager = new ButtonInputManager(
            new IButtonInputProvider[] { provider },
            actionMap,
            NullLogger<ButtonInputManager>.Instance);

        session.RaiseConnected("AA:BB:CC:DD:EE:FF");

        var triggered = false;
        manager.ActionTriggered += (_, _) => triggered = true;

        // Button press before StartAsync should not dispatch
        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        triggered.Should().BeFalse();

        await manager.StartAsync();
        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        triggered.Should().BeTrue();

        await manager.StopAsync();
        manager.Dispose();
    }

    [Fact]
    public async Task ButtonPressed_AfterProviderStopped_DoesNotDispatch()
    {
        var session  = new FakeHeyCyanSession();
        var provider = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        var actionMap = new ActionMap();
        HeyCyanButtonDefaults.SeedDefaults(actionMap);

        var manager = new ButtonInputManager(
            new IButtonInputProvider[] { provider },
            actionMap,
            NullLogger<ButtonInputManager>.Instance);

        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await manager.StartAsync();

        var eventCount = 0;
        manager.ActionTriggered += (_, _) => eventCount++;

        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        eventCount.Should().Be(1);

        await manager.StopAsync();

        // Button press after StopAsync should not dispatch
        session.RaiseButtonPressed(HeyCyanButtonGesture.Tap);
        eventCount.Should().Be(1, "should not increment after StopAsync");

        manager.Dispose();
    }
}
