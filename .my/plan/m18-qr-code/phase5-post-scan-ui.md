# M18 Phase 5 — Post-Scan UI & Voice Actions

**Status:** NOT STARTED  
**Depends on:** M18 Phase 4

---

## Goal

After a QR code or barcode is scanned, present the user with two parallel interaction paths:

1. **Visual** — an action card overlay with tappable buttons (works like the existing snapshot overlay)
2. **Voice** — the AI announces what it found and offers the same actions verbally

Both paths trigger the same underlying action. The user picks whichever is natural in the moment — tap a button while looking at the screen, or respond by voice while hands-free.

---

## User Flow

```
Scan completes (Phase 1 tool returns result)
       │
       ├─── Visual path ──────────────────────────────────┐
       │    ScanResultOverlay appears on camera tab        │
       │    Shows: content summary + action buttons        │
       │    User taps a button → action fires              │
       │    Overlay dismisses                              │
       │                                                   │
       ├─── Voice path ───────────────────────────────────┐│
       │    AI says: "I found a WiFi network called        ││
       │    CoffeeShop. Want me to connect, save the       ││
       │    password, or ignore it?"                       ││
       │    User responds by voice → AI calls tool/action  ││
       │    Overlay auto-dismisses when AI acts            ││
       │                                                   ││
       └─── Either path dismisses the overlay ─────────────┘┘
```

---

## Visual Path — ScanResultOverlay

### 1. `ScanResultOverlay` View

```
Pages/Main/Views/ScanResultOverlay.xaml
```

An overlay on the camera tab, same pattern as the existing snapshot overlay. Appears when `ShowScanResult` is `true`.

```xml
<Grid IsVisible="{Binding ShowScanResult}"
      BackgroundColor="#80000000"
      Padding="16">
    <Border StrokeShape="RoundRectangle 12"
            BackgroundColor="{AppThemeBinding Light=White, Dark=#2A2A2A}"
            Padding="16"
            VerticalOptions="Center"
            HorizontalOptions="Center"
            MaximumWidthRequest="360">
        <VerticalStackLayout Spacing="12">

            <!-- Content type icon + label -->
            <HorizontalStackLayout Spacing="8" HorizontalOptions="Center">
                <Label Text="{Binding ScanResultIcon}"
                       FontSize="24" VerticalOptions="Center" />
                <Label Text="{Binding ScanResultTitle}"
                       FontSize="18" FontAttributes="Bold"
                       VerticalOptions="Center" />
            </HorizontalStackLayout>

            <!-- Parsed content summary -->
            <Label AutomationId="ScanResultContent"
                   Text="{Binding ScanResultSummary}"
                   FontSize="14"
                   HorizontalTextAlignment="Center"
                   MaxLines="4"
                   LineBreakMode="TailTruncation" />

            <!-- Action buttons (generated from handler.SuggestedActions) -->
            <VerticalStackLayout BindableLayout.ItemsSource="{Binding ScanActions}"
                                 Spacing="8">
                <BindableLayout.ItemTemplate>
                    <DataTemplate x:DataType="models:ContentAction">
                        <Button Text="{Binding Label}"
                                Command="{Binding Command}"
                                AutomationId="{Binding Label}"
                                Style="{StaticResource ActionButton}"
                                HorizontalOptions="Fill" />
                    </DataTemplate>
                </BindableLayout.ItemTemplate>
            </VerticalStackLayout>

        </VerticalStackLayout>
    </Border>
</Grid>
```

**Key points:**
- Reuses `ContentAction` model — each button has a `Label` + `Command`
- Buttons are data-bound from `ScanActions` collection — populated by the handler's `SuggestedActions`
- Dismiss action is always the last button ("Ignore" / "Dismiss")
- Auto-dismissed after 30 seconds if no interaction

### 2. Content Type → Icon + Title Mapping

Each `IQrContentHandler` gets two additional properties:

```csharp
public interface IQrContentHandler
{
    // ... existing members from Phase 4 ...

    /// <summary>Emoji icon for the overlay card.</summary>
    string Icon { get; }

    /// <summary>Human-readable title (e.g. "WiFi Network", "Website").</summary>
    string DisplayName { get; }

    /// <summary>
    /// Format parsed content into a short summary for the overlay.
    /// E.g. "CoffeeShop (WPA)" for WiFi, or the URL for links.
    /// </summary>
    string Summarize(Dictionary<string, object> parsed);
}
```

| Handler | Icon | DisplayName | Summarize example |
|---------|------|-------------|-------------------|
| `UrlContentHandler` | 🔗 | Website | `https://example.com/menu` |
| `WifiContentHandler` | 📶 | WiFi Network | `CoffeeShop (WPA)` |
| `VCardContentHandler` | 👤 | Contact | `Jane Doe — Acme Corp` |
| `EmailContentHandler` | ✉️ | Email | `hello@example.com` |
| `PhoneContentHandler` | 📞 | Phone Number | `+1 555-123-4567` |
| `PlainTextContentHandler` | 📝 | Text | First 80 chars of content |

### 3. MainViewModel — Scan Result Properties

```csharp
// --- Scan result overlay state ---

private bool _showScanResult;
public bool ShowScanResult
{
    get => _showScanResult;
    set => SetProperty(ref _showScanResult, value);
}

private string _scanResultIcon = string.Empty;
public string ScanResultIcon
{
    get => _scanResultIcon;
    set => SetProperty(ref _scanResultIcon, value);
}

private string _scanResultTitle = string.Empty;
public string ScanResultTitle
{
    get => _scanResultTitle;
    set => SetProperty(ref _scanResultTitle, value);
}

private string _scanResultSummary = string.Empty;
public string ScanResultSummary
{
    get => _scanResultSummary;
    set => SetProperty(ref _scanResultSummary, value);
}

public ObservableCollection<ContentAction> ScanActions { get; } = [];
```

### 4. Populating the Overlay + Transcript Entry

Called from `MainViewModel` when the scan tool returns a successful result. The orchestrator fires an event; the ViewModel catches it, populates the overlay card, **and** adds a transcript entry with a "Show actions" button.

```csharp
// Holds the last scan context so the transcript button can reopen the overlay
private IQrContentHandler? _lastScanHandler;
private Dictionary<string, object>? _lastScanParsed;
private string? _lastScanRawContent;

internal void ShowScanResultCard(IQrContentHandler handler, Dictionary<string, object> parsed, string rawContent)
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        // Remember for reopen
        _lastScanHandler = handler;
        _lastScanParsed = parsed;
        _lastScanRawContent = rawContent;

        // --- Overlay ---
        ScanResultIcon = handler.Icon;
        ScanResultTitle = handler.DisplayName;
        ScanResultSummary = handler.Summarize(parsed);

        ScanActions.Clear();
        foreach (var action in handler.SuggestedActions)
        {
            ScanActions.Add(new ContentAction
            {
                Label = action,
                Icon = "",
                Command = new RelayCommand(() => ExecuteScanAction(action, handler, parsed, rawContent))
            });
        }

        ShowScanResult = true;
        _ = AutoDismissScanResultAsync();

        // --- Transcript entry ---
        var entry = new TranscriptEntry
        {
            Role = "Scan",
            Text = $"{handler.Icon} {handler.DisplayName}: {handler.Summarize(parsed)}"
        };
        entry.Actions.Add(new ContentAction
        {
            Label = "Show actions",
            Icon = "↩️",
            Command = new RelayCommand(ReopenScanResultCard)
        });
        entry.NotifyActionsChanged();
        Entries.Add(entry);
    });
}

private void ReopenScanResultCard()
{
    if (_lastScanHandler is not null && _lastScanParsed is not null && _lastScanRawContent is not null)
        ShowScanResultCard(_lastScanHandler, _lastScanParsed, _lastScanRawContent);
}

private void ExecuteScanAction(string action, IQrContentHandler handler, Dictionary<string, object> parsed, string rawContent)
{
    ShowScanResult = false;

    // Send the user's choice to the AI as a text message so it can act
    var prompt = $"The user chose \"{action}\" for the scanned {handler.ContentType}: {rawContent}";
    _ = _orchestrator.SendTextAsync(prompt);
}

private async Task AutoDismissScanResultAsync()
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    ShowScanResult = false;
}
```

**Key behaviors:**
- The transcript entry persists in the conversation history — the user can scroll back and tap "Show actions" to reopen the overlay at any time
- Reopening creates a fresh overlay with the same handler/parsed data
- The entry uses a `"Scan"` role to get a distinct color (see section 4b below)
- **Why route actions through the AI?** The AI already has tools for opening URLs (`Launcher.OpenAsync`), saving to memory (`SaveMemoryTool`), making calls (`MakePhoneCallTool`), etc. By sending the user's choice as a prompt, the AI picks the right tool automatically — no new dispatch code needed

### 4b. Transcript Entry Rendering

The current `CollectionView.ItemTemplate` in `MainPage.xaml` doesn't render `Actions`. Add an actions row below the existing content:

```xml
<DataTemplate x:DataType="models:TranscriptEntry">
    <VerticalStackLayout Padding="4,2">
        <Label Text="{Binding DisplayText}" ... />
        <Image ... />
        <Label ... /> <!-- caption -->

        <!-- Inline action buttons (scan results, links, etc.) -->
        <HorizontalStackLayout IsVisible="{Binding HasActions}"
                               BindableLayout.ItemsSource="{Binding Actions}"
                               Spacing="8" Margin="0,4,0,0">
            <BindableLayout.ItemTemplate>
                <DataTemplate x:DataType="models:ContentAction">
                    <Button Text="{Binding Label}"
                            Command="{Binding Command}"
                            FontSize="12"
                            HeightRequest="32"
                            Padding="8,0"
                            CornerRadius="4"
                            BackgroundColor="{AppThemeBinding Light=#E3F2FD, Dark=#1A3A5C}"
                            TextColor="{AppThemeBinding Light=#1565C0, Dark=#64B5F6}" />
                </DataTemplate>
            </BindableLayout.ItemTemplate>
        </HorizontalStackLayout>
    </VerticalStackLayout>
</DataTemplate>
```

This renders `ContentAction` buttons for **any** transcript entry that has them — not just scan results. Future features (detected URLs in AI responses, etc.) can reuse the same pattern.

### 4c. Scan Role Color

Add a `"Scan"` entry to `TranscriptEntry.RoleColor`:

```csharp
public Color RoleColor => (Role, IsLightTheme) switch
{
    ("You", true)  => Color.FromArgb("#2E7D32"),
    ("You", false) => Color.FromArgb("#81C784"),
    ("AI", true)   => Color.FromArgb("#1565C0"),
    ("AI", false)  => Color.FromArgb("#64B5F6"),
    ("Scan", true) => Color.FromArgb("#E65100"),   // orange
    ("Scan", false) => Color.FromArgb("#FFB74D"),
    (_, true)      => Color.FromArgb("#616161"),
    (_, false)     => Color.FromArgb("#BDBDBD"),
};
```

### 5. Wiring — Orchestrator → ViewModel

`ScanQrCodeTool` already returns a `ToolResult` with `content_type` and `details`. The connection point is in `AgentOrchestrator` after tool dispatch:

```csharp
// In HandleResponseDoneAsync, after ToolDispatcher returns a successful result for scan_qr_code:
if (tool.Name == "scan_qr_code" && result.IsSuccess)
{
    var json = JsonDocument.Parse(result.Json).RootElement;
    if (json.TryGetProperty("found", out var found) && found.GetBoolean())
    {
        var content = json.GetProperty("content").GetString()!;
        var handler = _contentResolver.Resolve(content);
        var parsed = handler.Parse(content);
        ScanResultReady?.Invoke(this, new ScanResultEventArgs(handler, parsed, content));
    }
}
```

New event on `AgentOrchestrator`:

```csharp
public event EventHandler<ScanResultEventArgs>? ScanResultReady;

public record ScanResultEventArgs(IQrContentHandler Handler, Dictionary<string, object> Parsed, string RawContent);
```

`MainViewModel` subscribes:

```csharp
_orchestrator.ScanResultReady += (_, e) => ShowScanResultCard(e.Handler, e.Parsed, e.RawContent);
```

### 6. Overlay Placement in MainPage.xaml

Add the overlay inside the camera tab grid, after the snapshot overlay:

```xml
<!-- Camera tab -->
<Grid IsVisible="{Binding ShowCameraTab}">
    <!-- ... existing camera + snapshot overlay ... -->

    <!-- Scan result action card -->
    <views:ScanResultOverlay />
</Grid>
```

Also add to the transcript tab as an overlay, so the card is visible regardless of which tab is active:

```xml
<!-- Row 1: Content area -->
<Grid Grid.Row="1">
    <!-- ... transcript + camera tabs ... -->

    <!-- Scan result card — floats above both tabs -->
    <views:ScanResultOverlay />
</Grid>
```

---

## Voice Path

The voice path requires **no new code** — it's already handled by the existing AI flow:

1. `ScanQrCodeTool` returns enriched JSON (Phase 4) with `content_type`, `details`, `suggested_actions`
2. The AI's system prompt tells it to read results aloud and ask what to do
3. The AI generates a natural announcement: *"I found a WiFi network called CoffeeShop, secured with WPA. Want me to connect, save the password, or ignore it?"*
4. User responds by voice → AI calls the appropriate existing tool

The `suggested_actions` array guides the AI so it offers the same options that appear as buttons.

### System Prompt Addition

Add to the agent's system prompt (in `ConversationAgent` or orchestrator config):

```
When a QR code scan returns results:
- Announce what you found using the content_type and details fields naturally.
  Examples:
    - URL: "I found a link to example.com/menu"
    - WiFi: "I found a WiFi network called CoffeeShop, secured with WPA"
    - Contact: "I found a contact card for Jane Doe from Acme Corp"
    - Phone: "I found a phone number: 555-123-4567"
- Offer the suggested_actions as choices.
- Wait for the user to respond before acting.
- If the user taps an on-screen button (you'll receive a text message with their choice), act on it immediately without re-asking.
```

---

## Dual-Path Interaction — Edge Cases

| Scenario | Behavior |
|----------|----------|
| User taps button before AI finishes speaking | Overlay dismisses, choice sent to AI, AI stops offering and acts |
| User responds by voice while overlay is visible | AI acts, overlay auto-dismisses (via orchestrator event) |
| AI finishes speaking, user does nothing for 30s | Overlay auto-dismisses; transcript entry remains with "Show actions" button |
| User says "ignore" or taps Ignore | Overlay dismisses, no action taken; transcript entry still available |
| Second scan while overlay is open | New scan replaces current overlay; both transcript entries remain |
| Scan on transcript tab | Overlay floats above transcript — same card; entry appended to transcript |
| User scrolls back and taps "Show actions" | Overlay reopens with original scan data |
| User taps "Show actions" after a second scan | Reopens the **most recent** scan (last-scan-wins) |

### Dismiss on AI Action

When the AI processes the voice response and calls a tool (e.g., opens a URL), the overlay should dismiss. This is handled by listening for the next tool dispatch after a scan:

```csharp
// In MainViewModel, when orchestrator fires any tool completion after scan
_orchestrator.TranscriptCompleted += (_, _) =>
{
    if (ShowScanResult)
        ShowScanResult = false;
};
```

---

## Accessibility

- All overlay buttons have `AutomationId` set to the action label
- `ScanResultContent` label has an AutomationId for UI testing
- Screen readers announce: icon + title + summary via `SemanticProperties`
- Buttons are focusable and keyboard-navigable on desktop

```xml
<Label SemanticProperties.Description="{Binding ScanResultTitle}"
       SemanticProperties.Hint="Scan result type" />
<Label SemanticProperties.Description="{Binding ScanResultSummary}"
       SemanticProperties.Hint="Scanned content" />
```

---

## Tests

| Test | File |
|------|------|
| Overlay shows when `ShowScanResult` is true | `MainViewModelTests.cs` |
| `ShowScanResultCard` populates all properties | `MainViewModelTests.cs` |
| `ScanActions` count matches handler's `SuggestedActions` | `MainViewModelTests.cs` |
| Tapping action button sets `ShowScanResult` to false | `MainViewModelTests.cs` |
| Tapping action sends prompt to orchestrator | `MainViewModelTests.cs` |
| Auto-dismiss after 30 seconds | `MainViewModelTests.cs` |
| Scan result adds transcript entry with Scan role | `MainViewModelTests.cs` |
| Transcript entry text contains icon + display name + summary | `MainViewModelTests.cs` |
| Transcript entry has "Show actions" button | `MainViewModelTests.cs` |
| "Show actions" button reopens overlay with same data | `MainViewModelTests.cs` |
| Reopen after dismiss repopulates `ScanActions` | `MainViewModelTests.cs` |
| `UrlContentHandler.Summarize` returns URL | `QrContentHandlerTests.cs` |
| `WifiContentHandler.Summarize` returns "SSID (Security)" | `QrContentHandlerTests.cs` |
| Overlay visible on both tabs (UI test) | `ScanResultOverlayTests.cs` |
| Button tap fires command (UI test) | `ScanResultOverlayTests.cs` |
| Transcript action button visible (UI test) | `ScanResultOverlayTests.cs` |

---

## File Summary

```
Pages/
    MainPage.xaml                   — add Actions rendering to transcript item template
    Main/Views/
        ScanResultOverlay.xaml      — overlay card view
        ScanResultOverlay.xaml.cs   — code-behind (empty, bindings only)

ViewModels/
    MainViewModel.cs                — scan result properties, ShowScanResultCard,
                                      ReopenScanResultCard, transcript entry creation

Models/
    TranscriptEntry.cs              — add "Scan" role color
    ScanResultEventArgs.cs          — event args record (handler + parsed + raw)

Orchestration/
    AgentOrchestrator.cs            — add ScanResultReady event

Services/QrCode/
    IQrContentHandler.cs            — add Icon, DisplayName, Summarize members
```

---

## Exit Criteria

1. Scan result overlay appears with content summary + action buttons after successful scan
2. Tapping a button dismisses overlay and triggers the action via AI
3. AI announces scan result and offers same actions by voice
4. User can interact via either path — tap or voice — with the same outcome
5. Overlay auto-dismisses after 30 seconds of inactivity
6. Second scan replaces current overlay content
7. Scan result appears as a transcript entry with icon, type, and summary
8. Transcript entry has a "Show actions" button that reopens the overlay
9. Transcript `ContentAction` buttons render inline for all entry types (reusable)
10. Overlay is accessible (AutomationIds, semantic properties)
