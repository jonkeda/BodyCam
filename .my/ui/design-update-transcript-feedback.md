# Design Update: Transcript List Live Feedback

## Problem

1. When a new `TranscriptEntry` is added to the list (user message, AI response), the item appears but there's no visual cue drawing attention to it.
2. When the AI is processing (between the user action and the first `TranscriptDelta`), there's no indication the system is working — the user stares at a static list.

## Current State

- `TranscriptEntry` has: `Role`, `Text` (observable, streams deltas), `Image`, `ImageCaption`, `RoleColor`.
- `MainViewModel.Entries` is an `ObservableCollection<TranscriptEntry>`.
- `CollectionView` auto-scrolls to bottom on `CollectionChanged` (code-behind).
- AI text arrives incrementally via `TranscriptDelta` events — `_currentAiEntry.Text += delta`.
- No `IsThinking` / `IsWorking` state. No animations or `ActivityIndicator` anywhere.

## Design

### 1. New Entry Slide-In

When a `TranscriptEntry` is added to the `CollectionView`, it should animate in so the user notices it.

**Approach — XAML `ItemTemplate` with implicit animation:**

- Wrap each item's `VerticalStackLayout` in a container that starts with `Opacity="0"` and `TranslationY="20"`.
- On `Loaded`, animate to `Opacity="1"` and `TranslationY="0"` over 250ms with `Easing.CubicOut`.
- This is a code-behind animation triggered from the DataTemplate's root element `Loaded` event, avoiding custom renderers.

**Implementation detail:**

```xml
<!-- In DataTemplate -->
<VerticalStackLayout Padding="4,2" Opacity="0" TranslationY="20"
                     Loaded="EntryItem_Loaded">
    ...
</VerticalStackLayout>
```

```csharp
// MainPage.xaml.cs
private async void EntryItem_Loaded(object sender, EventArgs e)
{
    if (sender is not VisualElement element) return;
    await Task.WhenAll(
        element.FadeTo(1, 250, Easing.CubicOut),
        element.TranslateTo(0, 0, 250, Easing.CubicOut));
}
```

### 2. AI Thinking Indicator

When the AI is working (entry exists but text is empty / still waiting for first delta), show a pulsing dot pattern inside the list item itself.

**Model changes — `TranscriptEntry`:**

```csharp
private bool _isThinking;
public bool IsThinking
{
    get => _isThinking;
    set => SetProperty(ref _isThinking, value);
}
```

**ViewModel flow — `MainViewModel`:**

1. When `_currentAiEntry` is created (in `TranscriptDelta` handler), set `IsThinking = true`.
2. On first non-empty delta arriving, set `IsThinking = false`.
3. On `TranscriptCompleted` for AI, ensure `IsThinking = false`.

Alternatively, create the AI entry eagerly when the user triggers an action (Look, Read, etc.) so the thinking indicator appears immediately — before any delta arrives. This gives instant feedback:

```csharp
// In each action command (LookCommand, etc.) or in a shared helper:
_currentAiEntry = new TranscriptEntry { Role = "AI", IsThinking = true };
Entries.Add(_currentAiEntry);

// Then in TranscriptDelta handler:
if (_currentAiEntry is not null && _currentAiEntry.IsThinking)
    _currentAiEntry.IsThinking = false;
_currentAiEntry!.Text += delta;
```

**XAML — thinking indicator inside the `DataTemplate`:**

```xml
<DataTemplate x:DataType="models:TranscriptEntry">
    <VerticalStackLayout Padding="4,2" Opacity="0" TranslationY="20"
                         Loaded="EntryItem_Loaded">

        <!-- Thinking dots — visible only while IsThinking -->
        <HorizontalStackLayout IsVisible="{Binding IsThinking}"
                               Spacing="6" Padding="4,8">
            <Ellipse x:Name="Dot1" WidthRequest="8" HeightRequest="8"
                     Fill="{Binding RoleColor}" Opacity="0.3" />
            <Ellipse x:Name="Dot2" WidthRequest="8" HeightRequest="8"
                     Fill="{Binding RoleColor}" Opacity="0.3" />
            <Ellipse x:Name="Dot3" WidthRequest="8" HeightRequest="8"
                     Fill="{Binding RoleColor}" Opacity="0.3" />
        </HorizontalStackLayout>

        <!-- Normal content — hidden while thinking -->
        <Label Text="{Binding DisplayText}"
               IsVisible="{Binding IsThinking, Converter={StaticResource InvertBool}}"
               FontSize="14"
               FontFamily="OpenSansRegular"
               TextColor="{Binding RoleColor}" />
        <!-- ... image, caption ... -->
    </VerticalStackLayout>
</DataTemplate>
```

**Dot pulse animation — code-behind:**

The three dots pulse sequentially (opacity 0.3 → 1.0 → 0.3) with staggered delays, creating a "typing" feel. Triggered from `Loaded` on the `HorizontalStackLayout`, runs in a loop until `IsThinking` becomes `false`.

```csharp
// MainPage.xaml.cs
private async void ThinkingDots_Loaded(object sender, EventArgs e)
{
    if (sender is not HorizontalStackLayout layout) return;

    var dots = layout.Children.OfType<Microsoft.Maui.Controls.Shapes.Ellipse>().ToList();
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
```

## Summary of Changes

| File | Change |
|------|--------|
| `Models/TranscriptEntry.cs` | Add `bool IsThinking` observable property |
| `ViewModels/MainViewModel.cs` | Create AI entry eagerly with `IsThinking = true`; clear on first delta |
| `MainPage.xaml` | Add `Loaded="EntryItem_Loaded"` + initial opacity/translation to item template; add thinking dots `HorizontalStackLayout` with `Loaded="ThinkingDots_Loaded"` |
| `MainPage.xaml.cs` | Add `EntryItem_Loaded` slide-in animation; add `ThinkingDots_Loaded` pulse loop |

## Visual Behavior

1. **User taps "Look"** → AI entry appears immediately, slides in from below, shows pulsing dots.
2. **First delta arrives** → dots disappear, text label becomes visible, text streams in character-by-character.
3. **User message inserted** → slides in above the AI entry.
4. **AI finishes** → `IsThinking` is already `false`, entry shows final text.

## Notes

- No third-party animation libraries needed — uses built-in MAUI `FadeTo`/`TranslateTo`.
- The pulse loop exits naturally when `IsVisible` becomes `false` (bound to `IsThinking`).
- The `InvertBool` converter already exists in the project (`Converters/InvertBoolConverter.cs`).
- Auto-scroll already works via existing `CollectionChanged` handler in code-behind.
