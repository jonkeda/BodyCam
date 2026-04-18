# Phase 5 — Reduced Motion & Audio Cues

Respect the user's reduced motion preference and add short audio cues for
state changes and background activity.

---

## Why

### Reduced Motion
Users with vestibular disorders or motion sensitivity can be physically discomforted
by animations. The transcript items currently slide in (`TranslationY="20"` → 0)
and fade in (`Opacity="0"` → 1), and the thinking dots pulse continuously. OS
settings allow users to request reduced motion, but the app ignores this.

### Audio Cues
The app already has a full AI voice for spoken output — `SemanticScreenReader.Announce()`
would be redundant and would clash with the AI's speech. Instead, use short non-verbal
audio cues (earcons) to signal state changes and background activity. These help
sighted and non-sighted users alike.

---

## Files Changed

| File | Change |
|------|--------|
| `MainPage.xaml.cs` | Check `PreferReducedMotion` in animation handlers |
| `Services/AudioCueService.cs` | New — plays short audio cues |
| `MainViewModel.cs` | Play cues on state transitions |
| `ToolDispatcher.cs` | Play cue when tool starts/finishes |
| `Resources/Raw/` | Audio cue WAV files |

---

## Reduced Motion

### Detecting the Preference

.NET MAUI doesn't expose a direct `PreferReducedMotion` API. Use platform-specific
checks:

```csharp
internal static class MotionPreference
{
    public static bool PrefersReducedMotion
    {
        get
        {
#if WINDOWS
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            return !uiSettings.AnimationsEnabled;
#elif ANDROID
            var resolver = Android.App.Application.Context.ContentResolver;
            var scale = Android.Provider.Settings.Global.GetFloat(
                resolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f);
            return scale == 0f;
#elif IOS
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#else
            return false;
#endif
        }
    }
}
```

Place in `Services/` or `Helpers/`.

### EntryItem_Loaded — Conditional Animation

```csharp
// Before
private async void EntryItem_Loaded(object sender, EventArgs e)
{
    if (sender is not VisualElement element) return;
    await Task.WhenAll(
        element.FadeTo(1, 250, Easing.CubicOut),
        element.TranslateTo(0, 0, 250, Easing.CubicOut));
}

// After
private async void EntryItem_Loaded(object sender, EventArgs e)
{
    if (sender is not VisualElement element) return;

    if (MotionPreference.PrefersReducedMotion)
    {
        element.Opacity = 1;
        element.TranslationY = 0;
        return;
    }

    await Task.WhenAll(
        element.FadeTo(1, 250, Easing.CubicOut),
        element.TranslateTo(0, 0, 250, Easing.CubicOut));
}
```

### ThinkingDots_Loaded — Conditional Animation

```csharp
// Before
private async void ThinkingDots_Loaded(object sender, EventArgs e)
{
    if (sender is not HorizontalStackLayout layout) return;
    var dots = layout.Children.OfType<Ellipse>().ToList();
    if (dots.Count < 3) return;

    while (layout.IsVisible)
    {
        for (int i = 0; i < dots.Count; i++)
        {
            await dots[i].FadeTo(1.0, 200);
            await dots[i].FadeTo(0.3, 200);
        }
        await Task.Delay(100);
    }
}

// After
private async void ThinkingDots_Loaded(object sender, EventArgs e)
{
    if (sender is not HorizontalStackLayout layout) return;
    var dots = layout.Children.OfType<Ellipse>().ToList();
    if (dots.Count < 3) return;

    // With reduced motion, show static dots at full opacity
    if (MotionPreference.PrefersReducedMotion)
    {
        foreach (var dot in dots)
            dot.Opacity = 1.0;
        return;
    }

    while (layout.IsVisible)
    {
        for (int i = 0; i < dots.Count; i++)
        {
            await dots[i].FadeTo(1.0, 200);
            await dots[i].FadeTo(0.3, 200);
        }
        await Task.Delay(100);
    }
}
```

### XAML Change

The initial `Opacity="0" TranslationY="20"` in XAML causes a visual flash if
reduced motion skips the animation (element starts invisible, code sets to visible
without animation). Move the initial state into the animation handler:

```xml
<!-- Before -->
<VerticalStackLayout Padding="4,2" Opacity="0" TranslationY="20"
                     Loaded="EntryItem_Loaded">

<!-- After — start visible, let code-behind handle animation -->
<VerticalStackLayout Padding="4,2" Opacity="0" TranslationY="20"
                     Loaded="EntryItem_Loaded">
```

Actually, keep `Opacity="0" TranslationY="20"` — the code-behind handles both
paths (animate or snap). Without these initial values, non-reduced-motion users
would see a flash of content at final position before the animation starts.

The reduced motion path immediately sets `Opacity=1` and `TranslationY=0`, so
the element appears instantly — no flash.

---

## Audio Cues (Earcons)

Short non-verbal sounds (50–200ms) that indicate state without competing with the
AI voice. Think of the soft click/chime when AirPods connect or the subtle tone
when Siri activates.

### Cue Inventory

| Cue | When | Sound | Duration |
|-----|------|-------|----------|
| `activate.wav` | Session starts (Sleep → Active) | Rising two-tone chime | ~150ms |
| `deactivate.wav` | Session stops (Active → Sleep) | Falling two-tone | ~150ms |
| `listen.wav` | Wake word mode entered | Soft single ping | ~100ms |
| `tool_start.wav` | Tool begins execution | Subtle tick/click | ~50ms |
| `tool_done.wav` | Tool completes successfully | Soft confirmation ding | ~100ms |
| `error.wav` | Error occurred | Low double-buzz | ~200ms |
| `connected.wav` | Connection test succeeds | Bright ping | ~100ms |

### AudioCueService

```csharp
public interface IAudioCueService
{
    Task PlayAsync(string cueName, CancellationToken ct = default);
    bool IsEnabled { get; }
}
```

```csharp
public class AudioCueService : IAudioCueService
{
    private readonly ISettingsService _settings;

    public bool IsEnabled => _settings.AudioCuesEnabled;

    public async Task PlayAsync(string cueName, CancellationToken ct = default)
    {
        if (!IsEnabled) return;

        var player = AudioManager.Current.CreatePlayer(
            await FileSystem.OpenAppPackageFileAsync($"{cueName}.wav"));
        player.Play();
    }
}
```

Uses `Plugin.Maui.Audio` or the MAUI CommunityToolkit `AudioManager` — plays
through the device speaker at low volume, not through the AI's audio output
channel. This avoids interference with TTS playback.

**Volume:** Cues play at ~30% system volume. They should be audible but not
startle. Configurable via a "Cue volume" slider if needed (defer to M30 Polish).

### Instrumentation Points

**MainViewModel — state transitions:**

```csharp
public ListeningLayer CurrentLayer
{
    get => _currentLayer;
    set
    {
        if (SetProperty(ref _currentLayer, value))
        {
            // existing notifications...

            _ = _audioCues.PlayAsync(value switch
            {
                ListeningLayer.Sleep => "deactivate",
                ListeningLayer.WakeWord => "listen",
                ListeningLayer.ActiveSession => "activate",
                _ => ""
            });
        }
    }
}
```

**ToolDispatcher — tool execution:**

```csharp
public async Task<string> ExecuteAsync(string toolName, JsonElement? args, CancellationToken ct)
{
    _ = _audioCues.PlayAsync("tool_start");

    try
    {
        var result = await tool.ExecuteAsync(args, ct);
        _ = _audioCues.PlayAsync("tool_done");
        return result;
    }
    catch
    {
        _ = _audioCues.PlayAsync("error");
        throw;
    }
}
```

**SettingsViewModel — connection test:**

```csharp
// After TestConnectionCommand completes
_ = _audioCues.PlayAsync(success ? "connected" : "error");
```

### Audio File Requirements

- Format: WAV PCM 16-bit, 44.1kHz, mono
- Each file < 20KB (short cues)
- Place in `Resources/Raw/` (MAUI MauiAsset build action)
- Generate with a tone generator or use CC0/public domain earcons
- No voice, no music — only tonal sounds

### Settings

```csharp
public bool AudioCuesEnabled { get; set; } // default true
```

Toggle in Settings → Debug section:

```xml
<Label Text="Audio cues" VerticalOptions="Center" />
<Switch IsToggled="{Binding AudioCuesEnabled}" />
```

### DI Registration

```csharp
services.AddSingleton<IAudioCueService, AudioCueService>();
```

Inject into `MainViewModel`, `ToolDispatcher`, `SettingsViewModel`.

---

## Testing

### Reduced Motion — Windows

1. Settings → Accessibility → Visual effects → "Animation effects" → Off
2. Launch app
3. Send a message → verify transcript entry appears instantly (no slide-in)
4. Trigger thinking state → verify dots appear at full opacity (no pulsing)

### Reduced Motion — Android

1. Settings → Developer options → Animator duration scale → "Animation off"
2. Launch app
3. Same checks as Windows

### Audio Cues — Functional

1. Start a session → hear `activate.wav` chime
2. Stop session → hear `deactivate.wav` tone
3. Trigger a tool (e.g. "Look") → hear `tool_start.wav` click, then `tool_done.wav` ding
4. Force an error → hear `error.wav` buzz
5. Test connection in settings → hear `connected.wav` or `error.wav`
6. Disable "Audio cues" in settings → verify no sounds play

### Audio Cues — No Interference

1. Start a session and trigger a tool while AI is speaking
2. Verify cue sounds don't interrupt or garble the AI voice
3. Verify cues play through device speaker, not BT audio output

---

## Exit Criteria

- `MotionPreference.PrefersReducedMotion` utility exists with Windows + Android implementations
- `EntryItem_Loaded` skips animation when reduced motion is preferred
- `ThinkingDots_Loaded` shows static dots when reduced motion is preferred
- `IAudioCueService` + `AudioCueService` exist with 7 audio cue files
- Cues play on state transitions, tool start/done, and errors
- Cues are toggleable via settings (default on)
- Cues don't interfere with AI voice output
- Manual testing passed with animations disabled on both platforms
