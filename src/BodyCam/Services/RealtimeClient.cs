using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BodyCam.Models;
using BodyCam.Services.Realtime;

namespace BodyCam.Services;

public class RealtimeClient : IRealtimeClient
{
    private readonly IApiKeyService _apiKeyService;
    private readonly AppSettings _settings;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;

    public RealtimeClient(IApiKeyService apiKeyService, AppSettings settings)
    {
        _apiKeyService = apiKeyService;
        _settings = settings;
    }

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync()
            ?? throw new InvalidOperationException("API key not configured. Set OPENAI_API_KEY or AZURE_OPENAI_API_KEY.");

        _ws = new ClientWebSocket();

        if (_settings.Provider == OpenAiProvider.Azure)
            _ws.Options.SetRequestHeader("api-key", apiKey);
        else
            _ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        _ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");

        await _ws.ConnectAsync(_settings.GetRealtimeUri(), ct);

        _receiveCts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));

        IsConnected = true;
        await UpdateSessionAsync(ct);
    }

    public async Task DisconnectAsync()
    {
        if (!IsConnected) return;

        IsConnected = false;

        _receiveCts?.Cancel();

        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token);
            }
            catch { /* Best-effort close */ }
        }

        if (_receiveLoop is not null)
        {
            try { await _receiveLoop; } catch { }
        }

        _ws?.Dispose();
        _ws = null;
        _receiveCts?.Dispose();
        _receiveCts = null;
        _receiveLoop = null;
    }

    public async Task SendAudioChunkAsync(byte[] pcm16Data, CancellationToken ct = default)
    {
        var msg = new AudioBufferAppendMessage
        {
            Type = "input_audio_buffer.append",
            Audio = Convert.ToBase64String(pcm16Data)
        };
        await SendJsonAsync(msg, RealtimeJsonContext.Default.AudioBufferAppendMessage, ct);
    }

    public Task CommitAudioBufferAsync(CancellationToken ct = default)
        => SendSimpleMessageAsync("input_audio_buffer.commit", ct);

    public Task CreateResponseAsync(CancellationToken ct = default)
        => SendSimpleMessageAsync("response.create", ct);

    public Task CancelResponseAsync(CancellationToken ct = default)
        => SendSimpleMessageAsync("response.cancel", ct);

    public async Task TruncateResponseAudioAsync(string itemId, int audioEndMs, CancellationToken ct = default)
    {
        var msg = new TruncateMessage
        {
            Type = "conversation.item.truncate",
            ItemId = itemId,
            ContentIndex = 0,
            AudioEndMs = audioEndMs
        };
        await SendJsonAsync(msg, RealtimeJsonContext.Default.TruncateMessage, ct);
    }

    public async Task UpdateSessionAsync(CancellationToken ct = default)
    {
        var modalities = _settings.Mode == ConversationMode.Separated
            ? new[] { "text" }           // STT only — no audio output from Realtime
            : new[] { "text", "audio" }; // Full audio in+out (Mode A)

        var msg = new SessionUpdateMessage
        {
            Type = "session.update",
            Session = new SessionUpdatePayload
            {
                Modalities = modalities,
                Voice = _settings.Voice,
                Instructions = _settings.SystemInstructions,
                InputAudioFormat = "pcm16",
                OutputAudioFormat = "pcm16",
                InputAudioTranscription = new InputAudioTranscription { Model = _settings.TranscriptionModel },
                TurnDetection = new TurnDetectionConfig { Type = _settings.TurnDetection }
            }
        };
        await SendJsonAsync(msg, RealtimeJsonContext.Default.SessionUpdateMessage, ct);
    }

    public async Task SendTextForTtsAsync(string text, CancellationToken ct = default)
    {
        // Create a conversation item with the assistant's reply text
        var itemMsg = new ConversationItemCreateMessage
        {
            Type = "conversation.item.create",
            Item = new ConversationItem
            {
                Type = "message",
                Role = "assistant",
                Content = [new ContentPart { Type = "input_text", Text = text }]
            }
        };
        await SendJsonAsync(itemMsg, RealtimeJsonContext.Default.ConversationItemCreateMessage, ct);

        // Request a response — the Realtime API will generate audio from the text
        await CreateResponseAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    // Events
    public event EventHandler<byte[]>? AudioDelta;
    public event EventHandler<string>? OutputTranscriptDelta;
    public event EventHandler<string>? OutputTranscriptCompleted;
    public event EventHandler<string>? InputTranscriptCompleted;
    public event EventHandler? SpeechStarted;
    public event EventHandler? SpeechStopped;
    public event EventHandler<RealtimeResponseInfo>? ResponseDone;
    public event EventHandler<string>? ErrorOccurred;

    // --- Private implementation ---

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var messageBuffer = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException) { break; }

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                messageBuffer.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    var json = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                    messageBuffer.SetLength(0);
                    DispatchMessage(json);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Receive loop error: {ex.Message}");
        }
        finally
        {
            IsConnected = false;
        }
    }

    internal void DispatchMessage(string json)
    {
        var type = ServerEventParser.GetType(json);
        if (type is null) return;

        switch (type)
        {
            case "response.audio.delta":
            {
                var delta = ServerEventParser.GetStringProperty(json, "delta");
                if (delta is not null)
                {
                    try
                    {
                        var audioBytes = Convert.FromBase64String(delta);
                        AudioDelta?.Invoke(this, audioBytes);
                    }
                    catch { }
                }
                break;
            }

            case "response.audio_transcript.delta":
            {
                var delta = ServerEventParser.GetStringProperty(json, "delta");
                if (delta is not null)
                    OutputTranscriptDelta?.Invoke(this, delta);
                break;
            }

            case "response.audio_transcript.done":
            {
                var transcript = ServerEventParser.GetStringProperty(json, "transcript");
                if (transcript is not null)
                    OutputTranscriptCompleted?.Invoke(this, transcript);
                break;
            }

            case "conversation.item.input_audio_transcription.completed":
            {
                var transcript = ServerEventParser.GetStringProperty(json, "transcript");
                if (transcript is not null)
                    InputTranscriptCompleted?.Invoke(this, transcript);
                break;
            }

            case "input_audio_buffer.speech_started":
                SpeechStarted?.Invoke(this, EventArgs.Empty);
                break;

            case "input_audio_buffer.speech_stopped":
                SpeechStopped?.Invoke(this, EventArgs.Empty);
                break;

            case "response.done":
            {
                var (responseId, itemId, outputTranscript, inputTranscript) =
                    ServerEventParser.ParseResponseDone(json);
                if (responseId is not null)
                {
                    ResponseDone?.Invoke(this, new RealtimeResponseInfo
                    {
                        ResponseId = responseId,
                        ItemId = itemId,
                        OutputTranscript = outputTranscript,
                        InputTranscript = inputTranscript
                    });
                }
                break;
            }

            case "error":
            {
                var message = ServerEventParser.GetNestedStringProperty(json, "error", "message")
                           ?? "Unknown error";
                ErrorOccurred?.Invoke(this, message);
                break;
            }

            // session.created, session.updated, response.created, etc. — informational, no action needed
        }
    }

    private async Task SendJsonAsync<T>(T message, JsonTypeInfo<T> typeInfo, CancellationToken ct) where T : class
    {
        if (_ws?.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(message, typeInfo);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private Task SendSimpleMessageAsync(string type, CancellationToken ct)
    {
        var msg = new RealtimeMessage { Type = type };
        return SendJsonAsync(msg, RealtimeJsonContext.Default.RealtimeMessage, ct);
    }
}
