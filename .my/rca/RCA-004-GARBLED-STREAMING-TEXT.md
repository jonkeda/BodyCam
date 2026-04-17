# RCA-004: Garbled Streaming Transcript Text

## Symptom

During a conversation, the AI's streaming response text became garbled partway through (after "butter"). The final completed text appeared clean — the garbling was visible during the streaming delta phase.

## Event Flow

```
1. response.audio_transcript.delta "That sounds"        → Text = "That sounds"
2. response.audio_transcript.delta " great! A simple"   → Text += " great! A simple"
3. response.audio_transcript.delta " recipe could be"   → Text += " recipe could be"
...
N. response.audio_transcript.delta " butter"            → Text += " butter"
N+1. response.audio_transcript.delta ", eggs"           → Text = GARBLED HERE
...
M. response.audio_transcript.done "That sounds great! A simple recipe..."  → Text = CLEAN
```

The `TranscriptCompleted` handler overwrites with the final clean text from `response.audio_transcript.done`, so the end state is correct — but the user saw garbled text during streaming.

## Root Cause: FormattedString binding + rapid PropertyChanged

The transcript is rendered using a `FormattedString` with `Span` bindings:

```xml
<Label.FormattedText>
    <FormattedString>
        <Span Text="{Binding Role}" FontAttributes="Bold" />
        <Span Text=": " FontAttributes="Bold" />
        <Span Text="{Binding Text}" />
    </FormattedString>
</Label.FormattedText>
```

Each `_currentAiEntry.Text += delta` calls `SetProperty` → `PropertyChanged` → MAUI re-evaluates the `FormattedString`. With ~15-30 deltas per response arriving in rapid succession, every single one triggers a full `FormattedString` rebuild and re-layout.

**MAUI's `FormattedString` with data-bound `Span` elements is not designed for high-frequency updates.** When `PropertyChanged` fires rapidly:

1. The `Span` binding updates the text
2. MAUI invalidates the `FormattedString` and the `Label` layout
3. The layout engine re-measures and re-renders
4. Before rendering completes, the NEXT delta fires `PropertyChanged`
5. The layout is invalidated again mid-render

This causes the visible text to momentarily display in a corrupted state — characters from the previous render mixed with characters from the new text. The effect is worse for longer strings (more layout work) and when deltas arrive in quick bursts.

### Contributing factor: Per-delta PropertyChanged firing

```csharp
_currentAiEntry.Text += delta;
```

This calls the `Text` setter, which calls `SetProperty`, which fires `PropertyChanged` for *every single delta*. For a typical response, this means 15-30 property change notifications in under a second.

## Proposed Fix: Batch delta updates

Instead of firing `PropertyChanged` for every delta, accumulate deltas in a `StringBuilder` and update the bound `Text` property on a throttled timer or after a batch of deltas:

```csharp
// Option A: Use a StringBuilder + throttle
private readonly StringBuilder _pendingText = new();
private bool _updateScheduled;

_orchestrator.TranscriptDelta += (_, delta) =>
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        if (string.IsNullOrEmpty(delta)) return;

        if (_currentAiEntry is null)
        {
            _currentAiEntry = new TranscriptEntry { Role = "AI" };
            Entries.Add(_currentAiEntry);
        }

        _pendingText.Append(delta);

        if (!_updateScheduled)
        {
            _updateScheduled = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_currentAiEntry is not null)
                    _currentAiEntry.Text = _pendingText.ToString();
                _updateScheduled = false;
            });
        }
    });
};
```

**Option B (simpler):** Bypass `FormattedString` — use a simple `Label` with `StringFormat` or just concatenate role + text in the binding, avoiding the complex `Span` layout entirely.

**Option C (simplest):** Use a plain `Label Text="{Binding DisplayText}"` where `DisplayText` is a computed property that returns `$"{Role}: {Text}"`. Single property, no `FormattedString`, no `Span` rebuilds.

## Files Involved

| File | Role |
|------|------|
| `src/BodyCam/ViewModels/MainViewModel.cs` | Delta handler fires PropertyChanged per-delta |
| `src/BodyCam/Models/TranscriptEntry.cs` | `Text` property fires PropertyChanged on every set |
| `src/BodyCam/MainPage.xaml` | `FormattedString` with `Span` bindings — expensive to re-layout |

## Severity

**Low-Medium** — Text is garbled momentarily during streaming but auto-corrects when `TranscriptCompleted` fires. Audio playback is unaffected.
