# Step 15: Model/Voice Constants

**Priority:** P3 | **Effort:** Small | **Risk:** String arrays in ModelOptions are fragile and untestable

---

## Problem

`ModelOptions` uses `string[]` for model lists and raw string constants for defaults. Adding a new model requires updating the array and the `Label` method. A typo in a model name silently breaks model selection.

Current structure:

```csharp
public const string DefaultRealtime = "gpt-realtime-1.5";
public static readonly string[] RealtimeModels = ["gpt-realtime-1.5", "gpt-realtime-mini"];
```

## Steps

### 15.1 Add ModelInfo record

**File:** `src/BodyCam/ModelOptions.cs`

Add at the top of the file:

```csharp
public record ModelInfo(string Id, string Label);
```

### 15.2 Replace string arrays with ModelInfo arrays

```csharp
public static class ModelOptions
{
    // --- Realtime (voice) ---
    public const string DefaultRealtime = "gpt-realtime-1.5";
    public static readonly ModelInfo[] RealtimeModels =
    [
        new("gpt-realtime-1.5", "Realtime 1.5 (Premium)"),
        new("gpt-realtime-mini", "Realtime Mini (Budget)"),
    ];

    // --- Chat (text reasoning) ---
    public const string DefaultChat = "gpt-5.4-mini";
    public static readonly ModelInfo[] ChatModels =
    [
        new("gpt-5.4", "GPT-5.4 (Flagship)"),
        new("gpt-5.4-mini", "GPT-5.4 Mini"),
        new("gpt-5.4-nano", "GPT-5.4 Nano (Cheapest)"),
    ];

    // --- Vision ---
    public const string DefaultVision = "gpt-5.4";
    public static readonly ModelInfo[] VisionModels =
    [
        new("gpt-5.4", "GPT-5.4 (Flagship)"),
        new("gpt-5.4-mini", "GPT-5.4 Mini"),
    ];

    // --- Transcription ---
    public const string DefaultTranscription = "gpt-4o-mini-transcribe";
    public static readonly ModelInfo[] TranscriptionModels =
    [
        new("gpt-4o-mini-transcribe", "GPT-4o Mini Transcribe"),
        new("gpt-4o-transcribe", "GPT-4o Transcribe (Best)"),
    ];

    // --- Voice presets ---
    public const string DefaultVoice = "marin";
    public static readonly string[] Voices =
    [
        "alloy", "ash", "ballad", "coral", "echo",
        "fable", "marin", "sage", "shimmer", "verse",
    ];

    // --- Turn detection ---
    public const string DefaultTurnDetection = "semantic_vad";
    public static readonly string[] TurnDetectionModes = ["semantic_vad", "server_vad"];

    // --- Noise reduction ---
    public const string DefaultNoiseReduction = "near_field";
    public static readonly string[] NoiseReductionModes = ["near_field", "far_field"];

    /// <summary>
    /// Get label for a model ID. Checks all ModelInfo arrays.
    /// </summary>
    public static string Label(string modelId)
    {
        var all = RealtimeModels
            .Concat(ChatModels)
            .Concat(VisionModels)
            .Concat(TranscriptionModels);

        return all.FirstOrDefault(m => m.Id == modelId)?.Label ?? modelId;
    }
}
```

### 15.3 Update consumers

Search for usage of `ModelOptions.RealtimeModels` etc. in `SettingsViewModel` or XAML bindings. These currently iterate `string[]` — update to use `ModelInfo.Id` and `ModelInfo.Label`:

```csharp
// Before (in SettingsViewModel or Settings page)
foreach (var model in ModelOptions.RealtimeModels)
    // model is string

// After
foreach (var model in ModelOptions.RealtimeModels)
    // model.Id for the value, model.Label for display
```

### 15.4 Keep Voices / TurnDetectionModes / NoiseReductionModes as string[]

These don't have separate labels (they display the raw string), so keep them as `string[]`.

### 15.5 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```

Check `SettingsPage` on both Windows and Android to verify Picker displays labels correctly.
