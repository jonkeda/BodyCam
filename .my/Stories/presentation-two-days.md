# BodyCam: From Idea to Smart Glasses in Two Days

*A developer presentation — everything built from scratch with AI-assisted development.*

---

## Slide 1 — The Pitch

**"We went from an empty folder to a 13-tool smart glasses platform in two days."**

No starter template. No existing codebase. No boilerplate. Just an idea, a $35 pair of sunglasses from Alibaba, and an AI coding agent in VS Code.

- .NET MAUI cross-platform app — built from scratch
- Real-time voice conversation via OpenAI Realtime API
- Live camera vision via GPT
- 13 AI-powered tools via function calling
- Wake word detection architecture
- Azure OpenAI multi-provider support
- Settings UI with per-tool configuration
- 229 unit tests + 13 live API tests
- **All green. Zero failures.**

---

## Slide 2 — The Starting Point

```
Day 1, 8:00 AM
────────────────

e:\repos\Private\BodyCam\
└── (empty)
```

That's it. An empty folder and an idea:

> "Build an open-source alternative to RayBan Meta smart glasses
> using $35 Chinese BT glasses and a .NET MAUI companion app
> powered by OpenAI."

The first file written was `first.md` — a natural language spec describing the agent architecture, the service layer, and every interface. Not code. Just a prompt.

---

## Slide 3 — The Roadmap

Before writing any code, we wrote the roadmap. Nine milestones:

```
M0 Scaffold ──► M7 Auth ──► M1 Audio ──► M2 Conversation ──► M3 Vision
                                                                  │
                                                        M4 Glasses (future)
                                                                  │
                                                        M5 Smart Features
                                                                  │
                                                        M6 Polish (future)
```

Plus M8 (Model Selection) and M9 (Azure Provider).

Each milestone got a design doc *before* any code. Five design docs for M5 alone. Thirty-plus across the project.

**Lesson #1: The roadmap took 20 minutes. It saved 20 hours.**

---

## Slide 4 — M0: The Scaffold (Hour 1)

First milestone: a runnable MAUI app with nothing in it.

```
BodyCam.sln
├── src/BodyCam/
│   ├── Agents/          (VoiceInput, Conversation, VoiceOutput, Vision)
│   ├── Services/        (Audio, Camera, RealtimeClient, Settings)
│   ├── Orchestration/   (AgentOrchestrator)
│   ├── Models/          (SessionContext, TranscriptEntry)
│   ├── Mvvm/            (ObservableObject, RelayCommand, ViewModelBase)
│   ├── ViewModels/      (MainViewModel, SettingsViewModel)
│   └── MauiProgram.cs   (DI registration)
├── src/BodyCam.Tests/
├── src/BodyCam.IntegrationTests/
└── src/BodyCam.RealTests/
```

DI wired. Settings loaded. MainPage with a Start button. Three test projects. The app launched and did nothing — but it launched.

---

## Slide 5 — M7+M1: Voice (Hours 2–6)

**The hardest part of the whole project.**

| What | How |
|---|---|
| API key storage | `IApiKeyService` → MAUI SecureStorage |
| Mic capture | `IAudioInputService` → NAudio, PCM 24kHz 16-bit mono |
| Speaker playback | `IAudioOutputService` → NAudio, buffered queue |
| API connection | `RealtimeClient` → WebSocket to OpenAI Realtime API |
| Voice agents | `VoiceInputAgent` (mic → API), `VoiceOutputAgent` (API → speaker) |

The first time it worked: "Hello?" into the laptop mic → transcript on screen → AI speaks back → 500ms round-trip.

Then the bugs:
- **RCA-005**: Garbled voice — audio chunks overlapping. `async void` fire-and-forget without a queue.
- **RCA-004**: Scrambled transcript — streaming deltas arriving out of order on network hiccups.
- **RCA-003**: Transcript ordering — user and AI lines interleaving incorrectly.

Nine steps. Nine design docs. Five RCAs. The voice loop was solid.

---

## Slide 6 — M2: Conversation (Hours 6–8)

Almost anticlimactic after audio.

```csharp
// ConversationAgent — backed by GPT via Chat Completions
public async Task<string> AnalyzeAsync(string query, string? context = null)
{
    var messages = new List<ChatMessage> { /* system prompt + history + query */ };
    var response = await _chatClient.GetResponseAsync(messages);
    return response.Text;
}
```

`SessionContext` for conversation history. System prompt for personality. Orchestrator pipeline: VoiceIn → Conversation → VoiceOut.

Seven steps. First real conversation: asked it about the weather, then about the code, then told it a joke. It laughed — or at least generated audio that sounded like a polite chuckle.

---

## Slide 7 — M3: Vision (Hours 8–12)

This is where it got real.

```csharp
// VisionAgent — sends JPEG frames to GPT Vision
public async Task<string> DescribeFrameAsync(byte[] jpeg, string? prompt)
{
    var messages = new List<ChatMessage>
    {
        new(ChatRole.User, [
            new TextContent(prompt ?? "Describe what you see."),
            new ImageContent(jpeg, "image/jpeg")
        ])
    };
    var response = await _chatClient.GetResponseAsync(messages);
    return response.Text;
}
```

Pointed the webcam at the desk. "What do you see?"

*"A white desk with a 27-inch monitor showing a code editor. A mechanical keyboard, a wireless mouse, and a coffee mug with steam rising from it."*

It saw the steam rising from the coffee.

Then **RCA-006**: black frames. Camera capturing before the sensor stabilized. **RCA-007**: black after first frame — CameraView lifecycle issue. Both fixed.

Eight steps. The app could see, hear, and speak.

---

## Slide 8 — M8/M9: Multi-Provider (Hours 12–14)

Added Azure OpenAI as an alternative provider. Model selection UI. Settings page with Pickers for every model.

```csharp
public enum OpenAiProvider { OpenAi, Azure }
```

One `AppSettings` class. One `SettingsService` backed by MAUI `Preferences`. The `RealtimeClient` and agents branch on provider for endpoint construction.

Connection test button that validates credentials against the live API.

---

## Slide 9 — M5: The Big One (Hours 14–36)

**"Everything is a Tool."**

This was the architectural bet. Don't hardcode features. Make every capability a plugin that the LLM discovers through function calling.

Before M5, the orchestrator had a switch statement:

```csharp
switch (functionName)
{
    case "describe_scene": /* 30 lines inline */ break;
    case "deep_analysis":  /* 20 lines inline */ break;
    default: /* error */ break;
}
```

After M5, the orchestrator had zero tool-specific code:

```csharp
var result = await _dispatcher.ExecuteAsync(functionName, arguments, context, ct);
```

---

## Slide 10 — The Tool Interface

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    string ParameterSchema { get; }       // auto-generated from POCO
    bool IsEnabled { get; }
    WakeWordBinding? WakeWord => null;     // tools declare their own trigger
    Task<ToolResult> ExecuteAsync(string? argumentsJson, ToolContext ctx, CancellationToken ct);
}
```

A complete tool — 40 lines, one file:

```csharp
public class ReadTextTool : ToolBase<ReadTextArgs>
{
    public override string Name => "read_text";
    public override string Description => "Read text visible in the camera view.";
    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-read_en_windows.ppn",
        Mode = WakeWordMode.QuickAction,
        InitialPrompt = "Read any text you can see."
    };

    protected override async Task<ToolResult> ExecuteAsync(
        ReadTextArgs args, ToolContext context, CancellationToken ct)
    {
        var frame = await context.CaptureFrame(ct);
        if (frame is null) return ToolResult.Fail("Camera not available.");

        var text = await _vision.DescribeFrameAsync(frame, "Read all visible text.");
        return ToolResult.Success(new { text });
    }
}
```

Registration: `builder.Services.AddSingleton<ITool, ReadTextTool>();`

**There is no step 2.**

---

## Slide 11 — 10 Steps, 10 Green Builds

M5 was executed as 10 sequential steps, each with a subagent:

| Step | What | Tests Added | Running Total |
|---|---|---|---|
| 1 | Tool abstractions (ITool, ToolBase, SchemaGenerator) | +17 | 147 |
| 2 | Migrate existing tools out of orchestrator | +9 | 155 |
| 3 | ToolDispatcher + DI wiring | +7 | 162 |
| 4 | UI redesign (tri-state pill, tabs, action bar) | +4 | 166 |
| 5 | Wake word service (IWakeWordService) | +7 | 173 |
| 6 | Wake word → tool binding | +6 | 179 |
| 7 | Phase A: 8 new tools + MemoryStore | +25 | 204 |
| 8 | Phase B: 3 new tools + SkiaSharp | +11 | 215 |
| 9 | Per-tool settings UI | +6 | 221 |
| 10 | Platform services (mic handoff) | +8 | 229 |

Every step: implement → build → test → all green → next step.

---

## Slide 12 — The 13 Tools

| Tool | What It Does | Wake Word |
|---|---|---|
| `describe_scene` | Camera → GPT Vision → spoken description | "Look" |
| `deep_analysis` | Complex reasoning via Chat Completions | — |
| `read_text` | OCR via vision model | "Read" |
| `take_photo` | Capture + save JPEG | — |
| `save_memory` | Persist notes to JSON store | "Remember" |
| `recall_memory` | Search saved memories | — |
| `set_translation_mode` | Live speech translation | "Translate" |
| `make_phone_call` | Platform phone dialer | "Call" |
| `send_message` | SMS / WhatsApp | — |
| `lookup_address` | Address lookup passthrough | — |
| `find_object` | Continuous camera scan + SkiaSharp annotation | "Find" |
| `navigate_to` | Opens maps with directions | "Navigate" |
| `start_scene_watch` | Background condition monitoring | — |

---

## Slide 13 — The LLM as Router

We don't decide which tool to call. The model does.

```
User says: "Read the sign in front of me"
  → Model sees 13 tool definitions with JSON schemas
  → Model decides: read_text({ focus: "sign" })
  → ToolDispatcher.ExecuteAsync("read_text", ...)
  → ReadTextTool captures frame, sends to vision
  → Result: "EXIT ONLY — Do Not Enter"
  → Model speaks: "The sign says Exit Only"
```

The orchestrator has exactly **3 wake word cases** — and they never change:
1. `StartSession` — "Hey BodyCam"
2. `GoToSleep` — "Go to sleep"
3. `InvokeTool` — any tool wake word → delegate to dispatcher

Add 10 more tools tomorrow? Zero orchestrator changes.

---

## Slide 14 — The Surprise

When we asked the model to "navigate to the nearest Starbucks," it didn't call `navigate_to`.

It called `lookup_address` first.

It wanted to find the actual address *before* starting navigation. Smarter than the test expected.

**When the LLM is your router, it makes decisions you didn't hardcode.**

We updated the test to accept both tools.

---

## Slide 15 — Real API Tests

Unit tests prove your mocks work. We wrote 13 tests that hit the live OpenAI Realtime API.

```csharp
[Fact]
public async Task SaveMemory_RoundTrip_ModelConfirmsSaved()
{
    await _fixture.SendTextInputAsync(
        "Remember that my car is parked in spot B7.");
    await _fixture.WaitForFunctionCallAsync(TimeSpan.FromSeconds(30));

    var call = _fixture.FunctionCalls.First(fc => fc.Name == "save_memory");
    _fixture.Reset();

    await _fixture.SendFunctionCallOutputAsync(call.CallId,
        JsonSerializer.Serialize(new { saved = true, content = "Car in B7" }));
    await _fixture.WaitForResponseAsync(TimeSpan.FromSeconds(30));

    _fixture.OutputTranscripts.Should().NotBeEmpty();
    // Model says "Got it, I'll remember that."
}
```

3 registration tests + 6 function call tests + 4 round-trip tests = **13 real tests. All green.**

---

## Slide 16 — How AI-Assisted Development Worked

### The pattern:

```
Human: writes roadmap, design doc, step spec
  ↓
AI agent: implements code, tests, DI wiring
  ↓
AI agent: runs build (must succeed)
  ↓
AI agent: runs tests (ALL must pass)
  ↓
Human: reviews, moves to next step
```

### What the AI handled:
- ~80 new files with consistent code style
- Constructor changes propagated to all test helpers
- DI registration ordering
- 229 tests across 3 test projects
- Platform-conditional compilation
- XAML data binding with proper `x:DataType`

### What I decided:
- The roadmap and milestone order
- Architecture: everything-is-a-tool, wake-word-as-property
- Which tools to build
- When the model made unexpected choices (accept both tools)
- When to stop

---

## Slide 17 — By the Numbers

| Metric | Count |
|---|---|
| Calendar time | ~2 days |
| Starting point | Empty folder |
| Milestones completed | 8 (M0, M7, M1, M2, M3, M8, M9, M5) |
| Design documents | 30+ |
| Step plans | 40+ steps |
| Files created | ~80 |
| Files modified | ~40 |
| Tools built | 13 |
| RCAs written (bugs found & fixed) | 7 |
| Unit tests | 229 |
| Real API tests | 13 |
| Total tests passing | 242 |
| Test failures | 0 |
| NuGet packages | ~10 |
| Lines changed in orchestrator to add 11 tools | 0 |
| Hardware cost | $35 |
| RayBan Meta cost | $299 |

---

## Slide 18 — What It Does

```
You:        "Hey BodyCam"
BodyCam:    [wakes up — gray pill → green pill]

You:        "What do you see?"
BodyCam:    "You're at a crosswalk. The sign says Broadway."

You:        "Read that menu in the window."
BodyCam:    "Espresso $3.50, Latte $4.75, Cold Brew $5.00..."

You:        "Remember — cold brew here is five dollars."
BodyCam:    "Got it, I'll remember that."

You:        "Find my red mug."
BodyCam:    [scans camera every 3 seconds]
            "Found it — left side of the desk, next to the monitor."

You:        "Navigate to the subway."
BodyCam:    [maps app opens with walking directions]

You:        "Go to sleep."
BodyCam:    [green pill → gray pill, mic released]
```

$35 sunglasses. Open source. Extensible. Yours.

---

## Slide 19 — Lessons Learned

**1. Design first, code second.** 20 minutes of roadmap saved 20 hours of rework. Every milestone had a design doc before a single line of code.

**2. AI agents are force multipliers, not replacements.** I made every architectural decision. The agent implemented them — fast, consistently, across 80 files without typos.

**3. The plugin pattern is the unlock.** Once `ITool` and `ToolDispatcher` existed, each new tool was 15 minutes: one file, one DI line, one test file. The agent could parallelize this trivially.

**4. Real API tests catch what mocks hide.** The `lookup_address` surprise only appeared against the live model. Your mocks will always agree with your assumptions.

**5. Subagents with strict specs beat long conversations.** Each step got a fresh agent with a detailed spec: files to create, code to write, tests to run. No context drift, no hallucinated state.

**6. You can build a LOT in two days.** Not a prototype. Not a demo. A tested, multi-platform app with 13 tools, 242 tests, and real API integration. The bottleneck isn't typing — it's deciding what to build.

---

## Slide 20 — Q&A

Two days. One developer. One AI agent.

From an empty folder to a smart glasses platform.

242 tests. 13 tools. 8 milestones. Zero failures.

The glasses cost $35.

*Questions?*
