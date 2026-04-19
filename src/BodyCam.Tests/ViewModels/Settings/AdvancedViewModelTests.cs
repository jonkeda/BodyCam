using BodyCam.Services;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class AdvancedViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    [Fact]
    public void DebugMode_Toggle_PersistsToSettings()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.DebugMode = true;
        _settings.DebugMode.Should().BeTrue();
    }

    [Fact]
    public void ShowTokenCounts_Toggle_PersistsToSettings()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.ShowTokenCounts = true;
        _settings.ShowTokenCounts.Should().BeTrue();
    }

    [Fact]
    public void ShowCostEstimate_Toggle_PersistsToSettings()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.ShowCostEstimate = true;
        _settings.ShowCostEstimate.Should().BeTrue();
    }

    [Fact]
    public void SendDiagnosticData_Toggle_PersistsToSettings()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.SendDiagnosticData = true;
        _settings.SendDiagnosticData.Should().BeTrue();
    }

    [Fact]
    public void ToolSettingsSections_NoTools_Empty()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.ToolSettingsSections.Should().BeEmpty();
    }

    [Fact]
    public void Title_IsAdvanced()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.Title.Should().Be("Advanced");
    }
}
