# Chat Transcript UI — Design

## Problem

The transcript panel shows each streaming token as a separate line:

```
AI: Hello
AI: !
AI: It's
AI: great
AI: to
AI: have
You: Bye.
AI: you
AI: here
AI: .
```

`OnOutputTranscriptDelta` fires per token and emits `TranscriptUpdated("AI: {delta}")`.
The `MainViewModel` appends each emission as a new line to a plain string.
Result: unreadable token-per-line output.

## Goal

Show a chat-style transcript with proper message grouping:

```
AI:  Hello! It's great to have you here.
You: Bye.
AI:  How can I help you today?
```

AI messages accumulate streaming deltas into a single entry.
User messages appear as complete entries (already work correctly).

---

## Design

### 1. TranscriptEntry Model

New model class to represent a single chat message entry.

```csharp
// Models/TranscriptEntry.cs
public class TranscriptEntry : ObservableObject
{
    private string _text = string.Empty;

    public string Role { get; init; }        // "AI" or "You"
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public string Display => $"{Role}: {Text}";   // bound to UI
}
```

`Text` is observable so the Label updates as deltas arrive.
`Display` is a computed read-only property (raise PropertyChanged for it when Text changes).

### 2. Orchestrator Event Changes

Replace the single `TranscriptUpdated` event with two granular events:

```csharp
// AgentOrchestrator.cs  (new events — old event stays for backward compat)
public event EventHandler<string>? TranscriptDelta;      // AI streaming token
public event EventHandler<string>? TranscriptCompleted;   // complete message (AI or user)
```

| Event | Source | Data |
|-------|--------|------|
| `TranscriptDelta` | `OnOutputTranscriptDelta` | raw token text (no "AI:" prefix) |
| `TranscriptCompleted` | `OnOutputTranscriptCompleted` | `"AI:{full text}"` |
| `TranscriptCompleted` | `OnInputTranscriptCompleted` | `"You:{full text}"` |

Keep the existing `TranscriptUpdated` event for debug log compat but stop using it for the main transcript UI.

### 3. MainViewModel Changes

Replace `string Transcript` with an `ObservableCollection<TranscriptEntry>`.

```csharp
public ObservableCollection<TranscriptEntry> Entries { get; } = [];

private TranscriptEntry? _currentAiEntry;
```

**Delta handler** — accumulates into current AI entry:
```csharp
_orchestrator.TranscriptDelta += (_, delta) =>
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        if (_currentAiEntry is null)
        {
            _currentAiEntry = new TranscriptEntry { Role = "AI" };
            Entries.Add(_currentAiEntry);
        }
        _currentAiEntry.Text += delta;
    });
};
```

**Completed handler** — finalizes AI entry or adds user entry:
```csharp
_orchestrator.TranscriptCompleted += (_, msg) =>
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        if (msg.StartsWith("You:"))
        {
            // Finalize any in-progress AI entry
            _currentAiEntry = null;
            Entries.Add(new TranscriptEntry
            {
                Role = "You",
                Text = msg[4..].Trim()
            });
        }
        else // AI completed
        {
            // Replace streamed text with final transcript (may differ)
            if (_currentAiEntry is not null)
                _currentAiEntry.Text = msg[3..].Trim();
            _currentAiEntry = null;
        }
    });
};
```

### 4. XAML Changes

Replace the plain `Label` with a `CollectionView`:

```xml
<!-- Transcript -->
<Frame Grid.Row="1" BorderColor="..." Padding="8" CornerRadius="4">
    <CollectionView
        x:Name="TranscriptList"
        ItemsSource="{Binding Entries}">
        <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="models:TranscriptEntry">
                <VerticalStackLayout Padding="4,2">
                    <Label FontSize="14" FontFamily="OpenSansRegular">
                        <Label.FormattedText>
                            <FormattedString>
                                <Span Text="{Binding Role}"
                                      FontAttributes="Bold"
                                      TextColor="{...}" />
                                <Span Text=": " />
                                <Span Text="{Binding Text}" />
                            </FormattedString>
                        </Label.FormattedText>
                    </Label>
                </VerticalStackLayout>
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</Frame>
```

Role label is bold. Different colors for AI vs You (e.g., purple vs gray).

### 5. Auto-Scroll

When a new entry is added or an existing entry's text changes, scroll to bottom:

```csharp
Entries.CollectionChanged += (_, _) =>
{
    if (Entries.Count > 0)
        TranscriptList.ScrollTo(Entries.Count - 1);
};
```

### 6. Clear Command

```csharp
ClearCommand = new RelayCommand(() =>
{
    Entries.Clear();
    _currentAiEntry = null;
});
```

---

## File Changes

| File | Change |
|------|--------|
| `Models/TranscriptEntry.cs` | **New** — ObservableObject with Role, Text, Display |
| `Orchestration/AgentOrchestrator.cs` | Add `TranscriptDelta` and `TranscriptCompleted` events |
| `ViewModels/MainViewModel.cs` | Replace `string Transcript` with `ObservableCollection<TranscriptEntry>`, add delta accumulation logic |
| `MainPage.xaml` | Replace `ScrollView > Label` with `CollectionView` using `DataTemplate` |
| `MainPage.xaml.cs` | No changes (binding handles everything) |

## Test Impact

| Test | Impact |
|------|--------|
| `OutputTranscriptDelta_EmitsTranscriptUpdated` | Update: also verify `TranscriptDelta` fires with raw token |
| `InputTranscriptCompleted_UpdatesSessionAndUI` | Update: verify `TranscriptCompleted` fires with `"You:{text}"` |
| New: `TranscriptDelta_AccumulatesIntoSingleEntry` | ViewModel test: fire 3 deltas → Entries has 1 item with concatenated text |
| New: `TranscriptCompleted_FinalizesAiEntry` | ViewModel test: delta + completed → _currentAiEntry is null, text replaced with final |
| New: `UserMessage_DoesNotMergeWithAiEntry` | ViewModel test: AI delta, then user completed → 2 entries |
| New: `Clear_ResetsEntries` | ViewModel test: add entries, clear → empty collection |

## Edge Cases

- **Rapid user interruption**: User speaks while AI is streaming → `InputTranscriptCompleted` arrives before `OutputTranscriptCompleted`. The delta handler creates a new AI entry, user message finalizes it via `_currentAiEntry = null`, then the AI completed event sees `_currentAiEntry` is null and creates nothing (the partial text is already in the collection, just incomplete — acceptable since the response was interrupted anyway).
- **Empty deltas**: Ignore empty/whitespace-only deltas (don't create entry for nothing).
- **Back-to-back AI responses**: Each `OutputTranscriptCompleted` sets `_currentAiEntry = null`, so next delta starts a new entry.
