# M2 Implementation — Step 3: ConversationAgent Rewrite

**Depends on:** Step 2 (IChatCompletionsClient available in DI)
**Produces:** Rewritten `ConversationAgent` that calls Chat Completions with streaming, system prompt injection

---

## Why This Step?

The current `ConversationAgent` is a stub — it just appends messages to `SessionContext`. For Mode B, it becomes the brain: receive transcript → build message array from history → call Chat Completions → stream reply tokens back to caller.

---

## Tasks

### 3.1 — Rewrite `ConversationAgent`

**File:** `src/BodyCam/Agents/ConversationAgent.cs` — REWRITE

```csharp
using BodyCam.Models;
using BodyCam.Services;

namespace BodyCam.Agents;

/// <summary>
/// Mode A: Records transcripts (passthrough — Realtime API handles reasoning).
/// Mode B: Calls Chat Completions API for custom reasoning with streaming.
/// </summary>
public class ConversationAgent
{
    private readonly IChatCompletionsClient _chatClient;
    private readonly AppSettings _settings;

    public ConversationAgent(IChatCompletionsClient chatClient, AppSettings settings)
    {
        _chatClient = chatClient;
        _settings = settings;
    }

    // --- Mode A (passthrough) methods — unchanged from M1 ---

    public void AddUserMessage(string transcript, SessionContext session)
    {
        session.Messages.Add(new ChatMessage { Role = "user", Content = transcript });
    }

    public void AddAssistantMessage(string transcript, SessionContext session)
    {
        session.Messages.Add(new ChatMessage { Role = "assistant", Content = transcript });
    }

    // --- Mode B (separated pipeline) methods — NEW ---

    /// <summary>
    /// Process a user transcript through Chat Completions and stream reply tokens.
    /// Adds both the user message and assistant reply to session history.
    /// </summary>
    public async IAsyncEnumerable<string> ProcessTranscriptAsync(
        string transcript,
        SessionContext session,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Add user message to history
        AddUserMessage(transcript, session);

        // Ensure system prompt is set
        if (string.IsNullOrWhiteSpace(session.SystemPrompt))
            session.SystemPrompt = _settings.SystemInstructions;

        // Get trimmed history with system prompt + vision context
        var messages = session.GetTrimmedHistory();

        // Stream reply from Chat Completions
        var replyBuilder = new System.Text.StringBuilder();

        await foreach (var token in _chatClient.CompleteStreamingAsync(messages, ct))
        {
            replyBuilder.Append(token);
            yield return token;
        }

        // Add complete reply to session history
        var fullReply = replyBuilder.ToString();
        if (fullReply.Length > 0)
        {
            AddAssistantMessage(fullReply, session);
        }
    }

    /// <summary>
    /// Non-streaming variant for simple cases.
    /// </summary>
    public async Task<string> ProcessTranscriptFullAsync(
        string transcript,
        SessionContext session,
        CancellationToken ct = default)
    {
        AddUserMessage(transcript, session);

        if (string.IsNullOrWhiteSpace(session.SystemPrompt))
            session.SystemPrompt = _settings.SystemInstructions;

        var messages = session.GetTrimmedHistory();
        var reply = await _chatClient.CompleteAsync(messages, ct);

        if (reply.Length > 0)
            AddAssistantMessage(reply, session);

        return reply;
    }
}
```

### 3.2 — Update DI registration

**File:** `src/BodyCam/MauiProgram.cs` — MODIFY

`ConversationAgent` now takes `IChatCompletionsClient` and `AppSettings` in its constructor. The existing `AddSingleton<ConversationAgent>()` should still work since both dependencies are already registered. Verify DI resolution.

### 3.3 — Define the BodyCam system prompt

**File:** `src/BodyCam/AppSettings.cs` — MODIFY (default value)

Change `SystemInstructions` default from `"You are a helpful assistant."` to:

```csharp
public string SystemInstructions { get; set; } = """
    You are BodyCam, an AI assistant integrated into smart glasses.
    You can see what the user sees (when vision is active) and hear what they say.

    Guidelines:
    - Be concise — the user hears your response through small speakers
    - Prefer short, direct answers (1-3 sentences)
    - If vision context is available, reference what you see
    - You can be asked to remember things for later
    - Be conversational and natural
    """;
```

**Note:** This default applies in both modes. In Mode A, it's sent to the Realtime API via `session.update`. In Mode B, it's the Chat Completions system message.

---

## Verification

- [ ] App builds with rewritten `ConversationAgent`
- [ ] DI resolves `ConversationAgent` with its new dependencies
- [ ] Mode A still works — `AddUserMessage` / `AddAssistantMessage` unchanged
- [ ] `ProcessTranscriptAsync` streams tokens from mock `IChatCompletionsClient`
- [ ] User message and full reply are both recorded in `SessionContext`
- [ ] System prompt is injected when empty

---

## Unit Tests

**File:** `src/BodyCam.Tests/Agents/ConversationAgentTests.cs` — NEW or MODIFY

```csharp
[Fact] AddUserMessage_AppendsToSession()
[Fact] AddAssistantMessage_AppendsToSession()
[Fact] ProcessTranscriptAsync_StreamsTokens()
[Fact] ProcessTranscriptAsync_AddsUserAndAssistantMessages()
[Fact] ProcessTranscriptAsync_SetsSystemPromptIfEmpty()
[Fact] ProcessTranscriptAsync_RespectssCancellation()
[Fact] ProcessTranscriptFullAsync_ReturnsCompleteReply()
```

All tests mock `IChatCompletionsClient` — no real API calls.
