using BodyCam.ViewModels;
using FluentAssertions;

namespace BodyCam.Tests.ViewModels;

public class ExtractToolResultTextTests
{
    [Fact]
    public void Description_Field_ReturnsValue()
    {
        var json = """{"description":"A sunny park with trees."}""";
        MainViewModel.ExtractToolResultText(json).Should().Be("A sunny park with trees.");
    }

    [Fact]
    public void Text_Field_ReturnsValue()
    {
        var json = """{"text":"EXIT\nNo smoking"}""";
        MainViewModel.ExtractToolResultText(json).Should().Be("EXIT\nNo smoking");
    }

    [Fact]
    public void Analysis_Field_ReturnsValue()
    {
        var json = """{"analysis":"The object appears to be a ceramic vase."}""";
        MainViewModel.ExtractToolResultText(json).Should().Be("The object appears to be a ceramic vase.");
    }

    [Fact]
    public void Error_Field_ReturnsError()
    {
        var json = """{"error":"Camera not available."}""";
        MainViewModel.ExtractToolResultText(json).Should().Be("Camera not available.");
    }

    [Fact]
    public void Error_TakesPrecedence_OverDescription()
    {
        var json = """{"error":"Timeout","description":"partial"}""";
        MainViewModel.ExtractToolResultText(json).Should().Be("Timeout");
    }

    [Fact]
    public void Found_Result_ReturnsDescription()
    {
        var json = """{"found":true,"description":"FOUND: keys on the table","target":"my keys"}""";
        MainViewModel.ExtractToolResultText(json).Should().Be("FOUND: keys on the table");
    }

    [Fact]
    public void NotFound_Result_ReturnsDescription()
    {
        var json = """{"found":false,"description":"Could not find 'keys' within 30s.","target":"keys"}""";
        MainViewModel.ExtractToolResultText(json).Should().Be("Could not find 'keys' within 30s.");
    }

    [Fact]
    public void Unknown_Schema_ReturnsRawJson()
    {
        var json = """{"foo":"bar"}""";
        MainViewModel.ExtractToolResultText(json).Should().Be(json);
    }

    [Fact]
    public void Invalid_Json_ReturnsRawString()
    {
        var raw = "not json at all";
        MainViewModel.ExtractToolResultText(raw).Should().Be(raw);
    }

    [Fact]
    public void Saved_Photo_ReturnsDescription()
    {
        var json = """{"saved":true,"fileName":"photo_20260419.jpg","description":"Photo captured"}""";
        MainViewModel.ExtractToolResultText(json).Should().Be("Photo captured");
    }
}
