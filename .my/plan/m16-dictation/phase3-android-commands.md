# M16 Phase 3 — Android & Command Mode

Port text injection to Android. Add command mode for editing previously dictated
text. Add multi-language detection.

**Depends on:** Phase 1 (dictation pipeline), Phase 2 (cleanup service).

---

## Wave 1: Android Text Injection — ClipboardManager

Start with the simplest Android approach: copy text to clipboard and show a
notification/toast prompting the user to paste.

### AndroidClipboardTextInjectionProvider

```csharp
// Platforms/Android/AndroidClipboardTextInjectionProvider.cs
public class AndroidClipboardTextInjectionProvider : ITextInjectionProvider
{
    public string ProviderId => "android-clipboard";
    public string DisplayName => "Clipboard (Paste)";
    public bool IsAvailable => true;

    public async Task InjectTextAsync(string text, CancellationToken ct = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var clipboard = (ClipboardManager)Platform.CurrentActivity!
                .GetSystemService(Context.ClipboardService)!;

            var clip = ClipData.NewPlainText("BodyCam Dictation", text);
            clipboard.PrimaryClip = clip;
        });

        // Show toast instructing user to paste
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Toast.MakeText(Platform.CurrentActivity,
                "Text copied — paste in your app",
                ToastLength.Short)!.Show();
        });
    }

    public Task ReplaceLastAsync(int charCount, string newText, CancellationToken ct = default)
    {
        // Cannot do programmatic replacement on Android without accessibility
        // Fall back to just putting new text on clipboard
        return InjectTextAsync(newText, ct);
    }
}
```

### DI Registration (Android)
```csharp
#elif ANDROID
builder.Services.AddSingleton<ITextInjectionProvider, AndroidClipboardTextInjectionProvider>();
#endif
```

### Tests
```csharp
public class AndroidClipboardTextInjectionProviderTests
{
    [Fact]
    public void ProviderId_IsAndroidClipboard()
    {
        var provider = new AndroidClipboardTextInjectionProvider();
        provider.ProviderId.Should().Be("android-clipboard");
    }

    [Fact]
    public void IsAvailable_IsTrue()
    {
        var provider = new AndroidClipboardTextInjectionProvider();
        provider.IsAvailable.Should().BeTrue();
    }
}
```

### Verify
- [ ] Provider compiles on Android target
- [ ] Clipboard copy works
- [ ] Toast notification shows
- [ ] DI registration correct for Android

---

## Wave 2: Android AccessibilityService Text Injection

The premium approach: a background AccessibilityService that can type into any
focused text field without manual paste.

### BodyCamAccessibilityService

```java
// Platforms/Android/BodyCamAccessibilityService.cs (or native Java/Kotlin)

// Android AccessibilityService that:
// 1. Monitors focused input fields
// 2. Receives text from BodyCam via a bound service / broadcast
// 3. Injects text using AccessibilityNodeInfo.ACTION_SET_TEXT
```

```csharp
// Platforms/Android/AccessibilityTextInjectionProvider.cs
public class AccessibilityTextInjectionProvider : ITextInjectionProvider
{
    public string ProviderId => "android-accessibility";
    public string DisplayName => "Direct Input (Accessibility)";

    public bool IsAvailable
    {
        get
        {
            // Check if BodyCamAccessibilityService is enabled
            var context = Platform.CurrentActivity!;
            var accessibilityManager = (AccessibilityManager)context
                .GetSystemService(Context.AccessibilityService)!;
            return accessibilityManager.IsEnabled
                && IsBodyCamServiceEnabled(context);
        }
    }

    public async Task InjectTextAsync(string text, CancellationToken ct = default)
    {
        // Send text to AccessibilityService via local broadcast
        var intent = new Intent("com.bodycam.INJECT_TEXT");
        intent.PutExtra("text", text);
        Platform.CurrentActivity!.SendBroadcast(intent);

        // The AccessibilityService receives this and calls:
        // focusedNode.PerformAction(
        //     AccessibilityNodeInfo.ACTION_SET_TEXT,
        //     Bundle { "ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE" = existingText + newText })
    }

    public async Task ReplaceLastAsync(int charCount, string newText, CancellationToken ct = default)
    {
        // Get current text from focused node, replace last N chars
        var intent = new Intent("com.bodycam.REPLACE_TEXT");
        intent.PutExtra("charCount", charCount);
        intent.PutExtra("newText", newText);
        Platform.CurrentActivity!.SendBroadcast(intent);
    }
}
```

### AndroidManifest Configuration
```xml
<!-- Platforms/Android/AndroidManifest.xml -->
<service
    android:name=".BodyCamAccessibilityService"
    android:permission="android.permission.BIND_ACCESSIBILITY_SERVICE"
    android:exported="false">
    <intent-filter>
        <action android:name="android.accessibilityservice.AccessibilityService" />
    </intent-filter>
    <meta-data
        android:name="android.accessibilityservice"
        android:resource="@xml/accessibility_service_config" />
</service>
```

```xml
<!-- Platforms/Android/Resources/xml/accessibility_service_config.xml -->
<accessibility-service
    xmlns:android="http://schemas.android.com/apk/res/android"
    android:description="@string/accessibility_service_description"
    android:accessibilityEventTypes="typeViewFocused|typeViewTextChanged"
    android:accessibilityFlags="flagDefault|flagIncludeNotImportantViews"
    android:canRetrieveWindowContent="true"
    android:notificationTimeout="100" />
```

### Permission Flow
The user must explicitly enable the AccessibilityService in Android Settings.
BodyCam should:
1. Check if service is enabled on dictation start
2. If not, show explanation dialog
3. Deep-link to Settings → Accessibility → BodyCam
4. Fall back to clipboard mode if declined

```csharp
// Settings deep-link
var intent = new Intent(Android.Provider.Settings.ActionAccessibilitySettings);
Platform.CurrentActivity!.StartActivity(intent);
```

### Provider Selection
```csharp
// TextInjectionManager selects the best available provider
public class TextInjectionManager : ITextInjectionService
{
    private readonly IEnumerable<ITextInjectionProvider> _providers;

    // Priority: accessibility > clipboard
    private ITextInjectionProvider ActiveProvider =>
        _providers.FirstOrDefault(p => p.ProviderId == "android-accessibility" && p.IsAvailable)
        ?? _providers.First(p => p.ProviderId == "android-clipboard");
}
```

### Verify
- [ ] AccessibilityService declaration in manifest
- [ ] Service config XML correct
- [ ] Provider checks if service is enabled
- [ ] Falls back to clipboard if not enabled
- [ ] Permission dialog explains why it's needed
- [ ] Text injection works in Chrome, Gmail, WhatsApp
- [ ] Tests pass

---

## Wave 3: Command Mode

Allow the user to edit previously dictated text with voice commands:
- "Delete the last sentence"
- "Make this more concise"
- "Turn this into bullet points"
- "Replace [word] with [word]"
- "Undo"

### DictationCommandService

```csharp
// Services/Dictation/IDictationCommandService.cs
public interface IDictationCommandService
{
    /// <summary>
    /// Process a voice command on the dictation history.
    /// Returns the replacement text (or null if command not recognized).
    /// </summary>
    Task<CommandResult?> ProcessCommandAsync(
        string command, DictationHistory history, CancellationToken ct = default);
}

public record CommandResult(
    string ReplacementText,
    int ReplaceCharCount  // 0 = append, >0 = replace last N chars
);

public record DictationHistory(IReadOnlyList<string> Segments);
```

### Implementation

```csharp
// Services/Dictation/DictationCommandService.cs
public class DictationCommandService : IDictationCommandService
{
    private readonly IApiKeyService _apiKeyService;

    private const string CommandSystemPrompt = """
        You are a text editing assistant. The user has dictated some text and now
        wants to edit it with a voice command.

        DICTATED TEXT:
        {text}

        USER COMMAND:
        {command}

        Apply the command and return ONLY the modified text. If the command
        doesn't make sense, return the original text unchanged.
        """;

    public async Task<CommandResult?> ProcessCommandAsync(
        string command, DictationHistory history, CancellationToken ct = default)
    {
        // Quick local commands (no LLM needed)
        if (IsDeleteCommand(command))
            return HandleDelete(history);

        if (IsUndoCommand(command))
            return HandleUndo(history);

        // LLM-powered commands
        var fullText = string.Join(" ", history.Segments);
        var prompt = CommandSystemPrompt
            .Replace("{text}", fullText)
            .Replace("{command}", command);

        var result = await CallLlmAsync(prompt, ct);
        return new CommandResult(result, fullText.Length);
    }

    private static bool IsDeleteCommand(string cmd) =>
        cmd.Contains("delete", StringComparison.OrdinalIgnoreCase)
        && cmd.Contains("last", StringComparison.OrdinalIgnoreCase);

    private static bool IsUndoCommand(string cmd) =>
        cmd.Equals("undo", StringComparison.OrdinalIgnoreCase);
}
```

### Activating Command Mode
The user says "command mode" or "edit that" during dictation. The orchestrator:
1. Pauses dictation
2. Switches DictationAgent to command processing
3. Next utterance is treated as a command (not dictated text)
4. After command executes, returns to dictation mode

### Tests
```csharp
public class DictationCommandServiceTests
{
    [Fact]
    public async Task ProcessCommandAsync_DeleteLast_RemovesLastSegment()
    {
        var svc = CreateService();
        var history = new DictationHistory(["Hello.", "World."]);

        var result = await svc.ProcessCommandAsync("delete the last sentence", history);

        result.Should().NotBeNull();
        result!.ReplacementText.Should().Be("Hello.");
    }

    [Fact]
    public async Task ProcessCommandAsync_Undo_RemovesLastSegment()
    {
        var svc = CreateService();
        var history = new DictationHistory(["First.", "Second.", "Third."]);

        var result = await svc.ProcessCommandAsync("undo", history);

        result.Should().NotBeNull();
        result!.ReplacementText.Should().Be("First. Second.");
    }
}
```

### Verify
- [ ] "Delete last sentence" works locally (no LLM)
- [ ] "Undo" works locally
- [ ] LLM commands ("make concise", "bullet points") work
- [ ] Command mode activates/deactivates cleanly
- [ ] Replacement text is injected correctly
- [ ] Tests pass

---

## Wave 4: Multi-Language Detection

The Realtime API's transcription model supports multiple languages. Add
automatic language detection so dictation works without manual language
selection.

### Language Configuration
```csharp
// In session configuration for Realtime API
public class DictationSessionConfig
{
    /// <summary>
    /// Language for transcription. Null = auto-detect.
    /// ISO 639-1 codes: "en", "nl", "de", "fr", "es", "ja", etc.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Language for cleanup LLM. Follows transcription language
    /// unless overridden.
    /// </summary>
    public string? CleanupLanguage { get; set; }
}
```

### Cleanup Prompt Adaptation
```csharp
// DictationCleanupService — language-aware prompts
private string GetSystemPrompt(DictationMode mode, string? language)
{
    var basePrompt = mode == DictationMode.Clean ? CleanSystemPrompt : RewriteSystemPrompt;

    if (language != null && language != "en")
        basePrompt += $"\n\nThe text is in {GetLanguageName(language)}. " +
                      "Preserve the language — do not translate to English.";

    return basePrompt;
}
```

### Verify
- [ ] Auto language detection works
- [ ] Cleanup preserves non-English languages
- [ ] Personal dictionary works across languages
- [ ] Tests pass

---

## Wave 5: Build & Full Integration Test

### Build Verification
```powershell
# Windows
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None

# Android
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android

# Tests
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj
```

### Full Flow Test (Windows)
1. Start BodyCam → "Start dictation in clean mode"
2. Open Notepad → speak with filler words
3. Verify clean text appears (no "um", "uh")
4. Say "Switch to rewrite mode"
5. Speak rambling paragraph → verify polished output
6. Say "Command mode" → "Make this more concise"
7. Verify text is replaced with concise version
8. Say "Stop dictation"

### Full Flow Test (Android)
1. Start BodyCam → "Start dictation"
2. Verify clipboard notification appears
3. Open WhatsApp → paste → verify text
4. Enable AccessibilityService → retry
5. Verify text appears automatically (no paste)

### Verify
- [ ] 0 build errors (both platforms)
- [ ] All unit tests pass
- [ ] Windows clipboard injection works
- [ ] Android clipboard works
- [ ] Android accessibility works (if enabled)
- [ ] Clean mode removes fillers
- [ ] Rewrite mode polishes text
- [ ] Command mode edits text
- [ ] Multi-language works
- [ ] Personal dictionary persists across sessions
- [ ] No regressions in conversation mode
