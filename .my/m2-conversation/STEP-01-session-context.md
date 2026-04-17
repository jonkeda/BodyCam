# M2 Implementation — Step 1: SessionContext Enhancement + Mode Flag

**Depends on:** M1 complete
**Produces:** Enhanced `SessionContext`, `ConversationMode` enum, `AppSettings.Mode`, settings UI toggle

---

## Why First?

Every subsequent step branches on the conversation mode. The `ConversationMode` flag determines whether the orchestrator runs Mode A (Realtime passthrough) or Mode B (separated pipeline). `SessionContext` needs sliding-window history before `ConversationAgent` can build Chat API messages from it.

---

## Tasks

### 1.1 — Add `ConversationMode` enum to `AppSettings.cs`

**File:** `src/BodyCam/AppSettings.cs` — MODIFY

Add the enum and a property:

```csharp
public enum ConversationMode
{
    /// <summary>Realtime API handles reasoning + TTS (M1 behavior).</summary>
    Realtime,

    /// <summary>Separated pipeline: Realtime STT → ConversationAgent → TTS.</summary>
    Separated
}
```

Add to `AppSettings`:

```csharp
public ConversationMode Mode { get; set; } = ConversationMode.Realtime;
```

**Why `Realtime` default:** M1 behavior is unchanged. Users opt into Mode B explicitly.

### 1.2 — Enhance `SessionContext` with sliding window

**File:** `src/BodyCam/Models/SessionContext.cs` — MODIFY

Replace the current minimal class with:

```csharp
namespace BodyCam.Models;

/// <summary>
/// Holds conversation state shared across agents.
/// Provides a sliding window over message history to stay within token budget.
/// </summary>
public class SessionContext
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public List<ChatMessage> Messages { get; } = [];
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; }

    /// <summary>Approximate max character budget for history (rough token proxy: 1 token ≈ 4 chars).</summary>
    public int MaxHistoryChars { get; set; } = 16_000; // ~4000 tokens

    /// <summary>Vision context from the most recent camera frame description.</summary>
    public string? LastVisionDescription { get; set; }

    /// <summary>The system prompt injected as the first message for Chat Completions.</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Returns messages trimmed to fit within <see cref="MaxHistoryChars"/>.
    /// Always keeps the system prompt (if any) and the most recent messages.
    /// </summary>
    public List<ChatMessage> GetTrimmedHistory()
    {
        var result = new List<ChatMessage>();

        // Always include system prompt as first message
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            result.Add(new ChatMessage { Role = "system", Content = SystemPrompt });
        }

        // Inject vision context as a system message if available
        if (!string.IsNullOrWhiteSpace(LastVisionDescription))
        {
            result.Add(new ChatMessage
            {
                Role = "system",
                Content = $"[Vision context] You can currently see: {LastVisionDescription}"
            });
        }

        // Walk backwards, accumulating until budget exhausted
        var budget = MaxHistoryChars;
        var kept = new List<ChatMessage>();

        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var msg = Messages[i];
            var cost = msg.Content.Length;
            if (budget - cost < 0 && kept.Count > 0)
                break;
            budget -= cost;
            kept.Add(msg);
        }

        kept.Reverse();
        result.AddRange(kept);
        return result;
    }

    /// <summary>Clears all messages and resets state for a new session.</summary>
    public void Reset()
    {
        Messages.Clear();
        LastVisionDescription = null;
        SessionId = Guid.NewGuid().ToString("N");
        StartedAt = DateTime.UtcNow;
        IsActive = false;
    }
}

public class ChatMessage
{
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
```

### 1.3 — Add `Mode` to `ISettingsService` and `SettingsService`

**File:** `src/BodyCam/Services/ISettingsService.cs` — MODIFY
**File:** `src/BodyCam/Services/SettingsService.cs` — MODIFY

Add a `ConversationMode Mode` property persisted via `Preferences`, defaulting to `ConversationMode.Realtime`.

### 1.4 — Add Mode toggle to Settings UI

**File:** `src/BodyCam/SettingsPage.xaml` — MODIFY
**File:** `src/BodyCam/ViewModels/SettingsViewModel.cs` — MODIFY

Add a picker or toggle switch: "Conversation Mode" → Realtime / Separated.

### 1.5 — Add `ModelOptions` entries for Mode

**File:** `src/BodyCam/ModelOptions.cs` — MODIFY

Add:
```csharp
public static readonly string[] ConversationModes = ["Realtime", "Separated"];
```

---

## Verification

- [ ] App builds and runs
- [ ] `AppSettings.Mode` defaults to `ConversationMode.Realtime`
- [ ] Settings page shows mode picker
- [ ] Changing mode persists across app restarts
- [ ] `SessionContext.GetTrimmedHistory()` returns correct messages within budget
- [ ] Mode A (Realtime) still works exactly as before — no behavior change

---

## Unit Tests

**File:** `src/BodyCam.Tests/Models/SessionContextTests.cs` — NEW or MODIFY

```csharp
[Fact] GetTrimmedHistory_EmptyMessages_ReturnsOnlySystemPrompt()
[Fact] GetTrimmedHistory_WithinBudget_ReturnsAllMessages()
[Fact] GetTrimmedHistory_ExceedsBudget_TrimsOldestMessages()
[Fact] GetTrimmedHistory_WithVisionContext_InjectsVisionMessage()
[Fact] Reset_ClearsEverything()
```
