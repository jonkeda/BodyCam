using BodyCam.Services.Input;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class ButtonDeviceMappingsViewModelTests
{
    private static IButtonMappingStore CreateStore()
    {
        var store = Substitute.For<IButtonMappingStore>();
        store.Get(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ButtonGesture>())
             .Returns(ButtonAction.None);
        return store;
    }

    private static IButtonInputProvider CreateProvider(
        string providerId, string displayName, params ButtonDescriptor[] buttons)
    {
        var provider = Substitute.For<IButtonInputProvider>();
        provider.ProviderId.Returns(providerId);
        provider.DisplayName.Returns(displayName);
        provider.Buttons.Returns(buttons.ToList().AsReadOnly());
        return provider;
    }

    [Fact]
    public void SingleButton_CreatesRowsForEachGesture()
    {
        var provider = CreateProvider("heycyan-glasses", "HeyCyan Glasses Button",
            new ButtonDescriptor("glasses-button", "Glasses Button",
                [ButtonGesture.SingleTap, ButtonGesture.DoubleTap, ButtonGesture.LongPress]));

        var vm = new ButtonDeviceMappingsViewModel(provider, CreateStore());

        vm.DeviceName.Should().Be("HeyCyan Glasses Button");
        vm.ProviderId.Should().Be("heycyan-glasses");
        vm.Rows.Should().HaveCount(3);
    }

    [Fact]
    public void SingleButton_LabelsShowGestureOnly()
    {
        var provider = CreateProvider("test", "Test",
            new ButtonDescriptor("btn", "Button",
                [ButtonGesture.SingleTap, ButtonGesture.DoubleTap]));

        var vm = new ButtonDeviceMappingsViewModel(provider, CreateStore());

        vm.Rows[0].Label.Should().Be("Single Tap");
        vm.Rows[1].Label.Should().Be("Double Tap");
    }

    [Fact]
    public void MultipleButtons_LabelsIncludeButtonName()
    {
        var provider = CreateProvider("keyboard", "Keyboard Shortcuts",
            new ButtonDescriptor("look", "Look (F5)",
                [ButtonGesture.SingleTap, ButtonGesture.LongPress]),
            new ButtonDescriptor("photo", "Photo (F6)",
                [ButtonGesture.SingleTap]));

        var vm = new ButtonDeviceMappingsViewModel(provider, CreateStore());

        vm.Rows.Should().HaveCount(3);
        vm.Rows[0].Label.Should().Be("Look (F5) — Single Tap");
        vm.Rows[1].Label.Should().Be("Look (F5) — Long Press");
        vm.Rows[2].Label.Should().Be("Photo (F6) — Single Tap");
    }

    [Fact]
    public void NoButtons_CreatesEmptyRows()
    {
        var provider = CreateProvider("empty", "Empty Device");

        var vm = new ButtonDeviceMappingsViewModel(provider, CreateStore());

        vm.Rows.Should().BeEmpty();
    }

    [Fact]
    public void AvailableActions_ContainsAllEnumValues()
    {
        var provider = CreateProvider("test", "Test",
            new ButtonDescriptor("btn", "Button", [ButtonGesture.SingleTap]));

        var vm = new ButtonDeviceMappingsViewModel(provider, CreateStore());

        vm.AvailableActions.Should().Contain(ButtonAction.None);
        vm.AvailableActions.Should().Contain(ButtonAction.VolumeUp);
        vm.AvailableActions.Should().Contain(ButtonAction.VolumeDown);
        vm.AvailableActions.Should().Contain(ButtonAction.Mute);
        vm.AvailableActions.Should().Contain(ButtonAction.NextTrack);
        vm.AvailableActions.Should().Contain(ButtonAction.PreviousTrack);
    }

    [Fact]
    public void TripleTap_GestureLabel()
    {
        var provider = CreateProvider("test", "Test",
            new ButtonDescriptor("btn", "Button",
                [ButtonGesture.TripleTap]));

        var vm = new ButtonDeviceMappingsViewModel(provider, CreateStore());

        vm.Rows[0].Label.Should().Be("Triple Tap");
    }

    [Fact]
    public void LongPressRelease_GestureLabel()
    {
        var provider = CreateProvider("test", "Test",
            new ButtonDescriptor("btn", "Button",
                [ButtonGesture.LongPressRelease]));

        var vm = new ButtonDeviceMappingsViewModel(provider, CreateStore());

        vm.Rows[0].Label.Should().Be("Long Press Release");
    }

    [Fact]
    public void SettingAction_PersistsToStore()
    {
        var store = CreateStore();
        var provider = CreateProvider("test", "Test",
            new ButtonDescriptor("btn", "Button",
                [ButtonGesture.SingleTap]));

        var vm = new ButtonDeviceMappingsViewModel(provider, store);
        vm.Rows[0].Action = ButtonAction.Photo;

        store.Received(1).Set("test", "btn", ButtonGesture.SingleTap, ButtonAction.Photo);
    }
}
