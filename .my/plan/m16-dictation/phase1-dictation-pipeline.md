# M16 Phase 1 — Core Dictation Pipeline

Build the minimal dictation system: capture STT output from the Realtime API,
inject it as text into the focused app on Windows. Raw mode only (no LLM cleanup).

**Depends on:** M12 Phase 1 (audio input), M2 (Realtime API).

---

## Wave 1: Text Injection Abstraction

### ITextInjectionProvider

New interface following the M12/M13 provider pattern:

```csharp
// Services/TextInjection/ITextInjectionProvider.cs
public interface ITextInjectionProvider
{
    string ProviderId { get; }      // "clipboard", "uiautomation", "accessibility"
    string DisplayName { get; }
    bool IsAvailable { get; }

    /// <summary>
    /// Inject text at the current cursor position in the focused app.
    /// </summary>
    Task InjectTextAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Replace the last N characters with new text (for corrections).
    /// </summary>
    Task ReplaceLastAsync(int charCount, string newText, CancellationToken ct = default);
}
```

### ITextInjectionService

High-level service (backward compat wrapper, like `IAudioInputService`):

```csharp
// Services/TextInjection/ITextInjectionService.cs
public interface ITextInjectionService
{
    bool IsAvailable { get; }

    Task InjectTextAsync(string text, CancellationToken ct = default);
    Task ReplaceLastAsync(int charCount, string newText, CancellationToken ct = default);
}
```

### TextInjectionManager

Manager implementing `ITextInjectionService`, delegates to the active provider:

```csharp
// Services/TextInjection/TextInjectionManager.cs
public class TextInjectionManager : ITextInjectionService
{
    private readonly ITextInjectionProvider _provider;

    public TextInjectionManager(ITextInjectionProvider provider)
    {
        _provider = provider;
    }

    public bool IsAvailable => _provider.IsAvailable;

    public Task InjectTextAsync(string text, CancellationToken ct = default)
        => _provider.InjectTextAsync(text, ct);

    public Task ReplaceLastAsync(int charCount, string newText, CancellationToken ct = default)
        => _provider.ReplaceLastAsync(charCount, newText, ct);
}
```

### Files
- `Services/TextInjection/ITextInjectionProvider.cs`
- `Services/TextInjection/ITextInjectionService.cs`
- `Services/TextInjection/TextInjectionManager.cs`

### Tests
```csharp
public class TextInjectionManagerTests
{
    [Fact]
    public async Task InjectTextAsync_DelegatesToProvider()
    {
        var mock = Substitute.For<ITextInjectionProvider>();
        var manager = new TextInjectionManager(mock);

        await manager.InjectTextAsync("hello");

        await mock.Received(1).InjectTextAsync("hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IsAvailable_ReflectsProvider()
    {
        var mock = Substitute.For<ITextInjectionProvider>();
        mock.IsAvailable.Returns(true);
        var manager = new TextInjectionManager(mock);

        manager.IsAvailable.Should().BeTrue();
    }
}
```

### Verify
- [ ] `ITextInjectionProvider` interface compiles
- [ ] `ITextInjectionService` interface compiles
- [ ] `TextInjectionManager` delegates to provider
- [ ] Unit tests pass

---

## Wave 2: Windows Clipboard Text Injection

### ClipboardTextInjectionProvider

Windows-specific implementation using clipboard + Ctrl+V:

```csharp
// Platforms/Windows/ClipboardTextInjectionProvider.cs
public class ClipboardTextInjectionProvider : ITextInjectionProvider
{
    public string ProviderId => "clipboard";
    public string DisplayName => "Clipboard (Ctrl+V)";
    public bool IsAvailable => true; // Always available on Windows

    public async Task InjectTextAsync(string text, CancellationToken ct = default)
    {
        // Save current clipboard content
        var previousContent = await GetClipboardTextAsync();

        try
        {
            // Set clipboard to dictated text
            await SetClipboardTextAsync(text);

            // Simulate Ctrl+V
            await SimulatePasteAsync();

            // Small delay for paste to complete
            await Task.Delay(50, ct);
        }
        finally
        {
            // Restore previous clipboard content
            if (previousContent != null)
                await SetClipboardTextAsync(previousContent);
        }
    }

    public async Task ReplaceLastAsync(int charCount, string newText, CancellationToken ct = default)
    {
        // Select last N chars with Shift+Left, then paste replacement
        await SimulateSelectBackAsync(charCount);
        await InjectTextAsync(newText, ct);
    }

    private static async Task SetClipboardTextAsync(string text)
    {
        // Must run on UI thread for clipboard access
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Clipboard.Default.SetTextAsync(text);
        });
    }

    private static async Task<string?> GetClipboardTextAsync()
    {
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            return await Clipboard.Default.GetTextAsync();
        });
    }

    private static Task SimulatePasteAsync()
    {
        // Windows.Forms.SendKeys or InputSimulator
        // Send Ctrl+V keystrokes
        // Implementation uses Windows Input API (SendInput)
        return Task.CompletedTask; // Platform implementation
    }

    private static Task SimulateSelectBackAsync(int charCount)
    {
        // Send Shift+Left N times to select backward
        return Task.CompletedTask; // Platform implementation
    }
}
```

**Key implementation detail:** Use `Windows.Win32.PInvoke` (CsWin32) or
`System.Windows.Forms.SendKeys` for keystroke simulation. Prefer `SendInput`
via P/Invoke for reliability:

```csharp
// Keystroke simulation via Win32 SendInput
[DllImport("user32.dll", SetLastError = true)]
private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

private static void SendCtrlV()
{
    var inputs = new INPUT[]
    {
        // Ctrl down
        new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_CONTROL } },
        // V down
        new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_V } },
        // V up
        new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_V, dwFlags = KEYEVENTF_KEYUP } },
        // Ctrl up
        new() { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } },
    };
    SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
}
```

### Tests
```csharp
public class ClipboardTextInjectionProviderTests
{
    [Fact]
    public void ProviderId_IsClipboard()
    {
        var provider = new ClipboardTextInjectionProvider();
        provider.ProviderId.Should().Be("clipboard");
    }

    [Fact]
    public void IsAvailable_IsTrue()
    {
        var provider = new ClipboardTextInjectionProvider();
        provider.IsAvailable.Should().BeTrue();
    }
}
```

Integration tests (require Windows desktop):
```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task InjectTextAsync_PastesIntoNotepad()
{
    // Launch notepad, inject text, read back via UIAutomation
    // Verify text appears in the editor
}
```

### Verify
- [ ] ClipboardTextInjectionProvider compiles
- [ ] Clipboard save/restore works
- [ ] Ctrl+V simulation works in Notepad
- [ ] ReplaceLastAsync selects backward and replaces

---

## Wave 3: Dictation Service & Agent

### IDictationService

Top-level dictation control:

```csharp
// Services/Dictation/IDictationService.cs
public interface IDictationService
{
    DictationState State { get; }
    DictationMode Mode { get; set; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task PauseAsync(CancellationToken ct = default);
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>Fires when cleaned text is ready for injection.</summary>
    event EventHandler<DictationTextEventArgs>? TextReady;

    /// <summary>Fires when dictation state changes.</summary>
    event EventHandler<DictationState>? StateChanged;
}

public enum DictationState
{
    Idle,
    Listening,
    Paused,
    Processing
}

public enum DictationMode
{
    Raw,        // Direct STT pass-through
    Clean,      // Filler removal + punctuation (Phase 2)
    Rewrite     // Full rewrite (Phase 2)
}

public record DictationTextEventArgs(string Text, bool IsFinal);
```

### DictationAgent

New agent that subscribes to `InputTranscriptCompleted` and routes text to
the injection service:

```csharp
// Agents/DictationAgent.cs
public class DictationAgent
{
    private readonly IDictationService _dictation;
    private readonly ITextInjectionService _injection;
    private readonly IRealtimeClient _realtime;

    private bool _isActive;

    public DictationAgent(
        IDictationService dictation,
        ITextInjectionService injection,
        IRealtimeClient realtime)
    {
        _dictation = dictation;
        _injection = injection;
        _realtime = realtime;

        _dictation.TextReady += OnTextReady;
    }

    public void Activate()
    {
        _isActive = true;
        _realtime.InputTranscriptCompleted += OnTranscriptCompleted;
    }

    public void Deactivate()
    {
        _isActive = false;
        _realtime.InputTranscriptCompleted -= OnTranscriptCompleted;
    }

    private void OnTranscriptCompleted(object? sender, TranscriptEventArgs e)
    {
        if (!_isActive) return;

        // In Raw mode, the transcript IS the text to inject.
        // In Clean/Rewrite modes, the DictationService will post-process first.
        // For Phase 1, DictationService just fires TextReady immediately.
        _dictation.ProcessTranscript(e.Text);
    }

    private async void OnTextReady(object? sender, DictationTextEventArgs e)
    {
        if (!e.IsFinal) return;
        await _injection.InjectTextAsync(e.Text + " ");
    }
}
```

### DictationService (Phase 1 — Raw mode only)

```csharp
// Services/Dictation/DictationService.cs
public class DictationService : IDictationService
{
    private DictationState _state = DictationState.Idle;
    private DictationMode _mode = DictationMode.Raw;

    public DictationState State => _state;
    public DictationMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    public event EventHandler<DictationTextEventArgs>? TextReady;
    public event EventHandler<DictationState>? StateChanged;

    public Task StartAsync(CancellationToken ct = default)
    {
        SetState(DictationState.Listening);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        SetState(DictationState.Idle);
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken ct = default)
    {
        if (_state == DictationState.Listening)
            SetState(DictationState.Paused);
        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken ct = default)
    {
        if (_state == DictationState.Paused)
            SetState(DictationState.Listening);
        return Task.CompletedTask;
    }

    public void ProcessTranscript(string text)
    {
        if (_state != DictationState.Listening) return;

        // Phase 1: Raw mode — pass through directly
        TextReady?.Invoke(this, new DictationTextEventArgs(text, IsFinal: true));
    }

    private void SetState(DictationState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }
}
```

### Tests
```csharp
public class DictationServiceTests
{
    [Fact]
    public async Task StartAsync_SetsStateToListening()
    {
        var svc = new DictationService();
        await svc.StartAsync();
        svc.State.Should().Be(DictationState.Listening);
    }

    [Fact]
    public async Task StopAsync_SetsStateToIdle()
    {
        var svc = new DictationService();
        await svc.StartAsync();
        await svc.StopAsync();
        svc.State.Should().Be(DictationState.Idle);
    }

    [Fact]
    public async Task ProcessTranscript_WhenListening_FiresTextReady()
    {
        var svc = new DictationService();
        await svc.StartAsync();

        DictationTextEventArgs? received = null;
        svc.TextReady += (_, e) => received = e;

        svc.ProcessTranscript("hello world");

        received.Should().NotBeNull();
        received!.Text.Should().Be("hello world");
        received.IsFinal.Should().BeTrue();
    }

    [Fact]
    public void ProcessTranscript_WhenIdle_DoesNotFire()
    {
        var svc = new DictationService();
        DictationTextEventArgs? received = null;
        svc.TextReady += (_, e) => received = e;

        svc.ProcessTranscript("hello");

        received.Should().BeNull();
    }

    [Fact]
    public async Task PauseAsync_SuppressesTranscripts()
    {
        var svc = new DictationService();
        await svc.StartAsync();
        await svc.PauseAsync();

        DictationTextEventArgs? received = null;
        svc.TextReady += (_, e) => received = e;
        svc.ProcessTranscript("hello");

        received.Should().BeNull();
        svc.State.Should().Be(DictationState.Paused);
    }
}

public class DictationAgentTests
{
    [Fact]
    public void Activate_SubscribesToTranscripts()
    {
        var dictation = Substitute.For<IDictationService>();
        var injection = Substitute.For<ITextInjectionService>();
        var realtime = Substitute.For<IRealtimeClient>();

        var agent = new DictationAgent(dictation, injection, realtime);
        agent.Activate();

        // Verify subscribed (event handler added)
        // NSubstitute can verify event subscription
    }
}
```

### Verify
- [ ] `IDictationService` interface compiles
- [ ] `DictationService` state machine works (Idle ↔ Listening ↔ Paused)
- [ ] `ProcessTranscript` fires `TextReady` only when Listening
- [ ] `DictationAgent` wires STT → DictationService → TextInjection
- [ ] All unit tests pass

---

## Wave 4: Dictation Tool + Orchestrator Integration

### StartDictationTool

Tool that the Realtime API can invoke when the user says "start dictation":

```csharp
// Tools/StartDictationTool.cs
public class StartDictationTool : ToolBase<StartDictationTool.Args>
{
    public record Args(
        [property: Description("Dictation mode: raw, clean, or rewrite")]
        string? Mode = null);

    private readonly IDictationService _dictation;

    public StartDictationTool(IDictationService dictation) : base(
        "start_dictation",
        "Start voice dictation mode. Text will be typed into the focused app.")
    {
        _dictation = dictation;
    }

    public override WakeWordBinding? WakeWord => new(
        KeywordPath: "bodycam-dictate",
        Mode: WakeWordMode.QuickAction);

    protected override async Task<string> ExecuteCoreAsync(Args args, CancellationToken ct)
    {
        if (_dictation.State == DictationState.Listening)
            return "Dictation is already active.";

        if (args.Mode != null && Enum.TryParse<DictationMode>(args.Mode, true, out var mode))
            _dictation.Mode = mode;

        await _dictation.StartAsync(ct);
        return $"Dictation started in {_dictation.Mode} mode. Speak naturally — text will appear in the focused app. Say 'stop dictation' to finish.";
    }
}
```

### StopDictationTool

```csharp
// Tools/StopDictationTool.cs
public class StopDictationTool : ToolBase<StopDictationTool.Args>
{
    public record Args;

    private readonly IDictationService _dictation;

    public StopDictationTool(IDictationService dictation) : base(
        "stop_dictation",
        "Stop voice dictation mode.")
    {
        _dictation = dictation;
    }

    protected override async Task<string> ExecuteCoreAsync(Args args, CancellationToken ct)
    {
        if (_dictation.State == DictationState.Idle)
            return "Dictation is not active.";

        await _dictation.StopAsync(ct);
        return "Dictation stopped.";
    }
}
```

### Orchestrator Integration

In `AgentOrchestrator`, add dictation mode awareness:

```csharp
// In AgentOrchestrator — extend existing StartAsync / StopAsync

// When dictation starts:
// 1. Connect Realtime API (if not connected)
// 2. Activate DictationAgent
// 3. Configure session for transcription-only (no AI response audio)

// When dictation stops:
// 1. Deactivate DictationAgent
// 2. If no conversation is active, disconnect Realtime API

// The key insight: during dictation, InputTranscriptCompleted goes to
// DictationAgent instead of (or in addition to) ConversationAgent.
// The orchestrator routes based on active mode.
```

### DI Registration

```csharp
// MauiProgram.cs additions
builder.Services.AddSingleton<IDictationService, DictationService>();
builder.Services.AddSingleton<DictationAgent>();

#if WINDOWS
builder.Services.AddSingleton<ITextInjectionProvider, ClipboardTextInjectionProvider>();
#endif
builder.Services.AddSingleton<ITextInjectionService, TextInjectionManager>();

// Tools
builder.Services.AddSingleton<ITool, StartDictationTool>();
builder.Services.AddSingleton<ITool, StopDictationTool>();
```

### Tests
```csharp
public class StartDictationToolTests
{
    [Fact]
    public async Task ExecuteAsync_StartsDictation()
    {
        var dictation = Substitute.For<IDictationService>();
        dictation.State.Returns(DictationState.Idle);
        var tool = new StartDictationTool(dictation);

        var result = await tool.ExecuteAsync("{}", CancellationToken.None);

        await dictation.Received(1).StartAsync(Arg.Any<CancellationToken>());
        result.Should().Contain("started");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyActive_ReturnsMessage()
    {
        var dictation = Substitute.For<IDictationService>();
        dictation.State.Returns(DictationState.Listening);
        var tool = new StartDictationTool(dictation);

        var result = await tool.ExecuteAsync("{}", CancellationToken.None);

        await dictation.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        result.Should().Contain("already active");
    }
}

public class StopDictationToolTests
{
    [Fact]
    public async Task ExecuteAsync_StopsDictation()
    {
        var dictation = Substitute.For<IDictationService>();
        dictation.State.Returns(DictationState.Listening);
        var tool = new StopDictationTool(dictation);

        var result = await tool.ExecuteAsync("{}", CancellationToken.None);

        await dictation.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }
}
```

### Verify
- [ ] `StartDictationTool` registered and callable
- [ ] `StopDictationTool` registered and callable
- [ ] DI registration compiles
- [ ] Orchestrator routes transcripts to DictationAgent when active
- [ ] Dictation mode coexists with conversation mode
- [ ] All tests pass
- [ ] Build succeeds

---

## Wave 5: Build & Integration Test

### Build Verification
```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj
```

### Manual Integration Test
1. Start BodyCam
2. Say "start dictation"
3. Open Notepad
4. Speak a sentence
5. Verify text appears in Notepad
6. Say "stop dictation"
7. Verify dictation stops

### Verify
- [ ] 0 build errors
- [ ] All unit tests pass (existing + new)
- [ ] Manual test: dictation → Notepad works
- [ ] No regressions in existing conversation mode
