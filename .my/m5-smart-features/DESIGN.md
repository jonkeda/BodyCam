# M5 — Smart Features ✦ Experience

**Status:** NOT STARTED
**Goal:** Build the "Meta-like" experience features.

---

## Scope

| # | Task | Details |
|---|------|---------|
| 5.1 | "Hey BodyCam" wake word | Always-on low-power wake word detection |
| 5.2 | Look & Ask | Auto-capture frame when user asks a question |
| 5.3 | Live translation | Real-time speech translation mode |
| 5.4 | Object/text recognition | Continuous background scene understanding |
| 5.5 | Memory / recall | "Remember this" — save context for later retrieval |
| 5.6 | Notification readout | Read phone notifications through glasses speakers |
| 5.7 | Navigation cues | Simple audio navigation directions |

## Exit Criteria

- [ ] Wake word activates listening without button press
- [ ] "What am I looking at?" auto-captures and describes
- [ ] Live translation of spoken foreign language
- [ ] "Remember this" saves context; "what did I save?" recalls it
- [ ] Phone notifications read aloud

---

## Technical Design

### 5.1 — Wake Word Detection

**Approach:** Local on-device keyword spotting (no cloud round-trip).

**Options:**
| Library | Platform | License | Notes |
|---------|----------|---------|-------|
| Porcupine (Picovoice) | All | Free tier | Custom wake words, very accurate |
| Vosk | All | Apache 2.0 | Full offline STT, can filter for keyword |
| Windows.Media.SpeechRecognition | Windows | Built-in | Constrained grammar mode |
| Android SpeechRecognizer | Android | Built-in | Limited offline support |

**Recommended:** Porcupine for cross-platform wake word, with Vosk as fallback.

**Flow:**
```
Mic (always-on, low power) → Wake word engine
  │
  ├── No match → discard audio
  │
  └── "Hey BodyCam" detected
        → Play acknowledgment tone
        → Start streaming to OpenAI Realtime API
        → Listen for user query
        → Process normally via Orchestrator
        → After response, return to wake-word-only mode
```

### 5.2 — Look & Ask

Detect vision-related intent in user speech and auto-capture.

**Trigger keywords:** "see", "look at", "what is", "read this", "describe", "show me"

```csharp
private static readonly string[] VisionKeywords =
    ["see", "look", "what is", "read", "describe", "show", "watching"];

bool ShouldTriggerVision(string transcript)
{
    var lower = transcript.ToLowerInvariant();
    return VisionKeywords.Any(k => lower.Contains(k));
}
```

**Flow:**
```
User: "What am I looking at?"
  → Keyword match → CaptureFrame()
  → VisionAgent.DescribeFrameAsync()
  → Inject description into conversation context
  → ConversationAgent processes with visual context
  → Response references what's visible
```

### 5.3 — Live Translation

**Mode:** User says "translate mode" or "translate to Spanish"

**Approach A — OpenAI Realtime API:**
- Modify system prompt to include: "Translate all user speech to {target language} and speak the translation"
- Realtime API handles STT → translation → TTS in one loop

**Approach B — Separated pipeline:**
- STT → text → GPT translation → TTS
- More control, supports language detection

**Decision:** Start with Approach A (simpler). Fall back to B if latency is too high.

### 5.4 — Object/Text Recognition

**Background scene understanding at intervals:**
- Capture frame every N seconds (configurable, default 10s)
- Send to Vision API with prompt: "List key objects, text, and notable details in this scene. Be brief."
- Store result in `SessionContext.SceneDescription`
- ConversationAgent can reference it: "Based on what I see around you..."

**OCR / text reading:**
- Trigger: "Read that" / "What does it say?"
- Capture frame → Vision API with prompt: "Read all visible text exactly as written"

### 5.5 — Memory / Recall

**User says "Remember this" → save current context:**

```csharp
public class MemoryStore
{
    private readonly List<MemoryEntry> _entries = [];

    public void Save(string content, string? visionContext = null)
    {
        _entries.Add(new MemoryEntry
        {
            Content = content,
            VisionContext = visionContext,
            Timestamp = DateTime.UtcNow,
            Location = null // future: GPS
        });
    }

    public IReadOnlyList<MemoryEntry> Search(string query)
    {
        // Simple keyword match; future: vector search
        return _entries
            .Where(e => e.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}

public class MemoryEntry
{
    public required string Content { get; set; }
    public string? VisionContext { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Location { get; set; }
}
```

**Persistence:** JSON file or SQLite for cross-session memory.

**Recall triggers:** "What did I save?", "Remember when...", "What was that thing..."

### 5.6 — Notification Readout

**Android:** Use `NotificationListenerService` to intercept notifications.
**Windows:** Use `Windows.UI.Notifications.Management.UserNotificationListener`.

**Flow:**
```
Phone notification arrives
  → Filter (whitelist/blacklist apps)
  → Extract title + body text
  → TTS: "Notification from {app}: {title}. {body}"
  → Play through glasses speakers
```

**Privacy controls:**
- Enable/disable per app
- "Do not disturb" mode
- Only read when session is active

### 5.7 — Navigation Cues

**Simple approach:** Use device GPS + a directions API.

- User says "Navigate to [place]"
- Query directions API (Google Maps, Mapbox)
- Convert turn-by-turn to audio cues
- "In 200 meters, turn right"

**Note:** This is lower priority. Can start with simple "ask the AI for directions" which uses GPT's knowledge.

---

## NuGet Packages Needed

| Package | Purpose |
|---------|---------|
| Porcupine.Net (or similar) | Wake word detection |
| SQLite-net-pcl | Memory persistence |

---

## Risks

| Risk | Mitigation |
|------|-----------|
| Wake word false positives | Tune sensitivity; add confirmation tone |
| Translation latency | Use Realtime API (sub-second) |
| Memory storage growth | Periodic cleanup, storage limits |
| Notification spam | Strict filtering, user control |
