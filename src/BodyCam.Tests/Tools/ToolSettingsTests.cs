using BodyCam.Agents;
using BodyCam.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class ToolSettingsTests
{
    [Fact]
    public void FindObjectTool_ImplementsIToolSettings()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new FindObjectTool(vision);

        var settings = tool as IToolSettings;
        settings.Should().NotBeNull();
        settings!.SettingsDisplayName.Should().Be("Find Object");
    }

    [Fact]
    public void GetSettingDescriptors_ReturnsTwoDescriptors()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new FindObjectTool(vision);
        var settings = (IToolSettings)tool;

        var descriptors = settings.GetSettingDescriptors();
        descriptors.Should().HaveCount(2);
        descriptors.Should().Contain(d => d.Key == "FindObject.ScanInterval");
        descriptors.Should().Contain(d => d.Key == "FindObject.Timeout");
    }

    [Fact]
    public void SetValue_UpdatesToolProperty()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new FindObjectTool(vision);
        var settings = (IToolSettings)tool;

        var descriptors = settings.GetSettingDescriptors();
        var scanInterval = descriptors.First(d => d.Key == "FindObject.ScanInterval");
        scanInterval.SetValue!(5);

        tool.ScanIntervalSeconds.Should().Be(5);
    }

    [Fact]
    public void GetValue_ReturnsCurrentToolProperty()
    {
        var chatClient = Substitute.For<IChatClient>();
        var vision = new VisionAgent(chatClient, new AppSettings());
        var tool = new FindObjectTool(vision);
        tool.TimeoutSeconds = 45;

        var settings = (IToolSettings)tool;
        var descriptors = settings.GetSettingDescriptors();
        var timeout = descriptors.First(d => d.Key == "FindObject.Timeout");

        timeout.GetValue!().Should().Be(45);
    }
}
