using System.Text.Json;
using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Realtime;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services;

public class RealtimeMessageTests
{
    [Fact]
    public void SessionUpdateMessage_SerializesCorrectly()
    {
        var msg = new SessionUpdateMessage
        {
            Type = "session.update",
            Session = new SessionUpdatePayload
            {
                Modalities = ["text", "audio"],
                Voice = "marin",
                InputAudioFormat = "pcm16",
                OutputAudioFormat = "pcm16"
            }
        };

        var json = JsonSerializer.Serialize(msg, RealtimeJsonContext.Default.SessionUpdateMessage);

        json.Should().Contain("\"type\":\"session.update\"");
        json.Should().Contain("\"voice\":\"marin\"");
        json.Should().Contain("\"modalities\":[\"text\",\"audio\"]");
        json.Should().Contain("\"input_audio_format\":\"pcm16\"");
    }

    [Fact]
    public void AudioBufferAppendMessage_SerializesWithBase64()
    {
        var pcm = new byte[] { 1, 2, 3, 4 };
        var msg = new AudioBufferAppendMessage
        {
            Type = "input_audio_buffer.append",
            Audio = Convert.ToBase64String(pcm)
        };

        var json = JsonSerializer.Serialize(msg, RealtimeJsonContext.Default.AudioBufferAppendMessage);

        json.Should().Contain("\"type\":\"input_audio_buffer.append\"");
        json.Should().Contain("\"audio\":\"AQIDBA==\"");
    }

    [Fact]
    public void TruncateMessage_SerializesCorrectly()
    {
        var msg = new TruncateMessage
        {
            Type = "conversation.item.truncate",
            ItemId = "item_abc",
            ContentIndex = 0,
            AudioEndMs = 1500
        };

        var json = JsonSerializer.Serialize(msg, RealtimeJsonContext.Default.TruncateMessage);

        json.Should().Contain("\"item_id\":\"item_abc\"");
        json.Should().Contain("\"audio_end_ms\":1500");
    }

    [Fact]
    public void ServerEventParser_GetType_ExtractsType()
    {
        var json = """{"type":"response.audio.delta","delta":"AQID"}""";
        ServerEventParser.GetType(json).Should().Be("response.audio.delta");
    }

    [Fact]
    public void ServerEventParser_GetType_InvalidJson_ReturnsNull()
    {
        ServerEventParser.GetType("not json").Should().BeNull();
    }

    [Fact]
    public void DispatchMessage_AudioDelta_DecodesBase64AndFiresEvent()
    {
        var apiKey = Substitute.For<IApiKeyService>();
        var settings = new AppSettings();
        var client = new RealtimeClient(apiKey, settings);

        byte[]? received = null;
        client.AudioDelta += (_, data) => received = data;

        var json = """{"type":"response.audio.delta","delta":"AQIDBA=="}""";
        client.DispatchMessage(json);

        received.Should().NotBeNull();
        received.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void DispatchMessage_OutputTranscriptDelta_FiresEvent()
    {
        var apiKey = Substitute.For<IApiKeyService>();
        var settings = new AppSettings();
        var client = new RealtimeClient(apiKey, settings);

        string? received = null;
        client.OutputTranscriptDelta += (_, text) => received = text;

        var json = """{"type":"response.audio_transcript.delta","delta":"Hello"}""";
        client.DispatchMessage(json);

        received.Should().Be("Hello");
    }

    [Fact]
    public void DispatchMessage_OutputTranscriptCompleted_FiresEvent()
    {
        var apiKey = Substitute.For<IApiKeyService>();
        var settings = new AppSettings();
        var client = new RealtimeClient(apiKey, settings);

        string? received = null;
        client.OutputTranscriptCompleted += (_, text) => received = text;

        var json = """{"type":"response.audio_transcript.done","transcript":"Hello world"}""";
        client.DispatchMessage(json);

        received.Should().Be("Hello world");
    }

    [Fact]
    public void DispatchMessage_InputTranscriptCompleted_FiresEvent()
    {
        var apiKey = Substitute.For<IApiKeyService>();
        var settings = new AppSettings();
        var client = new RealtimeClient(apiKey, settings);

        string? received = null;
        client.InputTranscriptCompleted += (_, text) => received = text;

        var json = """{"type":"conversation.item.input_audio_transcription.completed","transcript":"Test input"}""";
        client.DispatchMessage(json);

        received.Should().Be("Test input");
    }

    [Fact]
    public void DispatchMessage_SpeechStarted_FiresEvent()
    {
        var apiKey = Substitute.For<IApiKeyService>();
        var settings = new AppSettings();
        var client = new RealtimeClient(apiKey, settings);

        bool fired = false;
        client.SpeechStarted += (_, _) => fired = true;

        client.DispatchMessage("""{"type":"input_audio_buffer.speech_started"}""");

        fired.Should().BeTrue();
    }

    [Fact]
    public void DispatchMessage_SpeechStopped_FiresEvent()
    {
        var apiKey = Substitute.For<IApiKeyService>();
        var settings = new AppSettings();
        var client = new RealtimeClient(apiKey, settings);

        bool fired = false;
        client.SpeechStopped += (_, _) => fired = true;

        client.DispatchMessage("""{"type":"input_audio_buffer.speech_stopped"}""");

        fired.Should().BeTrue();
    }

    [Fact]
    public void DispatchMessage_ResponseDone_FiresWithInfo()
    {
        var apiKey = Substitute.For<IApiKeyService>();
        var settings = new AppSettings();
        var client = new RealtimeClient(apiKey, settings);

        RealtimeResponseInfo? received = null;
        client.ResponseDone += (_, info) => received = info;

        var json = """{"type":"response.done","response":{"id":"resp_001","output":[{"id":"item_001"}]}}""";
        client.DispatchMessage(json);

        received.Should().NotBeNull();
        received!.ResponseId.Should().Be("resp_001");
        received.ItemId.Should().Be("item_001");
    }

    [Fact]
    public void DispatchMessage_Error_FiresWithMessage()
    {
        var apiKey = Substitute.For<IApiKeyService>();
        var settings = new AppSettings();
        var client = new RealtimeClient(apiKey, settings);

        string? received = null;
        client.ErrorOccurred += (_, msg) => received = msg;

        var json = """{"type":"error","error":{"message":"Invalid API key"}}""";
        client.DispatchMessage(json);

        received.Should().Be("Invalid API key");
    }

    [Fact]
    public void DispatchMessage_UnknownType_DoesNotThrow()
    {
        var apiKey = Substitute.For<IApiKeyService>();
        var settings = new AppSettings();
        var client = new RealtimeClient(apiKey, settings);

        var act = () => client.DispatchMessage("""{"type":"session.created","session":{}}""");

        act.Should().NotThrow();
    }
}
