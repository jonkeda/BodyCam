using BodyCam;
using BodyCam.Services.AiProviders;
using FluentAssertions;

namespace BodyCam.Tests.Services.AiProviders;

public class FakeAiProvidersTests
{
    [Fact]
    public async Task FakeProviders_ReturnDeterministicResponses()
    {
        var text = new FakeAiTextProvider("hello");
        var vision = new FakeAiVisionProvider("a test image");
        var stt = new FakeAiSpeechToTextProvider("spoken words");
        var tts = new FakeAiTextToSpeechProvider();
        var images = new FakeAiImageGenerationProvider();
        var realtime = new FakeGrokRealtimeVoiceProvider();

        (await text.GetTextAsync(new AiTextRequest("fake-chat", [new("user", "hi")])))
            .Text.Should().Be("hello");
        (await vision.DescribeImageAsync(new AiVisionRequest("fake-vision", "look", [1, 2, 3])))
            .Text.Should().Be("a test image");
        (await stt.TranscribeAsync(new AiSpeechToTextRequest([1], "clip.wav", "audio/wav")))
            .Text.Should().Be("spoken words");
        (await tts.SynthesizeAsync(new AiTextToSpeechRequest("say it")))
            .AudioBytes.Should().NotBeEmpty();
        (await images.GenerateAsync(new AiImageGenerationRequest("draw it")))
            .Images.Should().ContainSingle(image => image.Url == "https://example.test/fake.png");
        realtime.CreateSessionOptions(new AppSettings()).RequiresEphemeralClientSecret.Should().BeTrue();

        text.LastRequest.Should().NotBeNull();
        vision.LastRequest.Should().NotBeNull();
        stt.LastRequest.Should().NotBeNull();
        tts.LastRequest.Should().NotBeNull();
        images.LastRequest.Should().NotBeNull();
    }
}
