using BodyCam.Tools;
using FluentAssertions;

namespace BodyCam.Tests.Tools;

public class ToolResultTests
{
    [Fact]
    public void Success_SetsIsSuccessTrue()
    {
        var result = ToolResult.Success(new { ok = true });

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Success_SerializesData()
    {
        var result = ToolResult.Success(new { name = "test" });

        result.Json.Should().Contain("\"name\":\"test\"");
    }

    [Fact]
    public void Fail_SetsIsSuccessFalse()
    {
        var result = ToolResult.Fail("something broke");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Fail_IncludesErrorInJson()
    {
        var result = ToolResult.Fail("something broke");

        result.Json.Should().Contain("something broke");
        result.Error.Should().Be("something broke");
    }
}
