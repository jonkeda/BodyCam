# M16 Phase 2 — AI Cleanup & Formatting

Add LLM post-processing to dictation. Clean mode removes filler words and adds
punctuation. Rewrite mode produces polished prose. Personal dictionary learns
custom names and jargon.

**Depends on:** Phase 1 (core dictation pipeline).

---

## Wave 1: Sentence Buffer

Before sending text to the LLM for cleanup, we need to buffer individual
transcript fragments into complete sentences. The Realtime API fires
`InputTranscriptCompleted` per utterance (VAD segment), which may be a fragment,
a sentence, or multiple sentences.

### TranscriptBuffer

```csharp
// Services/Dictation/TranscriptBuffer.cs
public class TranscriptBuffer
{
    private readonly StringBuilder _buffer = new();
    private static readonly char[] SentenceEnders = ['.', '!', '?'];

    /// <summary>
    /// Add a transcript fragment. Returns completed sentences (if any)
    /// while keeping the incomplete remainder in the buffer.
    /// </summary>
    public IReadOnlyList<string> Add(string fragment)
    {
        _buffer.Append(fragment);
        _buffer.Append(' ');

        var text = _buffer.ToString();
        var sentences = new List<string>();

        int lastEnd = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (SentenceEnders.Contains(text[i]))
            {
                var sentence = text[lastEnd..(i + 1)].Trim();
                if (sentence.Length > 0)
                    sentences.Add(sentence);
                lastEnd = i + 1;
            }
        }

        // Keep the remainder (incomplete sentence) in buffer
        _buffer.Clear();
        if (lastEnd < text.Length)
            _buffer.Append(text[lastEnd..].TrimStart());

        return sentences;
    }

    /// <summary>Flush remaining text as a final sentence.</summary>
    public string? Flush()
    {
        var remaining = _buffer.ToString().Trim();
        _buffer.Clear();
        return remaining.Length > 0 ? remaining : null;
    }
}
```

### Tests
```csharp
public class TranscriptBufferTests
{
    [Fact]
    public void Add_CompleteSentence_ReturnsSentence()
    {
        var buffer = new TranscriptBuffer();
        var sentences = buffer.Add("Hello world.");
        sentences.Should().ContainSingle().Which.Should().Be("Hello world.");
    }

    [Fact]
    public void Add_Fragment_ReturnsEmpty()
    {
        var buffer = new TranscriptBuffer();
        var sentences = buffer.Add("Hello");
        sentences.Should().BeEmpty();
    }

    [Fact]
    public void Add_FragmentThenSentenceEnd_ReturnsCombined()
    {
        var buffer = new TranscriptBuffer();
        buffer.Add("Hello");
        var sentences = buffer.Add("world.");
        sentences.Should().ContainSingle().Which.Should().Be("Hello world.");
    }

    [Fact]
    public void Flush_ReturnsRemainder()
    {
        var buffer = new TranscriptBuffer();
        buffer.Add("incomplete thought");
        buffer.Flush().Should().Be("incomplete thought");
    }

    [Fact]
    public void Add_MultipleSentences_ReturnsAll()
    {
        var buffer = new TranscriptBuffer();
        var sentences = buffer.Add("First. Second. Third.");
        sentences.Should().HaveCount(3);
    }
}
```

### Verify
- [ ] Buffer accumulates fragments
- [ ] Returns complete sentences on period/!/? 
- [ ] Keeps remainder for next fragment
- [ ] Flush returns incomplete text
- [ ] Tests pass

---

## Wave 2: DictationCleanupService

LLM post-processing service that cleans up raw transcripts.

### IDictationCleanupService

```csharp
// Services/Dictation/IDictationCleanupService.cs
public interface IDictationCleanupService
{
    /// <summary>
    /// Clean up a raw transcript sentence.
    /// </summary>
    Task<string> CleanAsync(string rawText, DictationMode mode, CancellationToken ct = default);
}
```

### DictationCleanupService

Uses GPT-4o-mini via the standard OpenAI chat completions API (not the Realtime
API — that's for audio). Small, fast, cheap text-to-text calls.

```csharp
// Services/Dictation/DictationCleanupService.cs
public class DictationCleanupService : IDictationCleanupService
{
    private readonly IApiKeyService _apiKeyService;
    private readonly IPersonalDictionaryService _dictionary;
    private readonly HttpClient _httpClient;

    private const string CleanSystemPrompt = """
        You are a dictation cleanup assistant. Clean up the following speech transcript:
        - Remove filler words (um, uh, like, you know, I mean, so, basically, actually)
        - Remove false starts and repeated words
        - Add proper punctuation and capitalization
        - Do NOT change the meaning or add new content
        - Do NOT rephrase — keep the speaker's words
        - Return ONLY the cleaned text, no explanation
        """;

    private const string RewriteSystemPrompt = """
        You are a writing assistant. Rewrite the following speech transcript into
        clear, well-structured prose:
        - Remove all filler words and false starts
        - Rephrase for clarity and conciseness
        - Add proper punctuation, capitalization, and paragraph breaks
        - Maintain the original meaning and intent
        - Return ONLY the rewritten text, no explanation
        """;

    public async Task<string> CleanAsync(string rawText, DictationMode mode, CancellationToken ct = default)
    {
        if (mode == DictationMode.Raw)
            return rawText;

        var systemPrompt = mode == DictationMode.Clean
            ? CleanSystemPrompt
            : RewriteSystemPrompt;

        // Append personal dictionary context if available
        var dictContext = _dictionary.GetContext();
        if (!string.IsNullOrEmpty(dictContext))
            systemPrompt += $"\n\nPersonal dictionary (use these spellings):\n{dictContext}";

        var apiKey = await _apiKeyService.GetKeyAsync("OpenAI", ct);

        // Call GPT-4o-mini chat completion
        var request = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = rawText }
            },
            max_tokens = rawText.Length * 2,
            temperature = 0.3
        };

        // ... HTTP call to api.openai.com/v1/chat/completions
        // Return the cleaned text from the response

        return rawText; // Placeholder — actual HTTP implementation
    }
}
```

### Cost Analysis
- GPT-4o-mini: ~$0.15/1M input tokens, ~$0.60/1M output tokens
- Average sentence: ~20 words ≈ 30 tokens
- System prompt: ~100 tokens
- Per sentence cost: ~$0.00002 (negligible)
- 30 minutes of dictation (~300 sentences): ~$0.006

### Tests
```csharp
public class DictationCleanupServiceTests
{
    [Fact]
    public async Task CleanAsync_RawMode_ReturnsUnchanged()
    {
        var svc = CreateService();
        var result = await svc.CleanAsync("um hello like world", DictationMode.Raw);
        result.Should().Be("um hello like world");
    }

    // Integration tests (require API key):
    [Fact]
    [Trait("Category", "RealApi")]
    public async Task CleanAsync_CleanMode_RemovesFillers()
    {
        var svc = CreateServiceWithRealKey();
        var result = await svc.CleanAsync(
            "So um I was like thinking that we should uh probably go to the store",
            DictationMode.Clean);

        result.Should().NotContain("um");
        result.Should().NotContain("uh");
        result.Should().NotContain("like thinking");
        result.Should().Contain("store");
    }
}
```

### Verify
- [ ] Raw mode returns text unchanged
- [ ] Clean mode system prompt is correct
- [ ] Rewrite mode system prompt is correct
- [ ] Personal dictionary context is appended
- [ ] HTTP call structure is correct
- [ ] Tests pass

---

## Wave 3: Personal Dictionary

Learn custom names, acronyms, and jargon so the cleanup LLM uses correct
spellings.

### IPersonalDictionaryService

```csharp
// Services/Dictation/IPersonalDictionaryService.cs
public interface IPersonalDictionaryService
{
    /// <summary>Add a word/phrase to the dictionary.</summary>
    void Add(string entry, string? description = null);

    /// <summary>Remove a word/phrase.</summary>
    void Remove(string entry);

    /// <summary>Get all entries.</summary>
    IReadOnlyList<DictionaryEntry> GetAll();

    /// <summary>Get dictionary context string for LLM prompt.</summary>
    string GetContext();
}

public record DictionaryEntry(string Word, string? Description);
```

### PersonalDictionaryService

Persists to a JSON file in app data:

```csharp
// Services/Dictation/PersonalDictionaryService.cs
public class PersonalDictionaryService : IPersonalDictionaryService
{
    private readonly List<DictionaryEntry> _entries = [];
    private readonly string _filePath;

    public PersonalDictionaryService()
    {
        _filePath = Path.Combine(
            FileSystem.AppDataDirectory, "personal-dictionary.json");
        Load();
    }

    public void Add(string entry, string? description = null)
    {
        if (_entries.Any(e => e.Word.Equals(entry, StringComparison.OrdinalIgnoreCase)))
            return;
        _entries.Add(new DictionaryEntry(entry, description));
        Save();
    }

    public string GetContext()
    {
        if (_entries.Count == 0) return string.Empty;
        return string.Join("\n", _entries.Select(e =>
            e.Description != null ? $"- {e.Word}: {e.Description}" : $"- {e.Word}"));
    }

    // ... Load/Save JSON implementation
}
```

### Adding Words
Users can add dictionary entries via:
1. **Voice command:** "Add MAUI to my dictionary" (via AddDictionaryTool)
2. **Settings UI:** Dictionary management page
3. **Auto-learn:** When the user manually corrects a word, offer to add it

### Tests
```csharp
public class PersonalDictionaryServiceTests
{
    [Fact]
    public void Add_NewEntry_AppearsInGetAll()
    {
        var svc = CreateTempDictionary();
        svc.Add("MAUI", ".NET Multi-platform App UI");
        svc.GetAll().Should().ContainSingle(e => e.Word == "MAUI");
    }

    [Fact]
    public void Add_Duplicate_DoesNotAddTwice()
    {
        var svc = CreateTempDictionary();
        svc.Add("MAUI");
        svc.Add("maui");
        svc.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void GetContext_FormatsForLlm()
    {
        var svc = CreateTempDictionary();
        svc.Add("MAUI", ".NET Multi-platform App UI");
        svc.Add("BodyCam");
        var ctx = svc.GetContext();
        ctx.Should().Contain("- MAUI: .NET Multi-platform App UI");
        ctx.Should().Contain("- BodyCam");
    }
}
```

### Verify
- [ ] Dictionary persists to JSON
- [ ] Duplicates prevented (case-insensitive)
- [ ] GetContext formats entries for LLM prompt
- [ ] Tests pass

---

## Wave 4: Integrate Cleanup into DictationService

Wire the buffer + cleanup into the existing `DictationService`:

```csharp
// Updated DictationService — now buffers sentences and cleans them
public class DictationService : IDictationService
{
    private readonly IDictationCleanupService _cleanup;
    private readonly TranscriptBuffer _buffer = new();

    // ... existing state machine code ...

    public async void ProcessTranscript(string text)
    {
        if (_state != DictationState.Listening) return;

        if (_mode == DictationMode.Raw)
        {
            // Direct pass-through (Phase 1 behavior)
            TextReady?.Invoke(this, new DictationTextEventArgs(text, true));
            return;
        }

        // Buffer until we have complete sentences
        var sentences = _buffer.Add(text);
        foreach (var sentence in sentences)
        {
            SetState(DictationState.Processing);
            var cleaned = await _cleanup.CleanAsync(sentence, _mode);
            SetState(DictationState.Listening);

            TextReady?.Invoke(this, new DictationTextEventArgs(cleaned, true));
        }
    }

    public override async Task StopAsync(CancellationToken ct = default)
    {
        // Flush remaining buffer
        var remaining = _buffer.Flush();
        if (remaining != null)
        {
            var cleaned = await _cleanup.CleanAsync(remaining, _mode);
            TextReady?.Invoke(this, new DictationTextEventArgs(cleaned, true));
        }

        SetState(DictationState.Idle);
    }
}
```

### Latency Budget
| Step | Latency |
|------|---------|
| STT (Realtime API) | ~200-500ms |
| Sentence buffer | 0ms (fires on sentence boundary) |
| GPT-4o-mini cleanup | ~300-500ms |
| Text injection (clipboard) | ~50ms |
| **Total (Raw mode)** | **~250-550ms** |
| **Total (Clean mode)** | **~550-1050ms** |
| **Total (Rewrite mode)** | **~800-1500ms** |

### Verify
- [ ] Raw mode still works (no cleanup call)
- [ ] Clean mode buffers → cleans → injects
- [ ] Rewrite mode produces polished text
- [ ] Flush on stop sends remaining text
- [ ] Processing state shown during cleanup
- [ ] All tests pass
- [ ] Build succeeds

---

## Wave 5: Mode Switching & DI Updates

### Voice Mode Switching
User can switch modes mid-dictation:
- "Switch to clean mode"
- "Switch to rewrite mode"  
- "Switch to raw mode"

The `StartDictationTool` already accepts a `Mode` argument. Add a
`SetDictationModeTool` for switching without restarting.

### DI Registration Updates
```csharp
// MauiProgram.cs additions for Phase 2
builder.Services.AddSingleton<IDictationCleanupService, DictationCleanupService>();
builder.Services.AddSingleton<IPersonalDictionaryService, PersonalDictionaryService>();
builder.Services.AddSingleton<ITool, SetDictationModeTool>();
```

### Verify
- [ ] Mode switching works mid-dictation
- [ ] DI registration compiles
- [ ] All Phase 1 + Phase 2 tests pass
- [ ] Build succeeds
