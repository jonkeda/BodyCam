# Step 01 — DI Registration + MAF Client Factory

Replace the hand-rolled `IRealtimeClient` registration with `Microsoft.Extensions.AI.IRealtimeClient` using the builder pipeline.

**Depends on:** Nothing  
**Touches:** `ServiceExtensions.cs`  
**Tests affected:** None (nothing consumes the new registration yet)

---

## What to Do

### 1.1 — Replace registration in `AddOrchestration()`

```
src/BodyCam/ServiceExtensions.cs
```

**Remove this line:**
```csharp
services.AddSingleton<IRealtimeClient, RealtimeClient>();
```

**Add the MAF registration (before `AgentOrchestrator`):**
```csharp
// MAF Realtime client with function-invocation middleware
services.AddSingleton<Microsoft.Extensions.AI.IRealtimeClient>(sp =>
{
    var settings = sp.GetRequiredService<AppSettings>();
    var apiKeyService = sp.GetRequiredService<IApiKeyService>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    var apiKey = apiKeyService.GetApiKeyAsync().GetAwaiter().GetResult()
        ?? throw new InvalidOperationException("API key not configured.");
    var credential = new System.ClientModel.ApiKeyCredential(apiKey);

    OpenAIRealtimeClient baseClient;
    if (settings.Provider == OpenAiProvider.Azure)
    {
        var azureClient = new AzureOpenAIClient(
            new Uri(settings.AzureEndpoint!), credential);
        // GetRealtimeClient() inherited from OpenAIClient
        var sdkRtClient = azureClient.GetRealtimeClient();
        baseClient = new OpenAIRealtimeClient(sdkRtClient, settings.AzureRealtimeDeploymentName!);
    }
    else
    {
        baseClient = new OpenAIRealtimeClient(apiKey, settings.RealtimeModel);
    }

    return baseClient.AsBuilder()
        .UseFunctionInvocation(loggerFactory)
        .UseLogging(loggerFactory)
        .Build(sp);
});
```

**Required usings (add at top of file):**
```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using Azure.AI.OpenAI;
```

> **Note:** `OpenAIRealtimeClient` is in `Microsoft.Extensions.AI.OpenAI`. Check the exact type name — it may be `OpenAIRealtimeClient` or accessed via `openaiClient.AsRealtimeClient(model)`. If `AsRealtimeClient()` extension exists, use that instead of `new OpenAIRealtimeClient(...)`.

### 1.2 — Keep the old registration temporarily

During the transition (steps 02-04 still need the old `IRealtimeClient` until they're rewritten), keep the old registration alive:

```csharp
services.AddSingleton<BodyCam.Services.IRealtimeClient, BodyCam.Services.RealtimeClient>();
```

Use the fully-qualified name to avoid collision with `Microsoft.Extensions.AI.IRealtimeClient`. This line gets deleted in step 05.

### 1.3 — Verify build

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
```

No behavioral change — nothing consumes the new MAF client yet. This step just proves the factory resolves and the middleware pipeline builds.

---

## Acceptance Criteria

- [ ] `Microsoft.Extensions.AI.IRealtimeClient` registered in DI with `FunctionInvokingRealtimeClient` + `LoggingRealtimeClient` middleware
- [ ] Both OpenAI and Azure paths handled
- [ ] Old `BodyCam.Services.IRealtimeClient` still registered (temporary)
- [ ] Windows build succeeds
- [ ] Android build succeeds

---

## Key Decisions

- **`FunctionInvokingRealtimeClient`** is registered here via `.UseFunctionInvocation()`. This means tools passed on `RealtimeSessionOptions.Tools` will be automatically invoked by the middleware — no manual function call handling needed in the orchestrator.
- **`LoggingRealtimeClient`** via `.UseLogging()` gives us structured logging of all Realtime messages for free.
- The old `RealtimeClient` stays alive during transition to avoid breaking `VoiceInputAgent` and `AgentOrchestrator` until they're rewritten.
