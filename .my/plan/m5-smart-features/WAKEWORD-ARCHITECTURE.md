# M5 — Wake Word Architecture

## Problem

The wake word analysis doc (5.1) has the same problem the tool architecture was
designed to fix: a big switch statement mapping wake words to actions.

```csharp
// 5.1-WAKE-WORD-ANALYSIS.md — orchestrator handler
switch (e.Type)
{
    case WakeWordType.Look:
        await ExecuteQuickActionAsync("describe_scene", null);
        break;
    case WakeWordType.Read:
        await ExecuteQuickActionAsync("read_text", null);
        break;
    case WakeWordType.Find:
        await StartAsync();
        break;
    // ... every new wake word = another case
}
```

And a parallel enum that must stay in sync with an array of `.ppn` file paths:

```csharp
public enum WakeWordType { FullSession, Look, Read, Find, Remember, ... }

private static readonly string[] KeywordPaths = [
    "hey_bodycam.ppn",    // index 0 = FullSession
    "bodycam_look.ppn",   // index 1 = Look
    // ... must match enum order exactly
];
```

This duplicates the routing problem. Adding a wake word means editing:
1. The enum
2. The `.ppn` path array
3. The switch statement in the orchestrator

Three places — the same antipattern the `ITool` plugin system eliminates.

---

## Solution: Wake Words Are a Property of Tools

A wake word is just **another way to invoke a tool**. The tool already knows its name,
description, and how to execute. It should also declare:
- Whether it has a wake word
- What the `.ppn` keyword file is
- Whether it's a quick action (connect → execute → disconnect) or needs a full session

### Extended ITool Interface

```csharp
public interface ITool
{
    // --- Existing (from TOOL-ARCHITECTURE.md) ---
    string Name { get; }
    string Description { get; }
    JsonElement ParameterSchema { get; }
    bool IsEnabled { get; }
    Task<string> ExecuteAsync(string? argumentsJson, ToolContext context, CancellationToken ct);

    // --- New: Wake word binding ---

    /// <summary>
    /// Wake word configuration for this tool. Null = no wake word.
    /// Tools opt-in to wake word activation by returning a config.
    /// </summary>
    WakeWordBinding? WakeWord { get; }
}
```

### WakeWordBinding

```csharp
/// <summary>
/// Declares how a tool is activated by a wake word.
/// </summary>
public class WakeWordBinding
{
    /// <summary>
    /// Path to the Porcupine .ppn keyword file (embedded resource).
    /// e.g. "bodycam_look.ppn"
    /// </summary>
    public required string KeywordPath { get; init; }

    /// <summary>
    /// Sensitivity for this specific wake word [0.0 - 1.0].
    /// Higher = fewer misses, more false positives.
    /// </summary>
    public float Sensitivity { get; init; } = 0.5f;

    /// <summary>
    /// How the tool is invoked when the wake word is detected.
    /// </summary>
    public WakeWordMode Mode { get; init; } = WakeWordMode.QuickAction;

    /// <summary>
    /// Optional initial prompt injected into the session when triggered.
    /// e.g. "The user said 'BodyCam, find'. Ask them what to find."
    /// Null = execute tool immediately with no arguments.
    /// </summary>
    public string? InitialPrompt { get; init; }
}

public enum WakeWordMode
{
    /// <summary>
    /// Connect → execute tool → speak result → disconnect → back to Layer 2.
    /// Used for self-contained actions like "look" and "read".
    /// </summary>
    QuickAction,

    /// <summary>
    /// Connect → inject initial prompt → stay in full session.
    /// Used for tools that need follow-up input like "find" and "navigate".
    /// </summary>
    FullSession
}
```

### Tool<TArgs> Base Class

```csharp
public abstract class Tool<TArgs> : ITool where TArgs : class, new()
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual bool IsEnabled => true;
    public virtual JsonElement ParameterSchema => SchemaGenerator.Generate<TArgs>();

    /// <summary>Override to bind a wake word. Default: no wake word.</summary>
    public virtual WakeWordBinding? WakeWord => null;

    protected abstract Task<ToolResult> ExecuteAsync(
        TArgs args, ToolContext context, CancellationToken ct);

    // ... ITool.ExecuteAsync implementation unchanged
}
```

---

## Tools Declare Their Own Wake Words

No enum. No parallel arrays. Each tool owns its wake word:

```csharp
public class DescribeSceneTool : Tool<DescribeSceneArgs>
{
    public override string Name => "describe_scene";
    public override string Description => "Capture what the camera currently sees...";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "bodycam_look.ppn",
        Mode = WakeWordMode.QuickAction
        // No InitialPrompt — executes immediately with empty args
    };

    // ... ExecuteAsync unchanged
}
```

```csharp
public class ReadTextTool : Tool<ReadTextArgs>
{
    public override string Name => "read_text";
    public override string Description => "Read all visible text...";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "bodycam_read.ppn",
        Mode = WakeWordMode.QuickAction
    };

    // ... ExecuteAsync unchanged
}
```

```csharp
public class FindObjectTool : Tool<FindObjectArgs>
{
    public override string Name => "find_object";
    public override string Description => "Find a specific object...";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "bodycam_find.ppn",
        Mode = WakeWordMode.FullSession,
        InitialPrompt = "The user wants to find something. Ask them what to look for."
    };

    // ... ExecuteAsync unchanged
}
```

```csharp
public class SaveMemoryTool : Tool<SaveMemoryArgs>
{
    public override string Name => "save_memory";
    public override string Description => "Save information to memory...";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "bodycam_remember.ppn",
        Mode = WakeWordMode.FullSession,
        InitialPrompt = "The user wants to remember something. Ask what to save."
    };
}
```

```csharp
public class SetTranslationModeTool : Tool<SetTranslationModeArgs>
{
    public override string Name => "set_translation_mode";
    public override string Description => "Activate live translation...";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "bodycam_translate.ppn",
        Mode = WakeWordMode.FullSession,
        InitialPrompt = "The user wants translation. Ask which language."
    };
}
```

```csharp
public class MakePhoneCallTool : Tool<MakePhoneCallArgs>
{
    public override string Name => "make_phone_call";
    public override string Description => "Initiate a phone call...";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "bodycam_call.ppn",
        Mode = WakeWordMode.FullSession,
        InitialPrompt = "The user wants to make a phone call. Ask who to call."
    };
}
```

```csharp
public class NavigateToTool : Tool<NavigateToArgs>
{
    public override string Name => "navigate_to";
    public override string Description => "Start navigation...";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "bodycam_navigate.ppn",
        Mode = WakeWordMode.FullSession,
        InitialPrompt = "The user wants directions. Ask where to go."
    };
}
```

Tools without a wake word (e.g. `deep_analysis`) just don't override `WakeWord` — it stays `null`. They're only callable by the LLM.

---

## Special Wake Words (Not Tools)

Two wake words aren't tools — they're system commands:

| Wake Word | Action | Owner |
|-----------|--------|-------|
| "Hey BodyCam" | Start full session (Layer 2 → 3) | WakeWordService |
| "Go to sleep" | Sleep (Layer 2 → 1) | WakeWordService |

These are registered directly by the `WakeWordService`, not by tools:

```csharp
public class WakeWordService
{
    // System keywords (always registered)
    private static readonly SystemWakeWord[] SystemKeywords =
    [
        new("hey_bodycam.ppn", SystemAction.StartSession, 0.5f),
        new("go_to_sleep.ppn", SystemAction.GoToSleep, 0.5f)
    ];

    private record SystemWakeWord(string KeywordPath, SystemAction Action, float Sensitivity);
    private enum SystemAction { StartSession, GoToSleep }
}
```

---

## WakeWordService: Built From Tool Registry

The service dynamically collects wake words from all registered tools at startup:

```csharp
public class WakeWordService : IWakeWordService, IDisposable
{
    private readonly IEnumerable<ITool> _tools;
    private readonly IAudioInputService _audioInput;
    private readonly string _accessKey;

    private Porcupine? _porcupine;
    private CancellationTokenSource? _cts;

    // Built at StartAsync from tools + system keywords
    private WakeWordEntry[] _entries = [];

    public ListeningLayer CurrentLayer { get; private set; } = ListeningLayer.Sleep;
    public event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    public WakeWordService(
        IEnumerable<ITool> tools,
        IAudioInputService audioInput,
        ISettingsService settings)
    {
        _tools = tools;
        _audioInput = audioInput;
        _accessKey = settings.PicovoiceAccessKey ?? "";
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        // Collect all wake word bindings
        _entries = BuildEntries();

        if (_entries.Length == 0)
            return; // No wake words configured

        var paths = _entries.Select(e => e.KeywordPath).ToList();
        var sensitivities = _entries.Select(e => e.Sensitivity).ToList();

        _porcupine = Porcupine.FromKeywordPaths(_accessKey, paths, sensitivities: sensitivities);
        _cts = new CancellationTokenSource();
        CurrentLayer = ListeningLayer.WakeWord;

        _ = Task.Run(() => DetectionLoopAsync(_cts.Token), ct);
    }

    /// <summary>
    /// Build the keyword list from system words + tool bindings.
    /// No enum, no hardcoded array — it's derived from the tool registry.
    /// </summary>
    private WakeWordEntry[] BuildEntries()
    {
        var entries = new List<WakeWordEntry>();

        // System keywords (always first)
        entries.Add(new WakeWordEntry(
            "hey_bodycam.ppn", 0.5f,
            SystemAction: WakeWordSystemAction.StartSession));

        entries.Add(new WakeWordEntry(
            "go_to_sleep.ppn", 0.5f,
            SystemAction: WakeWordSystemAction.GoToSleep));

        // Tool keywords (from ITool.WakeWord)
        foreach (var tool in _tools.Where(t => t.IsEnabled && t.WakeWord is not null))
        {
            entries.Add(new WakeWordEntry(
                tool.WakeWord!.KeywordPath,
                tool.WakeWord.Sensitivity,
                Tool: tool));
        }

        return entries.ToArray();
    }

    private async Task DetectionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await GetNextAudioFrameAsync(ct);
            var keywordIndex = _porcupine!.Process(frame);

            if (keywordIndex < 0) continue;

            var entry = _entries[keywordIndex];

            if (entry.SystemAction is WakeWordSystemAction.GoToSleep)
            {
                CurrentLayer = ListeningLayer.Sleep;
                await StopAsync();
                WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs
                {
                    Action = WakeWordAction.GoToSleep
                });
            }
            else if (entry.SystemAction is WakeWordSystemAction.StartSession)
            {
                WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs
                {
                    Action = WakeWordAction.StartSession
                });
            }
            else if (entry.Tool is not null)
            {
                WakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs
                {
                    Action = WakeWordAction.InvokeTool,
                    Tool = entry.Tool
                });
            }
        }
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _porcupine?.Dispose();
        _porcupine = null;
        CurrentLayer = ListeningLayer.Sleep;
        return Task.CompletedTask;
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
```

### Internal Types

```csharp
/// <summary>
/// One entry in the keyword list — either a system action or a tool binding.
/// </summary>
internal record WakeWordEntry(
    string KeywordPath,
    float Sensitivity,
    WakeWordSystemAction? SystemAction = null,
    ITool? Tool = null);

internal enum WakeWordSystemAction { StartSession, GoToSleep }

/// <summary>
/// Event args for wake word detection. Carries enough info for the
/// orchestrator to act without a switch statement.
/// </summary>
public class WakeWordDetectedEventArgs : EventArgs
{
    public required WakeWordAction Action { get; init; }

    /// <summary>The tool to invoke (only set when Action == InvokeTool).</summary>
    public ITool? Tool { get; init; }
}

public enum WakeWordAction
{
    StartSession,   // "Hey BodyCam" → open full session
    GoToSleep,      // "Go to sleep" → Layer 1
    InvokeTool      // "BodyCam, look" etc. → execute the attached tool
}
```

---

## Orchestrator: No Switch Statement

The orchestrator handles wake word events with zero knowledge of specific tools:

```csharp
// In AgentOrchestrator:

private async void OnWakeWordDetected(object? sender, WakeWordDetectedEventArgs e)
{
    switch (e.Action)
    {
        case WakeWordAction.GoToSleep:
            if (IsRunning) await StopAsync();
            _wakeWordService.CurrentLayer = ListeningLayer.Sleep;
            DebugLog?.Invoke(this, "Going to sleep.");
            break;

        case WakeWordAction.StartSession:
            await StartFullSessionAsync();
            break;

        case WakeWordAction.InvokeTool:
            await InvokeToolFromWakeWordAsync(e.Tool!);
            break;
    }
}

/// <summary>
/// Invoke a tool triggered by a wake word. Uses the tool's own WakeWord
/// binding to decide quick-action vs full-session.
/// No switch on tool name — the tool carries all the info.
/// </summary>
private async Task InvokeToolFromWakeWordAsync(ITool tool)
{
    var binding = tool.WakeWord!;
    DebugLog?.Invoke(this, $"Wake word → {tool.Name} ({binding.Mode})");

    await StartFullSessionAsync();

    if (binding.Mode == WakeWordMode.QuickAction)
    {
        // Execute tool immediately with empty args, speak result, disconnect
        try
        {
            var result = await tool.ExecuteAsync(null, _toolContext, _cts?.Token ?? default);
            await _realtime.SendFunctionCallOutputAsync($"wakeword_{tool.Name}", result);
            // ResponseDone handler will auto-disconnect for quick actions
            _pendingQuickDisconnect = true;
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke(this, $"Quick action failed: {ex.Message}");
            await StopAsync();
        }
    }
    else // WakeWordMode.FullSession
    {
        // Inject initial prompt so the LLM knows what the user wants
        if (binding.InitialPrompt is not null)
        {
            await _realtime.InjectSystemMessageAsync(binding.InitialPrompt);
            await _realtime.CreateResponseAsync();
        }
        // Session stays open — user continues talking
    }
}

private async Task StartFullSessionAsync()
{
    if (!IsRunning)
        await StartAsync();
}
```

This is **three cases** — and they never change. The `WakeWordAction` enum has
exactly three values that are structural (start, sleep, invoke tool). Adding a
new tool with a wake word requires zero changes to the orchestrator.

---

## Data Flow

```
┌──────────────────────────────────────────────────────────────────┐
│ DI Container at startup                                          │
│                                                                  │
│  ITool registrations:                                            │
│    DescribeSceneTool  (WakeWord: "bodycam_look.ppn",  Quick)    │
│    ReadTextTool       (WakeWord: "bodycam_read.ppn",  Quick)    │
│    FindObjectTool     (WakeWord: "bodycam_find.ppn",  Full)     │
│    DeepAnalysisTool   (WakeWord: null)                          │
│    SaveMemoryTool     (WakeWord: "bodycam_remember.ppn", Full)  │
│    ...                                                           │
└──────────────┬───────────────────────────────────────────────────┘
               │
               │ IEnumerable<ITool> injected into:
               │
       ┌───────┴────────┐              ┌──────────────────┐
       │ WakeWordService │              │  ToolDispatcher   │
       │                 │              │                   │
       │ Collects tools  │              │ Collects tools    │
       │ with WakeWord   │              │ with Name         │
       │ != null         │              │                   │
       │                 │              │ Routes LLM        │
       │ Builds Porcupine│              │ function_call     │
       │ keyword list    │              │ → ITool.Execute   │
       └───────┬─────────┘              └──────────────────┘
               │                                 ▲
               │ WakeWordDetected event          │ FunctionCallReceived event
               │                                 │
               ▼                                 │
       ┌───────────────────────────────────────────┐
       │           AgentOrchestrator               │
       │                                           │
       │  OnWakeWordDetected:                      │
       │    StartSession → StartAsync()            │
       │    GoToSleep    → StopAsync()             │
       │    InvokeTool   → InvokeToolFromWakeWord  │
       │                   (uses tool.WakeWord     │
       │                    .Mode to decide        │
       │                    quick vs full)          │
       │                                           │
       │  OnFunctionCallReceived:                  │
       │    _toolDispatcher.ExecuteAsync(call)      │
       └───────────────────────────────────────────┘
```

Both paths converge on the same tool instances — a tool can be triggered by:
1. **LLM function call** → `ToolDispatcher` → `ITool.ExecuteAsync()`
2. **Wake word** → `WakeWordService` → orchestrator → `ITool.ExecuteAsync()`
3. **UI button** → ViewModel → orchestrator → `ITool.ExecuteAsync()`

Same tool. Same code. Three entry points.

---

## Adding a New Tool With Wake Word

One file. One DI line. Zero orchestrator changes.

```csharp
// 1. Create Tools/LookupAddressTool.cs

public class LookupAddressArgs
{
    [JsonPropertyName("query")]
    public required string Query { get; set; }
}

public class LookupAddressTool : Tool<LookupAddressArgs>
{
    public override string Name => "lookup_address";

    public override string Description =>
        "Look up the address of a business or place.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "bodycam_address.ppn",   // ← ship this .ppn file
        Mode = WakeWordMode.FullSession,
        InitialPrompt = "The user wants to look up an address. Ask what place."
    };

    protected override async Task<ToolResult> ExecuteAsync(
        LookupAddressArgs args, ToolContext ctx, CancellationToken ct)
    {
        return ToolResult.Success(new
        {
            instruction = $"Use your knowledge to provide the address of '{args.Query}'."
        });
    }
}
```

```csharp
// 2. Register in MauiProgram.cs
builder.Services.AddSingleton<ITool, LookupAddressTool>();
```

Done. The `WakeWordService` picks it up automatically from DI. The `ToolDispatcher`
picks it up automatically from DI. The orchestrator doesn't change.

---

## Comparison

| Aspect | 5.1 analysis (before) | This architecture (after) |
|--------|----------------------|--------------------------|
| Wake word list | Hardcoded enum + array | Derived from `ITool.WakeWord` |
| Routing | Switch on `WakeWordType` | `tool.WakeWord.Mode` decides behavior |
| Adding a wake word | Edit enum, array, switch | Set `WakeWord` property on tool |
| Tool ↔ wake word coupling | Implicit by index position | Explicit on the tool class itself |
| System actions (sleep/start) | Mixed into same enum | Separate `WakeWordSystemAction` |
| Orchestrator changes per tool | Yes | No |
