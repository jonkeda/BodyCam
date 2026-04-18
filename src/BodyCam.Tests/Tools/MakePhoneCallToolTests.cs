using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Tools;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Tools;

public class MakePhoneCallToolTests
{
    private static ToolContext CreateContext() => new()
    {
        CaptureFrame = ct => Task.FromResult<byte[]?>(null),
        Session = new SessionContext(),
        Log = _ => { },
        RealtimeClient = Substitute.For<IRealtimeClient>()
    };

    [Fact]
    public async Task ExecuteAsync_EmptyContact_ReturnsFail()
    {
        var tool = new MakePhoneCallTool();
        var argsJson = JsonHelper.ParseElement("""{ "contact":""}""");

        var result = await tool.ExecuteAsync(argsJson, CreateContext(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Json.Should().Contain("Contact or phone number is required");
    }

    [Fact]
    public void Name_IsMakePhoneCall()
    {
        var tool = new MakePhoneCallTool();
        tool.Name.Should().Be("make_phone_call");
    }
}
