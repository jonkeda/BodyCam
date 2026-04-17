# Step 4: Repurpose ConversationAgent for Deep Analysis

Remove Mode A passthrough methods and Mode B streaming. Replace with a single `AnalyzeAsync` method for the `deep_analysis` function tool. Change dependency from `IChatCompletionsClient` to `IChatClient` directly.

## Depends On: Step 1 (Mode B removal)

## Files Modified

### 1. `src/BodyCam/Agents/ConversationAgent.cs`

**Rewrite** the entire class:

```csharp
using Microsoft.Extensions.AI;

namespace BodyCam.Agents;

/// <summary>
/// Executes deep analysis queries using a Chat Completions model (e.g. gpt-5.4).
/// Called by AgentOrchestrator when the Realtime API triggers the deep_analysis function.
/// </summary>
public class ConversationAgent
{
    private readonly IChatClient _chatClient;
    private readonly AppSettings _settings;

    public ConversationAgent(IChatClient chatClient, AppSettings settings)
    {
        _chatClient = chatClient;
        _settings = settings;
    }

    /// <summary>
    /// Performs deep analysis on a query using Chat Completions.
    /// Returns the full text result to be sent back as function_call_output.
    /// </summary>
    public async Task<string> AnalyzeAsync(
        string query,
        string? context = null,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>();

        // System prompt for analysis mode
        messages.Add(new ChatMessage(ChatRole.System,
            """
            You are a deep analysis assistant. Provide thorough, detailed answers.
            Be comprehensive but structured. Use markdown formatting where helpful.
            The user is interacting via voice — your response will be spoken aloud,
            so avoid overly long or complex formatting.
            """));

        // Include conversation context if provided
        if (!string.IsNullOrWhiteSpace(context))
        {
            messages.Add(new ChatMessage(ChatRole.System,
                $"Conversation context: {context}"));
        }

        messages.Add(new ChatMessage(ChatRole.User, query));

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? string.Empty;
    }
}
```

**Key changes:**
- Constructor takes `IChatClient` instead of `IChatCompletionsClient`
- Removes `AddUserMessage`, `AddAssistantMessage` (Realtime API manages its own conversation state)
- Removes `ProcessTranscriptAsync`, `ProcessTranscriptFullAsync` (Mode B streaming)
- Removes `SessionContext` dependency (not needed — Realtime API tracks history)
- Adds `AnalyzeAsync(query, context)` for function tool execution
- Uses `Microsoft.Extensions.AI.ChatMessage` directly (no mapping needed)

### 2. `src/BodyCam/MauiProgram.cs`

**Remove** `IChatCompletionsClient` registration:

```csharp
// DELETE:
builder.Services.AddSingleton<IChatCompletionsClient, ChatCompletionsClient>();
```

**Update** comment on IChatClient registration:

```csharp
// BEFORE:
// Chat Completions client (Mode B)
builder.Services.AddSingleton<IChatClient>(sp =>

// AFTER:
// Chat Completions client (deep_analysis tool)
builder.Services.AddSingleton<IChatClient>(sp =>
```

**Update** ConversationAgent registration — it now takes `IChatClient` directly, so no change needed to the DI line itself (it already resolves `IChatClient` from the container).

### 3. `src/BodyCam/Models/SessionContext.cs`

**No change needed yet.** SessionContext is no longer used by ConversationAgent, but it's still referenced by `AgentOrchestrator.Session`. We keep the `Session` property on the orchestrator for potential future use (e.g., tracking conversation state for analytics). The orchestrator's `AddUserMessage`/`AddAssistantMessage` calls will be removed since Mode A passthrough is gone.

### 4. `src/BodyCam/Orchestration/AgentOrchestrator.cs`

**Remove** `_conversation.AddUserMessage(transcript, Session)` from `OnInputTranscriptCompleted` (Mode A passthrough no longer needed — the Realtime API maintains its own conversation state):

```csharp
// BEFORE:
private async void OnInputTranscriptCompleted(object? sender, string transcript)
{
    TranscriptUpdated?.Invoke(this, $"You: {transcript}");
    TranscriptCompleted?.Invoke(this, $"You:{transcript}");
    DebugLog?.Invoke(this, $"User said: {transcript}");
    _conversation.AddUserMessage(transcript, Session);
}

// AFTER:
private void OnInputTranscriptCompleted(object? sender, string transcript)
{
    TranscriptUpdated?.Invoke(this, $"You: {transcript}");
    TranscriptCompleted?.Invoke(this, $"You:{transcript}");
    DebugLog?.Invoke(this, $"User said: {transcript}");
}
```

**Remove** `_conversation.AddAssistantMessage(transcript, Session)` from `OnOutputTranscriptCompleted`:

```csharp
// BEFORE:
private void OnOutputTranscriptCompleted(object? sender, string transcript)
{
    _conversation.AddAssistantMessage(transcript, Session);
    TranscriptCompleted?.Invoke(this, $"AI:{transcript}");
    DebugLog?.Invoke(this, $"AI said: {transcript}");
}

// AFTER:
private void OnOutputTranscriptCompleted(object? sender, string transcript)
{
    TranscriptCompleted?.Invoke(this, $"AI:{transcript}");
    DebugLog?.Invoke(this, $"AI said: {transcript}");
}
```

## Files Deleted

### `src/BodyCam/Services/IChatCompletionsClient.cs` — DELETE
### `src/BodyCam/Services/ChatCompletionsClient.cs` — DELETE

These are replaced by direct `IChatClient` usage in `ConversationAgent`.

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore -v q
```
