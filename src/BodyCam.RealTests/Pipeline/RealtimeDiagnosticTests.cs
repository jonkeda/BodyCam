using BodyCam.RealTests.Fixtures;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable MEAI001

namespace BodyCam.RealTests.Pipeline;

[Trait("Category", "RealAPI")]
public class RealtimeDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public RealtimeDiagnosticTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task DirectSession_SendText_ReceivesResponse()
    {
        var settings = RealtimeFixture.LoadSettings();
        var apiKey = RealtimeFixture.LoadApiKey(settings.Provider);
        var client = RealtimeFixture.BuildClient(apiKey, settings);

        _output.WriteLine($"Provider: {settings.Provider}");
        _output.WriteLine($"Creating session...");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        IRealtimeClientSession session;
        try
        {
            var options = new RealtimeSessionOptions
            {
                Instructions = "You are a helpful assistant. Respond briefly.",
            };
            session = await client.CreateSessionAsync(options, cts.Token);
            _output.WriteLine("Session created");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Session creation FAILED: {ex}");
            throw;
        }

        // Send text
        await session.SendAsync(
            new CreateConversationItemRealtimeClientMessage(
                new RealtimeConversationItem([new TextContent("Say the word hello")], role: ChatRole.User)),
            cts.Token);
        await session.SendAsync(new CreateResponseRealtimeClientMessage(), cts.Token);
        _output.WriteLine("Text sent, waiting for updates...");

        var updateCount = 0;
        try
        {
            await foreach (var msg in session.GetStreamingResponseAsync(cts.Token))
            {
                updateCount++;
                var type = msg.Type;

                if (type == RealtimeServerMessageType.OutputAudioDelta)
                {
                    var audioMsg = (OutputTextAudioRealtimeServerMessage)msg;
                    var bytes = audioMsg.Audio is not null ? Convert.FromBase64String(audioMsg.Audio).Length : 0;
                    _output.WriteLine($"  [{updateCount}] AudioDelta: {bytes} bytes");
                }
                else if (type == RealtimeServerMessageType.OutputTextDelta)
                {
                    var textMsg = (OutputTextAudioRealtimeServerMessage)msg;
                    _output.WriteLine($"  [{updateCount}] TextDelta: {textMsg.Text}");
                }
                else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
                {
                    var transcriptMsg = (OutputTextAudioRealtimeServerMessage)msg;
                    _output.WriteLine($"  [{updateCount}] TranscriptDone: {transcriptMsg.Text}");
                }
                else if (type == RealtimeServerMessageType.OutputTextDone)
                {
                    var textMsg = (OutputTextAudioRealtimeServerMessage)msg;
                    _output.WriteLine($"  [{updateCount}] TextDone: {textMsg.Text}");
                }
                else if (type == RealtimeServerMessageType.ResponseDone)
                {
                    _output.WriteLine($"  [{updateCount}] ResponseDone");
                    break;
                }
                else if (type == RealtimeServerMessageType.Error)
                {
                    var errorMsg = (ErrorRealtimeServerMessage)msg;
                    _output.WriteLine($"  [{updateCount}] ERROR: {errorMsg.Error?.Message}");
                }
                else
                {
                    _output.WriteLine($"  [{updateCount}] {msg.GetType().Name} Type={type}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _output.WriteLine($"TIMEOUT after {updateCount} updates");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"STREAM ERROR after {updateCount} updates: {ex}");
        }

        _output.WriteLine($"Total updates: {updateCount}");
        Assert.True(updateCount > 0, "Expected at least one update from the realtime API");

        if (session is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (session is IDisposable disposable)
            disposable.Dispose();
    }
}
