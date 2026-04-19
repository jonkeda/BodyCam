using BodyCam.RealTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable MEAI001
#pragma warning disable OPENAI002
#pragma warning disable SCME0001

namespace BodyCam.RealTests.Pipeline;

/// <summary>
/// Low-level MAF session tests that mirror exactly what AgentOrchestrator does,
/// to diagnose transcript delivery issues.
/// </summary>
[Trait("Category", "RealAPI")]
public class TranscriptDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public TranscriptDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Test 1: Send text with the same session options the orchestrator uses.
    /// Logs every message type and CLR type to see exactly what arrives.
    /// </summary>
    [Fact]
    public async Task SessionWithOrchestratorOptions_SendText_LogsAllMessages()
    {
        var settings = RealtimeFixture.LoadSettings();
        var apiKey = RealtimeFixture.LoadApiKey(settings.Provider);
        var client = RealtimeFixture.BuildClient(apiKey, settings);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var options = new RealtimeSessionOptions
        {
            Instructions = "You are a helpful assistant. Respond very briefly.",
            TranscriptionOptions = new()
            {
                ModelId = "whisper-1",
            },
            VoiceActivityDetection = new()
            {
                Enabled = true,
                AllowInterruption = true,
            },
        };

        _output.WriteLine($"Provider: {settings.Provider}");
        _output.WriteLine($"TranscriptionModel: whisper-1");

        var session = await client.CreateSessionAsync(options, cts.Token);
        _output.WriteLine("Session created");

        // Send text
        await session.SendAsync(
            new CreateConversationItemRealtimeClientMessage(
                new RealtimeConversationItem([new TextContent("Say hello")], role: ChatRole.User)),
            cts.Token);
        await session.SendAsync(new CreateResponseRealtimeClientMessage(), cts.Token);
        _output.WriteLine("Text sent, streaming...\n");

        var textDeltas = new List<string>();
        var transcriptDeltas = new List<string>();
        var audioBytes = 0;
        var completedTexts = new List<string>();
        var msgCount = 0;

        await foreach (var msg in session.GetStreamingResponseAsync(cts.Token))
        {
            msgCount++;
            var type = msg.Type;
            var clr = msg.GetType().Name;

            // Log every single message
            if (type == RealtimeServerMessageType.OutputAudioDelta)
            {
                var a = (OutputTextAudioRealtimeServerMessage)msg;
                var len = a.Audio is not null ? Convert.FromBase64String(a.Audio).Length : 0;
                audioBytes += len;
                _output.WriteLine($"  [{msgCount}] {clr} Type={type} audio={len}b");
            }
            else if (type == RealtimeServerMessageType.OutputTextDelta)
            {
                var t = (OutputTextAudioRealtimeServerMessage)msg;
                textDeltas.Add(t.Text ?? "");
                _output.WriteLine($"  [{msgCount}] {clr} Type={type} text=\"{t.Text}\"");
            }
            else if (type == RealtimeServerMessageType.OutputTextDone)
            {
                var t = (OutputTextAudioRealtimeServerMessage)msg;
                completedTexts.Add(t.Text ?? "");
                _output.WriteLine($"  [{msgCount}] {clr} Type={type} text=\"{t.Text}\"");
            }
            else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
            {
                var t = (OutputTextAudioRealtimeServerMessage)msg;
                completedTexts.Add($"[audio-transcript]{t.Text}");
                _output.WriteLine($"  [{msgCount}] {clr} Type={type} transcript=\"{t.Text}\"");
            }
            else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDelta)
            {
                var t = (OutputTextAudioRealtimeServerMessage)msg;
                transcriptDeltas.Add(t.Text ?? "");
                _output.WriteLine($"  [{msgCount}] {clr} Type={type} delta=\"{t.Text}\"");
            }
            else if (type == RealtimeServerMessageType.InputAudioTranscriptionCompleted)
            {
                var t = (InputAudioTranscriptionRealtimeServerMessage)msg;
                _output.WriteLine($"  [{msgCount}] {clr} Type={type} transcription=\"{t.Transcription}\"");
            }
            else if (type == RealtimeServerMessageType.ResponseDone)
            {
                _output.WriteLine($"  [{msgCount}] {clr} Type={type}");
                break;
            }
            else if (type == RealtimeServerMessageType.Error)
            {
                var e = (ErrorRealtimeServerMessage)msg;
                _output.WriteLine($"  [{msgCount}] ERROR: {e.Error?.Message}");
            }
            else
            {
                _output.WriteLine($"  [{msgCount}] {clr} Type={type}");
            }
        }

        _output.WriteLine($"\n=== Summary ===");
        _output.WriteLine($"Total messages: {msgCount}");
        _output.WriteLine($"Text deltas: {textDeltas.Count} => \"{string.Join("", textDeltas)}\"");
        _output.WriteLine($"Transcript deltas: {transcriptDeltas.Count} => \"{string.Join("", transcriptDeltas)}\"");
        _output.WriteLine($"Audio bytes: {audioBytes}");
        _output.WriteLine($"Completed texts: {string.Join(" | ", completedTexts)}");

        // GA Realtime API only supports audio OR text, not both.
        // With audio output, live transcript comes from OutputAudioTranscriptionDelta.
        transcriptDeltas.Should().NotBeEmpty("OutputAudioTranscriptionDelta should stream the AI's spoken text for live UI");
        audioBytes.Should().BeGreaterThan(0, "should get audio data");

        if (session is IAsyncDisposable d) await d.DisposeAsync();
    }

    /// <summary>
    /// Test 2: Same as above but WITHOUT OutputModalities set.
    /// This confirms the default behavior (audio-only, no text deltas).
    /// </summary>
    [Fact]
    public async Task SessionWithoutOutputModalities_SendText_LogsAllMessages()
    {
        var settings = RealtimeFixture.LoadSettings();
        var apiKey = RealtimeFixture.LoadApiKey(settings.Provider);
        var client = RealtimeFixture.BuildClient(apiKey, settings);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var options = new RealtimeSessionOptions
        {
            Instructions = "You are a helpful assistant. Respond very briefly.",
            // NO OutputModalities — what does the server default to?
        };

        _output.WriteLine($"Provider: {settings.Provider}");
        _output.WriteLine($"OutputModalities: NOT SET (server default)");

        var session = await client.CreateSessionAsync(options, cts.Token);
        _output.WriteLine("Session created");

        await session.SendAsync(
            new CreateConversationItemRealtimeClientMessage(
                new RealtimeConversationItem([new TextContent("Say hello")], role: ChatRole.User)),
            cts.Token);
        await session.SendAsync(new CreateResponseRealtimeClientMessage(), cts.Token);
        _output.WriteLine("Text sent, streaming...\n");

        var textDeltas = new List<string>();
        var audioBytes = 0;
        var msgCount = 0;

        await foreach (var msg in session.GetStreamingResponseAsync(cts.Token))
        {
            msgCount++;
            var type = msg.Type;
            var clr = msg.GetType().Name;

            if (type == RealtimeServerMessageType.OutputAudioDelta)
            {
                var a = (OutputTextAudioRealtimeServerMessage)msg;
                var len = a.Audio is not null ? Convert.FromBase64String(a.Audio).Length : 0;
                audioBytes += len;
                if (audioBytes < 50000) // Only log first few
                    _output.WriteLine($"  [{msgCount}] {clr} Type={type} audio={len}b");
            }
            else if (type == RealtimeServerMessageType.OutputTextDelta)
            {
                var t = (OutputTextAudioRealtimeServerMessage)msg;
                textDeltas.Add(t.Text ?? "");
                _output.WriteLine($"  [{msgCount}] {clr} Type={type} text=\"{t.Text}\"");
            }
            else if (type == RealtimeServerMessageType.ResponseDone)
            {
                _output.WriteLine($"  [{msgCount}] {clr} Type={type}");
                break;
            }
            else
            {
                _output.WriteLine($"  [{msgCount}] {clr} Type={type}");
            }
        }

        _output.WriteLine($"\n=== Summary ===");
        _output.WriteLine($"Total messages: {msgCount}");
        _output.WriteLine($"Text deltas: {textDeltas.Count}");
        _output.WriteLine($"Audio bytes: {audioBytes}");
        _output.WriteLine($"Had text: {textDeltas.Count > 0}, Had audio: {audioBytes > 0}");

        if (session is IAsyncDisposable d) await d.DisposeAsync();
    }

    /// <summary>
    /// Test 3: Full orchestrator round-trip with extra event diagnostics.
    /// Uses OrchestratorFixture but dumps every captured event.
    /// </summary>
    [Fact]
    public async Task Orchestrator_SendText_TranscriptEventsAreFired()
    {
        var settings = RealtimeFixture.LoadSettings();
        var apiKey = RealtimeFixture.LoadApiKey(settings.Provider);
        var client = RealtimeFixture.BuildClient(apiKey, settings);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var options = new RealtimeSessionOptions
        {
            Instructions = "You are a helpful assistant. Respond very briefly.",
            TranscriptionOptions = new()
            {
                ModelId = "whisper-1",
            },
            VoiceActivityDetection = new()
            {
                Enabled = true,
                AllowInterruption = true,
            },
        };

        var session = await client.CreateSessionAsync(options, cts.Token);

        // Dump the raw SDK session options to verify they were applied
        if (session.GetType().GetProperty("Options") is { } optsProp)
        {
            var opts = optsProp.GetValue(session);
            _output.WriteLine($"Session.Options type: {opts?.GetType().FullName}");
        }

        // Check RawRepresentation for the underlying SDK session config
        var rawProp = session.GetType().GetProperty("RawRepresentation");
        if (rawProp is not null)
        {
            var raw = rawProp.GetValue(session);
            _output.WriteLine($"RawRepresentation type: {raw?.GetType().FullName}");
        }

        await session.SendAsync(
            new CreateConversationItemRealtimeClientMessage(
                new RealtimeConversationItem([new TextContent("What is 2 + 2?")], role: ChatRole.User)),
            cts.Token);
        await session.SendAsync(new CreateResponseRealtimeClientMessage(), cts.Token);

        var textDeltas = new List<string>();
        var transcriptDeltas = new List<string>();
        var transcriptDone = "";
        var audioTranscript = "";
        var audioBytes = 0;

        await foreach (var msg in session.GetStreamingResponseAsync(cts.Token))
        {
            var type = msg.Type;

            if (type == RealtimeServerMessageType.OutputAudioTranscriptionDelta)
            {
                var t = (OutputTextAudioRealtimeServerMessage)msg;
                transcriptDeltas.Add(t.Text ?? "");
                _output.WriteLine($"  TranscriptDelta: \"{t.Text}\"");
            }
            else if (type == RealtimeServerMessageType.OutputTextDelta)
            {
                var t = (OutputTextAudioRealtimeServerMessage)msg;
                textDeltas.Add(t.Text ?? "");
                _output.WriteLine($"  TextDelta: \"{t.Text}\"");
            }
            else if (type == RealtimeServerMessageType.OutputTextDone)
            {
                var t = (OutputTextAudioRealtimeServerMessage)msg;
                transcriptDone = t.Text ?? "";
                _output.WriteLine($"  TextDone: \"{t.Text}\"");
            }
            else if (type == RealtimeServerMessageType.OutputAudioDelta)
            {
                var a = (OutputTextAudioRealtimeServerMessage)msg;
                var len = a.Audio is not null ? Convert.FromBase64String(a.Audio).Length : 0;
                audioBytes += len;
            }
            else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
            {
                var t = (OutputTextAudioRealtimeServerMessage)msg;
                audioTranscript = t.Text ?? "";
                _output.WriteLine($"  AudioTranscriptDone: \"{t.Text}\"");
            }
            else if (type == RealtimeServerMessageType.ResponseDone)
            {
                _output.WriteLine($"  ResponseDone");
                break;
            }
        }

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"TranscriptDeltas ({transcriptDeltas.Count}): \"{string.Join("", transcriptDeltas)}\"");
        _output.WriteLine($"TextDeltas ({textDeltas.Count}): \"{string.Join("", textDeltas)}\"");
        _output.WriteLine($"TextDone: \"{transcriptDone}\"");
        _output.WriteLine($"AudioTranscript: \"{audioTranscript}\"");
        _output.WriteLine($"AudioBytes: {audioBytes}");

        // The orchestrator feeds TranscriptDelta from OutputAudioTranscriptionDelta
        // and TranscriptCompleted from OutputAudioTranscriptionDone
        var hasLiveStream = transcriptDeltas.Count > 0;
        var hasCompletion = !string.IsNullOrEmpty(audioTranscript);

        _output.WriteLine($"\nhasLiveStream (for TranscriptDelta): {hasLiveStream}");
        _output.WriteLine($"hasCompletion (for TranscriptCompleted): {hasCompletion}");

        hasLiveStream.Should().BeTrue("OutputAudioTranscriptionDelta should stream AI's spoken text for live transcript");
        hasCompletion.Should().BeTrue("OutputAudioTranscriptionDone should provide final transcript text");

        if (session is IAsyncDisposable d) await d.DisposeAsync();
    }

    /// <summary>
    /// Test 4: Send audio PCM to a raw MAF session and check if InputAudioTranscriptionCompleted fires.
    /// This tests whether the TranscriptionOptions config reaches the server and works correctly.
    /// Note: Azure only supports whisper-1 for input transcription; gpt-4o-mini-transcribe is OpenAI-only.
    /// </summary>
    [Fact]
    public async Task SendAudio_InputAudioTranscriptionCompleted_Fires()
    {
        var settings = RealtimeFixture.LoadSettings();
        var apiKey = RealtimeFixture.LoadApiKey(settings.Provider);
        var client = RealtimeFixture.BuildClient(apiKey, settings);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var options = new RealtimeSessionOptions
        {
            Instructions = "You are a helpful assistant. Respond very briefly.",
            TranscriptionOptions = new() { ModelId = "whisper-1" },
            VoiceActivityDetection = new()
            {
                Enabled = true,
                AllowInterruption = false, // Prevent interruption during test
            },
        };

        _output.WriteLine($"Provider: {settings.Provider}");
        _output.WriteLine($"TranscriptionModel: whisper-1");
        _output.WriteLine($"AllowInterruption: false");

        var session = await client.CreateSessionAsync(options, cts.Token);
        _output.WriteLine("Session created");

        // Load and send audio in chunks (simulating mic input)
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData");
        var pcm = await File.ReadAllBytesAsync(Path.Combine(testDataDir, "hello_can_you_hear_me.pcm"));
        _output.WriteLine($"Audio file: {pcm.Length} bytes ({pcm.Length / 48000.0:F1}s at 24kHz PCM16)");

        const int chunkSize = 4800; // 100ms at 24kHz mono PCM16
        for (int offset = 0; offset < pcm.Length; offset += chunkSize)
        {
            var len = Math.Min(chunkSize, pcm.Length - offset);
            var chunk = new byte[len];
            Array.Copy(pcm, offset, chunk, 0, len);
            await session.SendAsync(
                new InputAudioBufferAppendRealtimeClientMessage(
                    new DataContent(chunk, "audio/pcm")), cts.Token);
        }
        _output.WriteLine($"Sent {pcm.Length / chunkSize} audio chunks");

        // Wait a moment for VAD to commit the audio before processing
        await Task.Delay(500);

        // Now stream and log ALL message types
        string? userTranscription = null;
        var aiTranscriptDeltas = new List<string>();
        string? aiTranscriptDone = null;
        var audioBytes = 0;
        var msgCount = 0;

        try
        {
            await foreach (var msg in session.GetStreamingResponseAsync(cts.Token))
            {
                msgCount++;
                var type = msg.Type;
                var clr = msg.GetType().Name;

                if (type == RealtimeServerMessageType.InputAudioTranscriptionCompleted)
                {
                    var t = (InputAudioTranscriptionRealtimeServerMessage)msg;
                    userTranscription = t.Transcription ?? "";
                    _output.WriteLine($"  [{msgCount}] INPUT TRANSCRIPTION: \"{userTranscription}\"");
                }                else if (type == new RealtimeServerMessageType("InputAudioTranscriptionFailed"))
                {
                    var t = (InputAudioTranscriptionRealtimeServerMessage)msg;
                    _output.WriteLine($"  [{msgCount}] INPUT TRANSCRIPTION FAILED!");
                    // Extract error message from MAF ErrorContent
                    if (t.Error is { } errorContent)
                    {
                        _output.WriteLine($"    ErrorContent.Message = {errorContent.Message}");
                        _output.WriteLine($"    ErrorContent.ErrorCode = {errorContent.ErrorCode}");
                        _output.WriteLine($"    ErrorContent.Details = {errorContent.Details}");
                    }
                }                else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDelta)
                {
                    var t = (OutputTextAudioRealtimeServerMessage)msg;
                    aiTranscriptDeltas.Add(t.Text ?? "");
                    _output.WriteLine($"  [{msgCount}] AI TranscriptDelta: \"{t.Text}\"");
                }
                else if (type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
                {
                    var t = (OutputTextAudioRealtimeServerMessage)msg;
                    aiTranscriptDone = t.Text;
                    _output.WriteLine($"  [{msgCount}] AI TranscriptDone: \"{t.Text}\"");
                }
                else if (type == RealtimeServerMessageType.OutputAudioDelta)
                {
                    var a = (OutputTextAudioRealtimeServerMessage)msg;
                    var len = a.Audio is not null ? Convert.FromBase64String(a.Audio).Length : 0;
                    audioBytes += len;
                }
                else if (type == RealtimeServerMessageType.Error)
                {
                    var e = (ErrorRealtimeServerMessage)msg;
                    _output.WriteLine($"  [{msgCount}] ERROR: {e.Error?.Message}");
                }
                else if (type == RealtimeServerMessageType.ResponseDone)
                {
                    _output.WriteLine($"  [{msgCount}] ResponseDone");
                    break;
                }
                else
                {
                    // Dump raw SDK event Kind for unhandled messages
                    var rawInfo = "";
                    if (msg.RawRepresentation is OpenAI.Realtime.RealtimeServerUpdate sdkUpdate)
                    {
                        try
                        {
                            var kindProp = sdkUpdate.GetType().GetProperty("Kind",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Instance);
                            rawInfo = $"Kind={kindProp?.GetValue(sdkUpdate)} SdkType={sdkUpdate.GetType().Name}";
                        }
                        catch { rawInfo = sdkUpdate.GetType().Name; }
                    }
                    _output.WriteLine($"  [{msgCount}] {clr} Type={type} {rawInfo}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _output.WriteLine($"TIMEOUT after {msgCount} messages");
        }

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"Total messages: {msgCount}");
        _output.WriteLine($"User transcription: \"{userTranscription}\"");
        _output.WriteLine($"AI transcript deltas: {aiTranscriptDeltas.Count} => \"{string.Join("", aiTranscriptDeltas)}\"");
        _output.WriteLine($"AI transcript done: \"{aiTranscriptDone}\"");
        _output.WriteLine($"AI audio bytes: {audioBytes}");

        userTranscription.Should().NotBeNullOrEmpty(
            "TranscriptionOptions should cause the server to transcribe the user's audio input");

        if (session is IAsyncDisposable d) await d.DisposeAsync();
    }
}
