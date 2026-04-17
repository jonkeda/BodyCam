using BodyCam.Tools;
using BodyCam.ViewModels;
using FluentAssertions;

namespace BodyCam.Tests.ViewModels;

public class ToolSettingsSectionTests
{
    [Fact]
    public void ToolSettingItem_Integer_UpdatesViaStringValue()
    {
        var descriptor = new ToolSettingDescriptor
        {
            Key = "Test.Value",
            Label = "Test",
            Type = ToolSettingType.Integer,
            DefaultValue = 10,
            GetValue = () => 10,
            SetValue = _ => { }
        };

        var item = new ToolSettingItem(descriptor);
        item.LoadFromDescriptor();
        item.StringValue.Should().Be("10");
    }

    [Fact]
    public void ToolSettingItem_Boolean_UpdatesViaBoolValue()
    {
        var called = false;
        var descriptor = new ToolSettingDescriptor
        {
            Key = "Test.Flag",
            Label = "Flag",
            Type = ToolSettingType.Boolean,
            DefaultValue = true,
            GetValue = () => true,
            SetValue = v => called = true
        };

        var item = new ToolSettingItem(descriptor);
        item.LoadFromDescriptor();
        item.BoolValue.Should().BeTrue();

        item.BoolValue = false;
        called.Should().BeTrue();
    }
}
