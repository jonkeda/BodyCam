using BodyCam;
using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Services.Camera.Commands;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class CommandDetailViewModelTests
{
    [Fact]
    public void Load_LookCommand_ShowsLookSettingsAndPrompts()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.DefaultTouchCommandMode.Returns(CameraCommandMode.ManualAim);
        settings.DefaultLookDetailLevel.Returns(LookDetailLevel.Overview);
        settings.DefaultReadDetailLevel.Returns(ReadDetailLevel.Full);
        settings.ConfirmExternalScanActions.Returns(true);

        var chatClient = Substitute.For<IChatClient>();
        var registry = new CameraCommandRegistry(
            [new LookCommand(new VisionAgent(chatClient, new AppSettings()))]);
        var vm = new CommandDetailViewModel(registry, settings);

        vm.Load("look");

        vm.Title.Should().Be("Look");
        vm.CommandId.Should().Be("look");
        vm.HasLookSettings.Should().BeTrue();
        vm.HasPromptDefinitions.Should().BeTrue();
        vm.PromptDefinitions.Should().Contain(p =>
            p.DisplayName == "Look"
            && p.Text == "Look. Give an overview."
            && p.Prompt.Contains("orientation-first overview"));
    }
}
