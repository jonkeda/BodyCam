using BodyCam.Services.Input;
using BodyCam.Tests.TestInfrastructure;
using FluentAssertions;

namespace BodyCam.Tests.Integration;

public class ButtonDispatchTests : IAsyncLifetime
{
    private readonly BodyCamTestHost _host = BodyCamTestHost.Create();
    private readonly List<ButtonActionEvent> _actions = new();

    public async Task InitializeAsync()
    {
        await _host.InitializeAsync();
        _host.ButtonInput.ActionTriggered += (_, a) => _actions.Add(a);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public void SingleTap_TriggersLook()
    {
        _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);

        _actions.Should().ContainSingle();
        _actions[0].Action.Should().Be(ButtonAction.Look);
    }

    [Fact]
    public void DoubleTap_TriggersPhoto()
    {
        _host.Buttons.SimulateGesture(ButtonGesture.DoubleTap);

        _actions.Should().ContainSingle();
        _actions[0].Action.Should().Be(ButtonAction.Photo);
    }

    [Fact]
    public void LongPress_TriggersToggleSession()
    {
        _host.Buttons.SimulateGesture(ButtonGesture.LongPress);

        _actions.Should().ContainSingle();
        _actions[0].Action.Should().Be(ButtonAction.ToggleSession);
    }

    [Fact]
    public void CustomMapping_OverridesDefault()
    {
        _host.ButtonInput.ActionMap.SetAction(
            "test-buttons:main", ButtonGesture.SingleTap, ButtonAction.Read);

        _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);

        _actions.Should().ContainSingle();
        _actions[0].Action.Should().Be(ButtonAction.Read);
    }

    [Fact]
    public void MultipleGestures_AllDispatched()
    {
        _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);
        _host.Buttons.SimulateGesture(ButtonGesture.DoubleTap);
        _host.Buttons.SimulateGesture(ButtonGesture.LongPress);

        _actions.Should().HaveCount(3);
        _actions.Select(a => a.Action).Should().ContainInOrder(
            ButtonAction.Look, ButtonAction.Photo, ButtonAction.ToggleSession);
    }

    [Fact]
    public void ActionEvent_HasCorrectMetadata()
    {
        _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);

        var action = _actions.Should().ContainSingle().Subject;
        action.SourceProviderId.Should().Be("test-buttons");
        action.SourceGesture.Should().Be(ButtonGesture.SingleTap);
        action.TimestampMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NoneAction_NotDispatched()
    {
        _host.ButtonInput.ActionMap.SetAction(
            "test-buttons:main", ButtonGesture.SingleTap, ButtonAction.None);

        _host.Buttons.SimulateGesture(ButtonGesture.SingleTap);

        _actions.Should().BeEmpty();
    }
}
