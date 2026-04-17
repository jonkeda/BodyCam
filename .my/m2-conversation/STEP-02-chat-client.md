# M2 Implementation — Step 2: Chat Completions Client

**Depends on:** Step 1 (ConversationMode enum, enhanced SessionContext)
**Produces:** `IChatCompletionsClient`, implementation using `Microsoft.Extensions.AI`, DI registration, NuGet packages

---

## Why This Step?

`ConversationAgent` needs to call the Chat Completions API. Rather than raw `HttpClient`, we use `Microsoft.Extensions.AI`'s `IChatClient` — this is the MAF-aligned abstraction that supports both OpenAI and Azure OpenAI, and enables future tool use / MCP bridging.

---

## Tasks

### 2.1 — Add NuGet packages

**File:** `src/BodyCam/BodyCam.csproj` — MODIFY

```xml
<PackageReference Include="Microsoft.Extensions.AI" Version="*" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="*" />
<PackageReference Include="OpenAI" Version="*" />
```

Pin versions after initial `dotnet restore` confirms compatibility with net10.0.

For Azure OpenAI support:
```xml
<PackageReference Include="Azure.AI.OpenAI" Version="*" />
```

### 2.2 — Create `IChatCompletionsClient` interface

**File:** `src/BodyCam/Services/IChatCompletionsClient.cs` — NEW

```csharp
namespace BodyCam.Services;

/// <summary>
/// Abstraction over Chat Completions API for testability.
/// Wraps Microsoft.Extensions.AI's IChatClient.
/// </summary>
public interface IChatCompletionsClient
{
    /// <summary>
    /// Sends messages to Chat Completions and returns the full reply.
    /// </summary>
    Task<string> CompleteAsync(
        IList<Models.ChatMessage> messages,
        CancellationToken ct = default);

    /// <summary>
    /// Sends messages to Chat Completions and streams reply tokens.
    /// </summary>
    IAsyncEnumerable<string> CompleteStreamingAsync(
        IList<Models.ChatMessage> messages,
        CancellationToken ct = default);
}
```

**Why a wrapper?** `IChatClient` from `Microsoft.Extensions.AI` works with its own `ChatMessage` type. Our `BodyCam.Models.ChatMessage` is simpler. The wrapper maps between them and keeps the interface stable for tests.

### 2.3 — Implement `ChatCompletionsClient`

**File:** `src/BodyCam/Services/ChatCompletionsClient.cs` — NEW

```csharp
using Microsoft.Extensions.AI;
using BodyCam.Models;
using ChatRole = Microsoft.Extensions.AI.ChatRole;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.Services;

public class ChatCompletionsClient : IChatCompletionsClient
{
    private readonly IChatClient _chatClient;

    public ChatCompletionsClient(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> CompleteAsync(
        IList<Models.ChatMessage> messages,
        CancellationToken ct = default)
    {
        var aiMessages = MapMessages(messages);
        var response = await _chatClient.GetResponseAsync(aiMessages, cancellationToken: ct);
        return response.Text ?? string.Empty;
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        IList<Models.ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var aiMessages = MapMessages(messages);
        await foreach (var update in _chatClient.GetStreamingResponseAsync(aiMessages, cancellationToken: ct))
        {
            if (update.Text is { Length: > 0 } text)
                yield return text;
        }
    }

    private static List<AiChatMessage> MapMessages(IList<Models.ChatMessage> messages)
    {
        return messages.Select(m => new AiChatMessage(
            m.Role switch
            {
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                _ => ChatRole.User
            },
            m.Content
        )).ToList();
    }
}
```

### 2.4 — Register `IChatClient` and `IChatCompletionsClient` in DI

**File:** `src/BodyCam/MauiProgram.cs` — MODIFY

```csharp
using Microsoft.Extensions.AI;
using OpenAI;

// After existing service registrations:

// Chat Completions client (Mode B)
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var appSettings = sp.GetRequiredService<AppSettings>();
    var apiKeyService = sp.GetRequiredService<IApiKeyService>();

    if (appSettings.Provider == OpenAiProvider.Azure)
    {
        // Azure OpenAI via Azure.AI.OpenAI
        var credential = new Azure.AzureKeyCredential(
            apiKeyService.GetApiKeyAsync().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("API key not configured."));
        var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(appSettings.AzureEndpoint!), credential);
        return azureClient.GetChatClient(appSettings.AzureChatDeploymentName!).AsIChatClient();
    }
    else
    {
        // Direct OpenAI
        var key = apiKeyService.GetApiKeyAsync().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("API key not configured.");
        var openAiClient = new OpenAIClient(key);
        return openAiClient.GetChatClient(appSettings.ChatModel).AsIChatClient();
    }
});

builder.Services.AddSingleton<IChatCompletionsClient, ChatCompletionsClient>();
```

**Note:** The `IChatClient` is registered lazily — it reads the API key at resolution time. This is fine because services are resolved after the user has entered their key.

### 2.5 — Add test project package references

**File:** `src/BodyCam.Tests/BodyCam.Tests.csproj` — MODIFY

Add matching packages so tests can mock `IChatCompletionsClient`.

---

## Verification

- [ ] `dotnet build` succeeds with new NuGet packages
- [ ] `IChatCompletionsClient` resolves from DI
- [ ] Can construct `ChatCompletionsClient` with a mock `IChatClient`
- [ ] `MapMessages` correctly converts all three roles (system, user, assistant)
- [ ] Streaming yields tokens progressively

---

## Unit Tests

**File:** `src/BodyCam.Tests/Services/ChatCompletionsClientTests.cs` — NEW

```csharp
[Fact] CompleteAsync_ReturnsChatResponse()
[Fact] CompleteStreamingAsync_YieldsTokens()
[Fact] MapMessages_MapsAllRoles()
[Fact] CompleteAsync_PropagatesCancellation()
```

These use a mock `IChatClient` — no real API calls.

---

## Risks

| Risk | Mitigation |
|------|-----------|
| NuGet version conflicts with net10.0 | Pin versions, test `dotnet restore` first |
| `IChatClient` API changes | Pin package version |
| Lazy API key resolution in DI | Falls back to prompt dialog; error message is clear |
