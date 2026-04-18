# Phase 1 — Semantic Labels & Screen Reader Support

Add `SemanticProperties` to every interactive and informational control so Narrator
(Windows) and TalkBack (Android) can announce purpose, state, and content.

---

## Why

The app is currently **invisible** to screen reader users. Emoji-only buttons
(`😴` `👂` `💬` `🐛`) produce no speech output. Pickers, switches, and transcript
items have no programmatic labels. A blind user cannot operate any part of the UI.

---

## Files Changed

| File | Change |
|------|--------|
| `MainPage.xaml` | Add `SemanticProperties.Description` and `Hint` to all controls |
| `SettingsPage.xaml` | Add `SemanticProperties.Description`, `Hint`, and `HeadingLevel` |
| `MainViewModel.cs` | Add `StateDescription` computed property |
| `TranscriptEntry.cs` | Add `AccessibleText` computed property |
| Unit tests | `StateDescription` and `AccessibleText` property tests |

---

## MainPage Changes

### Status Bar (Row 0)

**Status dot** — currently has no semantic meaning:

```xml
<!-- Before -->
<Ellipse AutomationId="StatusDot"
         WidthRequest="12" HeightRequest="12"
         Fill="{Binding StateColor}"
         VerticalOptions="Center" />

<!-- After -->
<Ellipse AutomationId="StatusDot"
         WidthRequest="12" HeightRequest="12"
         Fill="{Binding StateColor}"
         VerticalOptions="Center"
         SemanticProperties.Description="{Binding StateDescription}" />
```

**State buttons** — emoji text is meaningless to screen readers:

```xml
<!-- Before -->
<Button AutomationId="SleepButton" Text="😴" ... />

<!-- After -->
<Button AutomationId="SleepButton" Text="😴"
        SemanticProperties.Description="Sleep mode"
        SemanticProperties.Hint="Puts the assistant to sleep" ... />
```

Full state button table:

| AutomationId | `Description` | `Hint` |
|--------------|---------------|--------|
| `SleepButton` | `"Sleep mode"` | `"Puts the assistant to sleep"` |
| `ListenButton` | `"Listen mode"` | `"Activates wake word listening"` |
| `ActiveButton` | `"Active session"` | `"Starts an active conversation"` |
| `DebugToggleButton` | `"Toggle debug console"` | `"Shows or hides the debug log"` |
| `ClearButton` | `"Clear transcript"` | `"Removes all transcript entries"` |

### Tab Buttons (Row 2)

```xml
<Button AutomationId="TranscriptTabButton"
        Text="📝 Transcript"
        SemanticProperties.Description="Transcript tab"
        SemanticProperties.Hint="Shows the conversation transcript" ... />

<Button AutomationId="CameraTabButton"
        Text="📷 Camera"
        SemanticProperties.Description="Camera tab"
        SemanticProperties.Hint="Shows the live camera feed" ... />
```

### Quick Action Buttons (Row 3)

| AutomationId | `Description` | `Hint` |
|--------------|---------------|--------|
| `LookButton` | `"Look"` | `"Describes what the camera sees"` |
| `ReadButton` | `"Read"` | `"Reads text visible to the camera"` |
| `FindButton` | `"Find"` | `"Finds specific objects in the camera view"` |
| `AskButton` | `"Ask"` | `"Asks the AI a question"` |
| `PhotoButton` | `"Photo"` | `"Takes a photo with the camera"` |

### Snapshot Overlay

```xml
<Image AutomationId="SnapshotImage"
       Source="{Binding SnapshotImage}"
       SemanticProperties.Description="{Binding SnapshotCaption}" ... />

<Button AutomationId="DismissSnapshotButton"
        Text="Dismiss"
        SemanticProperties.Hint="Closes the snapshot overlay" ... />
```

### Transcript Items

```xml
<VerticalStackLayout Padding="4,2" Opacity="0" TranslationY="20"
                     Loaded="EntryItem_Loaded"
                     SemanticProperties.Description="{Binding AccessibleText}">

    <!-- Thinking dots -->
    <HorizontalStackLayout IsVisible="{Binding IsThinking}" ...
                           SemanticProperties.Description="Thinking...">
```

### Camera & Debug

```xml
<Label AutomationId="CameraPlaceholder"
       Text="Camera initializing..."
       SemanticProperties.Description="Camera is initializing" ... />

<Label AutomationId="DebugLabel"
       SemanticProperties.Description="Debug log output" ... />
```

---

## SettingsPage Changes

### Section Headings

Every section header label needs `HeadingLevel` so screen readers announce section
structure and users can jump between sections:

```xml
<!-- Before -->
<Label Text="Provider" FontSize="18" FontAttributes="Bold" Margin="0,8,0,0" />

<!-- After -->
<Label Text="Provider" FontSize="18" FontAttributes="Bold" Margin="0,8,0,0"
       SemanticProperties.HeadingLevel="Level1" />
```

Apply to all section headers:
- `"Provider"`
- `"Models"`
- `"Azure Deployments"`
- `"Voice Settings"`
- `"System Instructions"`
- `"API Key"`
- `"Camera"`
- `"Audio Input"`
- `"Audio Output"`
- `"Debug"`
- `"Tool Settings"`

### Pickers

Each picker is preceded by a label (e.g. `"Voice Model"`) but there's no
programmatic association. Add `SemanticProperties.Description` matching the
label text:

```xml
<Picker AutomationId="VoiceModelPicker"
        SemanticProperties.Description="Voice model"
        ItemsSource="{Binding RealtimeModelOptions}" ... />
```

All pickers:

| AutomationId | `Description` |
|--------------|---------------|
| `VoiceModelPicker` | `"Voice model"` |
| `ChatModelPicker` | `"Chat model"` |
| `VisionModelPicker` | `"Vision model"` |
| `TranscriptionModelPicker` | `"Transcription model"` |
| `VoicePicker` | `"Voice"` |
| `TurnDetectionPicker` | `"Turn detection"` |
| `NoiseReductionPicker` | `"Noise reduction"` |
| `CameraSourcePicker` | `"Camera source"` |
| `AudioInputPicker` | `"Microphone source"` |
| `AudioOutputPicker` | `"Speaker"` |

### Entries

```xml
<Entry AutomationId="AzureEndpointEntry"
       SemanticProperties.Description="Azure endpoint URL" ... />

<Entry AutomationId="ApiKeyDisplay"
       SemanticProperties.Description="API key, masked" ... />
```

All entries:

| AutomationId | `Description` |
|--------------|---------------|
| `AzureEndpointEntry` | `"Azure endpoint URL"` |
| `AzureApiVersionEntry` | `"Azure API version"` |
| `AzureRealtimeDeploymentEntry` | `"Azure realtime deployment name"` |
| `AzureChatDeploymentEntry` | `"Azure chat deployment name"` |
| `AzureVisionDeploymentEntry` | `"Azure vision deployment name"` |
| `ApiKeyDisplay` | `"API key, masked"` |
| `SystemInstructionsEditor` | `"System instructions for the AI assistant"` |

### Switches

```xml
<Switch AutomationId="DebugModeSwitch"
        SemanticProperties.Description="Debug mode"
        IsToggled="{Binding DebugMode}" ... />
```

| AutomationId | `Description` |
|--------------|---------------|
| `DebugModeSwitch` | `"Debug mode"` |
| `ShowTokenCountsSwitch` | `"Show token counts"` |
| `ShowCostEstimateSwitch` | `"Show cost estimate"` |

### Buttons

| AutomationId | `Description` | `Hint` |
|--------------|---------------|--------|
| `ToggleKeyVisibilityButton` | `"Toggle key visibility"` | `"Shows or hides the API key"` |
| `ChangeApiKeyButton` | `"Change API key"` | — |
| `ClearApiKeyButton` | `"Clear API key"` | `"Removes the stored API key"` |
| `TestConnectionButton` | `"Test connection"` | `"Tests the API connection with current settings"` |

### Radio Buttons

```xml
<RadioButton AutomationId="ProviderOpenAiRadio"
             Content="OpenAI"
             SemanticProperties.Description="Use OpenAI provider" ... />
<RadioButton AutomationId="ProviderAzureRadio"
             Content="Azure"
             SemanticProperties.Description="Use Azure OpenAI provider" ... />
```

### Tool Settings Items (DataTemplate)

```xml
<Switch IsToggled="{Binding BoolValue}"
        IsVisible="{Binding IsBoolean}"
        SemanticProperties.Description="{Binding Label}"
        Grid.Column="1" />
<Entry Text="{Binding StringValue}"
       IsVisible="{Binding IsInteger}"
       SemanticProperties.Description="{Binding Label}"
       Keyboard="Numeric" WidthRequest="80" Grid.Column="1" />
<Entry Text="{Binding StringValue}"
       IsVisible="{Binding IsText}"
       SemanticProperties.Description="{Binding Label}"
       WidthRequest="200" Grid.Column="1" />
```

---

## ViewModel / Model Changes

### MainViewModel.StateDescription

Add a computed property that returns a human-readable state name:

```csharp
public string StateDescription => CurrentLayer switch
{
    ListeningLayer.Sleep => "Sleep mode",
    ListeningLayer.WakeWord => "Listening for wake word",
    ListeningLayer.ActiveSession => "Active session",
    _ => "Unknown state"
};
```

Notify when `CurrentLayer` changes — add to the existing setter:

```csharp
OnPropertyChanged(nameof(StateDescription));
```

### TranscriptEntry.AccessibleText

Add a computed property that combines role and text for screen readers:

```csharp
public string AccessibleText => IsThinking
    ? $"{Role} is thinking"
    : string.IsNullOrEmpty(Text)
        ? Role
        : $"{Role}: {Text}";
```

Update the `Text` setter to also notify `AccessibleText`:

```csharp
if (SetProperty(ref _text, value))
{
    OnPropertyChanged(nameof(DisplayText));
    OnPropertyChanged(nameof(AccessibleText));
}
```

Update the `IsThinking` setter similarly:

```csharp
set
{
    if (SetProperty(ref _isThinking, value))
        OnPropertyChanged(nameof(AccessibleText));
}
```

---

## Unit Tests

### StateDescription Tests

```csharp
[Fact]
public void StateDescription_Sleep_ReturnsSleepMode()
{
    // Arrange — set CurrentLayer to Sleep
    // Assert — StateDescription should be "Sleep mode"
}

[Fact]
public void StateDescription_WakeWord_ReturnsListening()
{
    // Assert — "Listening for wake word"
}

[Fact]
public void StateDescription_ActiveSession_ReturnsActive()
{
    // Assert — "Active session"
}
```

### AccessibleText Tests

```csharp
[Fact]
public void AccessibleText_WithText_ReturnsRoleColonText()
{
    var entry = new TranscriptEntry { Role = "AI" };
    entry.Text = "Hello";
    entry.AccessibleText.Should().Be("AI: Hello");
}

[Fact]
public void AccessibleText_WhenThinking_ReturnsThinkingMessage()
{
    var entry = new TranscriptEntry { Role = "AI" };
    entry.IsThinking = true;
    entry.AccessibleText.Should().Be("AI is thinking");
}

[Fact]
public void AccessibleText_EmptyText_ReturnsRoleOnly()
{
    var entry = new TranscriptEntry { Role = "You" };
    entry.AccessibleText.Should().Be("You");
}
```

---

## Manual Testing Checklist

### Windows Narrator

1. Launch Narrator (Win+Ctrl+Enter)
2. Tab through MainPage — verify every button announces its description
3. Verify status dot announces current state
4. Navigate to transcript — verify each entry announces role + text
5. Switch to SettingsPage — verify section headings are announced as headings
6. Verify all pickers announce their label
7. Verify all switches announce their label and toggle state

### Android TalkBack

1. Enable TalkBack (Settings → Accessibility → TalkBack)
2. Swipe through MainPage controls — verify announcements
3. Verify transcript entries announce role + text
4. Verify SettingsPage controls announce labels
5. Verify explore-by-touch on quick action buttons

---

## Exit Criteria

- Every interactive control has `SemanticProperties.Description`
- Section headers have `HeadingLevel="Level1"`
- `StateDescription` and `AccessibleText` properties exist with unit tests
- Narrator can navigate the full app and announce every control
- TalkBack can navigate the full app and announce every control
