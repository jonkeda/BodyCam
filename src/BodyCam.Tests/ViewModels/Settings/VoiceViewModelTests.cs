using BodyCam.Services;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class VoiceViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    [Fact]
    public void SelectedVoice_Set_PersistsToSettings()
    {
        var vm = new VoiceViewModel(_settings);
        vm.SelectedVoice = "echo";
        _settings.Voice.Should().Be("echo");
    }

    [Fact]
    public void SelectedTurnDetection_Set_PersistsToSettings()
    {
        var vm = new VoiceViewModel(_settings);
        vm.SelectedTurnDetection = "server_vad";
        _settings.TurnDetection.Should().Be("server_vad");
    }

    [Fact]
    public void SystemInstructions_Set_PersistsToSettings()
    {
        var vm = new VoiceViewModel(_settings);
        vm.SystemInstructions = "You are a helpful assistant.";
        _settings.SystemInstructions.Should().Be("You are a helpful assistant.");
    }

    [Fact]
    public void VoiceOptions_ReturnsNonEmpty()
    {
        var vm = new VoiceViewModel(_settings);
        vm.VoiceOptions.Should().NotBeEmpty();
    }

    [Fact]
    public void SelectedVoice_RaisesPropertyChanged()
    {
        var vm = new VoiceViewModel(_settings);
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VoiceViewModel.SelectedVoice))
                raised = true;
        };
        vm.SelectedVoice = "nova";
        raised.Should().BeTrue();
    }

    [Fact]
    public void Title_IsVoiceAndAI()
    {
        var vm = new VoiceViewModel(_settings);
        vm.Title.Should().Be("Voice & AI");
    }
}
