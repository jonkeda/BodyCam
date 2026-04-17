# M2 Implementation — Step 5: Transcript UI Binding

**Depends on:** Step 3 (ConversationAgent streams tokens)
**Produces:** Updated `MainViewModel` that handles Mode B streaming deltas, differentiated user/AI entries

---

## Why This Step?

The current `MainViewModel` already handles transcript display via `TranscriptDelta` and `TranscriptCompleted` events. For Mode B, the same events fire (the orchestrator fires `TranscriptDelta` from the ConversationAgent's streaming output). However, we need to:

1. Ensure streaming deltas from the ConversationAgent create and update `TranscriptEntry` objects correctly
2. Show a visual indicator that Mode B is active (the AI "thinks" before speaking)
3. Handle the case where text appears before audio starts

---

## Tasks

### 5.1 — Verify existing event flow works for Mode B

The orchestrator already fires:
- `TranscriptCompleted` with `"You:{transcript}"` when user finishes speaking
- `TranscriptDelta` with each token as ConversationAgent streams
- `TranscriptCompleted` with `"AI:{reply}"` when ConversationAgent finishes

The existing `MainViewModel` subscription should handle this correctly since Mode B fires the same events. **Verify this works and fix any issues.**

### 5.2 — Add "thinking" status indicator

**File:** `src/BodyCam/ViewModels/MainViewModel.cs` — MODIFY

When Mode B is active and the ConversationAgent is processing, update `StatusText`:

```csharp
// In constructor, subscribe to orchestrator events:
_orchestrator.ConversationReplyDelta += (_, _) =>
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        if (StatusText == "Thinking...")
            StatusText = "Speaking...";
    });
};
```

In the `OnInputTranscriptCompleted` path (via existing `TranscriptCompleted` "You:" handler), set status:

```csharp
// When user message arrives in Mode B, update status
if (msg.StartsWith("You:") && _settingsService.Mode == ConversationMode.Separated)
{
    StatusText = "Thinking...";
}
```

### 5.3 — Add mode indicator to UI

**File:** `src/BodyCam/MainPage.xaml` — MODIFY

Add a small label showing the active mode next to the status text:

```xml
<Label
    Text="{Binding ModeLabel}"
    FontSize="12"
    VerticalOptions="Center"
    TextColor="{AppThemeBinding Light=#999, Dark=#666}" />
```

**File:** `src/BodyCam/ViewModels/MainViewModel.cs` — MODIFY

```csharp
public string ModeLabel => _settingsService.Mode == ConversationMode.Separated
    ? "[Mode B]"
    : "[Realtime]";
```

Refresh `ModeLabel` when starting a session (in `ToggleAsync`):

```csharp
OnPropertyChanged(nameof(ModeLabel));
```

### 5.4 — Auto-scroll transcript on new entries

**File:** `src/BodyCam/MainPage.xaml.cs` — MODIFY

The `CollectionView` should auto-scroll to the latest entry. If not already implemented:

```csharp
private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
{
    if (e.Action == NotifyCollectionChangedAction.Add && _vm.Entries.Count > 0)
    {
        TranscriptList.ScrollTo(_vm.Entries.Count - 1, position: ScrollToPosition.End, animate: false);
    }
}
```

---

## Verification

- [ ] Mode A: transcript display works exactly as before
- [ ] Mode B: user speech shows as "You: {text}" immediately
- [ ] Mode B: AI reply streams word-by-word in the transcript list
- [ ] Mode B: status shows "Thinking..." → "Speaking..." → "Listening..."
- [ ] Mode label shows "[Realtime]" or "[Mode B]" in the header
- [ ] Transcript auto-scrolls to latest entry

---

## Notes

This step is intentionally small — most of the UI binding already works because Mode B fires the same events as Mode A. The changes are cosmetic/UX: status indicators and mode labels.
