# How We Built BodyCam

*A short story about a developer and an AI who built smart glasses from scratch.*

---

It started with a $35 pair of sunglasses from Alibaba.

Jon had been staring at the RayBan Meta listing for weeks — $299 for a locked ecosystem you couldn't hack, couldn't extend, couldn't truly own. Then one night, scrolling through Alibaba at 2 AM, he found the TKYUAN BT5.3 glasses with a built-in camera. Bluetooth, open-ear speakers, front-facing 1080p camera. Fifty bucks shipped. He ordered two pairs because he knew from experience: first hardware always breaks.

He opened VS Code the next morning and typed the first prompt.

---

## The Roadmap

The plan was ambitious — nine milestones to take a pair of cheap Chinese glasses and make them smarter than anything Meta sold.

```
M0 (Scaffold) → M7 (Auth) → M1 (Audio) → M2 (Conversation) → M3 (Vision)
                                                                    ↓
                                                          M4 (Glasses Hardware)
                                                                    ↓
                                                          M5 (Smart Features)
                                                                    ↓
                                                          M6 (Polish)
```

The insight was simple: the glasses are dumb sensors — a camera, a mic, two speakers. The phone is the brain. A .NET MAUI app would orchestrate everything: capture audio, stream it to OpenAI's Realtime API, pipe vision through GPT, and push speech back through the glasses speakers. The total hardware cost was under $75. The RayBan Meta couldn't be customized at all.

---

## M0 — The Scaffold

Every project starts with a blank solution and a folder structure. Jon's first file was `first.md` — a detailed prompt describing every agent, every service, every interface. VoiceInputAgent, ConversationAgent, VoiceOutputAgent, VisionAgent. An orchestrator to bind them. Platform services for mic, speaker, and camera.

The scaffold went up in a single session. DI wired. Settings loaded. A MainPage with a Start button that did nothing yet. But the app launched, and that mattered.

---

## M1 — Finding the Voice

Audio was the hardest milestone. Not the code — the physics.

PCM audio at 24kHz, 16-bit, mono. Captured in 100ms chunks from the default microphone. Streamed over WebSocket to OpenAI's Realtime API in base64. The first time it worked, Jon spoke "Hello?" into his laptop mic and watched the transcript appear on screen three seconds later. Then the AI spoke back, and he nearly fell out of his chair. The latency was under 500ms.

But then came the bugs.

**RCA-005**: The AI's voice sounded like two people talking at once. Audio chunks were overlapping — a new segment started playing before the previous one finished. The `async void` handler was fire-and-forgetting audio writes without a queue. Fix: a proper concurrent playback buffer.

**RCA-004**: The transcript was garbled. Delta events from the streaming API arrived out of order when the network stuttered. Characters duplicated and rearranged into nonsense. Fix: sequence tracking and reassembly.

Nine steps, nine design docs, and the voice loop was solid. Speak, hear, respond.

---

## M2 — Conversation

With audio working, the conversation agent was almost anticlimactic. A `ConversationAgent` backed by GPT, a `SessionContext` to hold history, and a system prompt that gave the AI its personality. The orchestrator wired it in: VoiceIn → Conversation → VoiceOut. Seven steps.

The first real conversation happened on a Wednesday. Jon asked the AI about the weather, then about the code he was writing, then told it a joke. It laughed. Well — it generated audio that sounded like a polite chuckle. Close enough.

---

## M3 — Seeing

Vision was where it got interesting.

The camera service captured JPEG frames from the laptop webcam. The VisionAgent sent them to GPT-4 Vision with a prompt: "Describe what you see." The response fed back into the conversation context.

Jon pointed the webcam at his desk and said, "What do you see?"

*"A white desk with a 27-inch monitor showing a code editor. A mechanical keyboard, a wireless mouse, and a coffee mug with steam rising from it. Behind the monitor there is a bookshelf with technical books."*

It saw everything. The coffee mug. The steam. The books.

Then came **RCA-006**: black frames. The camera preview worked but the captured JPEG was entirely black. The CameraView was initializing asynchronously and the first frame capture fired before the sensor had stabilized. Fix: wait for a ready signal before capturing. Then another variant — blurry frames from autofocus lag. Fix: a stabilization delay.

By the end of M3, the app could see, hear, and speak. Eight steps, seven RCAs, and a working AI assistant that ran on a laptop.

---

## M5 — Everything is a Tool

Milestone 5 was the big one: make the assistant actually *smart*.

The design started with five documents and a single architectural insight: **everything is a tool**. Don't hardcode features into the orchestrator. Don't write switch statements. Make every capability — reading text, finding objects, saving memories, making phone calls — a self-contained plugin that the LLM discovers through function calling.

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    string ParameterSchema { get; }
    WakeWordBinding? WakeWord => null;
    Task<ToolResult> ExecuteAsync(string? argumentsJson, ToolContext context, CancellationToken ct);
}
```

One interface. Every tool implements it. The `ToolDispatcher` collects them from DI and routes function calls by name. Adding a new tool is one file and one DI line. The LLM is the router — its training decides when to call which tool.

Ten steps, executed one by one:

1. **Tool abstractions** — `ITool`, `ToolBase<T>`, `ToolContext`, `ToolResult`, `SchemaGenerator`. Seventeen tests.
2. **Migration** — Ripped `describe_scene` and `deep_analysis` out of the orchestrator's switch statement and into proper tool classes.
3. **ToolDispatcher** — The switch statement was gone. The dispatcher looked up tools by name from a dictionary.
4. **UI redesign** — A tri-state pill in the status bar: Sleep (gray), Listening (amber), Active (green). Transcript and camera tabs. Five quick-action buttons: Look, Read, Find, Ask, Photo.
5. **Wake word service** — Three listening layers. Sleep → Wake Word → Active Session. Porcupine for always-on keyword detection at 10 milliwatts. The Realtime API costs $5.76/hour to keep open — wake words cost nearly nothing.
6. **Wake word binding** — Wake words became a property of tools, not a central enum. Each tool declares its own trigger word. The dispatcher builds the keyword list from registered tools at startup. Three orchestrator cases that never change: StartSession, GoToSleep, InvokeTool.
7. **Phase A tools** — Eight new tools in one session. ReadText (OCR via vision). TakePhoto. SaveMemory and RecallMemory (JSON-backed persistent store). SetTranslationMode. MakePhoneCall. SendMessage. LookupAddress. Each one: a file, a test file, a DI line.
8. **Phase B tools** — FindObject (continuous camera scanning with SkiaSharp annotation), NavigateTo (platform maps), StartSceneWatch (background polling). SkiaSharp drew red borders around found objects.
9. **Per-tool settings** — `IToolSettings` interface. Tools that need configuration expose descriptors. The settings page renders them dynamically. FindObject got configurable scan interval and timeout.
10. **Platform services** — MicrophoneCoordinator for sequential handoff between Porcupine and the Realtime API. PlatformHelper for cross-platform keyword paths. NotificationInfo model for Android notification readout.

By step 10, the test count was 229. All passing. Thirteen tools registered. The orchestrator was clean — no tool-specific code, just a dispatcher call and three wake word cases.

---

## The Real Tests

Unit tests are a lie you tell yourself. They prove your mocks work.

So we wrote real tests. Thirteen of them. They connected to the live OpenAI Realtime API, registered all thirteen tools, sent natural language prompts, and verified the model called the right function.

"Read the text on the sign in front of me." → `read_text` ✓

"Remember that my car is parked in spot B7." → `save_memory` with B7 in the arguments ✓

"Find my red coffee mug." → `find_object` ✓

Then the round-trips: prompt the model, intercept the function call, send a mock result back, verify the model spoke the answer. ReadText got "EXIT ONLY — Do Not Enter" and the model said "The sign says Exit Only." SaveMemory got `{saved: true}` and the model said "Got it, I'll remember that."

One surprise: when asked to navigate to "the nearest Starbucks," the model chose `lookup_address` instead of `navigate_to`. It wanted to find the address first, then navigate. Smarter than the test expected. We updated the test to accept both.

All thirteen passed. Zero failures.

---

## The Numbers

| What | Count |
|---|---|
| Milestones completed | 6 (M0, M1, M2, M3, M5, M8/M9) |
| Unit tests | 229 |
| Real API tests | 13 |
| Tools | 13 |
| RCAs written | 7 |
| Design documents | 30+ |
| Step plans | 40+ steps |
| Lines of C# | thousands |
| Hardware cost | ~$50 |
| RayBan Meta cost | $299 |

---

## What It Does Now

You put on a pair of $35 sunglasses. You say "Hey BodyCam." The phone in your pocket wakes up.

"What do you see?"

*"You're standing at a crosswalk. The sign across the street says 'Broadway.' There's a coffee shop to your left with an open sign in the window."*

"Read that menu in the window."

*"I can see: Espresso $3.50, Latte $4.75, Cold Brew $5.00, Matcha Latte $5.50..."*

"Remember — the cold brew here is five dollars."

*"Got it. I'll remember that."*

"Navigate to the nearest subway station."

The maps app opens with walking directions.

You walk. The glasses play the AI's voice through open-ear speakers. Nobody around you knows. It looks like you're wearing sunglasses.

A $35 pair of sunglasses from Alibaba. Open source. Extensible. Yours.

---

*Built with .NET MAUI, OpenAI, SkiaSharp, Porcupine, xUnit, FluentAssertions, and an unreasonable number of late nights.*
