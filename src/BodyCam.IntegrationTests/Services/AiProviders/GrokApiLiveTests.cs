using BodyCam.Services.AiProviders;
using FluentAssertions;

namespace BodyCam.IntegrationTests.Services.AiProviders;

public class GrokApiLiveTests
{
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00,
        0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xFF, 0xFF, 0x3F,
        0x00, 0x05, 0xFE, 0x02, 0xFE, 0xA7, 0x35, 0x81, 0x84, 0x00, 0x00, 0x00,
        0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    [Fact]
    public async Task TextProvider_ReturnsLiveChatCompletion_WhenOptedIn()
    {
        if (!ShouldRunLiveTests())
            return;

        var provider = new GrokTextProvider(new GrokApiClient(GetApiKey()));

        var response = await provider.GetTextAsync(new AiTextRequest(
            GrokModelOptions.DefaultChat,
            [new("user", "Reply with one short sentence saying that the Grok text probe is OK.")],
            MaxOutputTokens: 32));

        response.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task VisionProvider_ReturnsLiveImageDescription_WhenOptedIn()
    {
        if (!ShouldRunLiveTests())
            return;

        var provider = new GrokVisionProvider(new GrokApiClient(GetApiKey()));

        var response = await provider.DescribeImageAsync(new AiVisionRequest(
            GrokModelOptions.DefaultVision,
            "Reply with one short sentence describing this simple test image.",
            OnePixelPng,
            "image/png",
            Detail: "low"));

        response.Text.Should().NotBeNullOrWhiteSpace();
    }

    private static bool ShouldRunLiveTests() =>
        string.Equals(Environment.GetEnvironmentVariable("BODYCAM_GROK_LIVE_TESTS"), "1", StringComparison.Ordinal);

    private static string GetApiKey()
    {
        var key = Environment.GetEnvironmentVariable("XAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("BODYCAM_XAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("BODYCAM_GROK_API_KEY");

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "Set XAI_API_KEY, BODYCAM_XAI_API_KEY, or BODYCAM_GROK_API_KEY when BODYCAM_GROK_LIVE_TESTS=1.");

        return key;
    }
}
