# Azure OpenAI — GA Endpoint Migration

> **Status: COMPLETED (April 2026).** The codebase now uses the GA `/openai/v1/` endpoint with `Microsoft.Extensions.AI` (`IRealtimeClient`). The preview endpoint and raw WebSocket parsing have been removed. This document is kept for historical reference.

How to create an Azure OpenAI resource that supports the GA `/openai/v1/` endpoint path, enabling native SDK event handling (no raw WebSocket workarounds).

## Current Architecture

The app uses **Microsoft.Extensions.AI (MAF)** as the abstraction layer:
- `IRealtimeClient` / `IRealtimeClientSession` — MAF interfaces
- `OpenAIRealtimeClient` — MAF adapter wrapping the OpenAI SDK's `RealtimeClient`
- `AzureRealtimeClient` — subclass of SDK `RealtimeClient` that injects `api-key` header
- GA endpoint: `wss://{resource}.cognitiveservices.azure.com/openai/v1/realtime`
- Transcription model: `whisper-1` on Azure (`gpt-4o-mini-transcribe` is not available on Azure)
- Tool dispatch is manual via `RawRepresentation` (MAF doesn't have `FunctionInvokingRealtimeClient` for realtime)

---

## Historical Context

The previous Cognitive Services resource (`*.cognitiveservices.azure.com`) only supported the **preview** endpoint:

```
wss://{resource}.cognitiveservices.azure.com/openai/realtime?api-version=2025-04-01-preview&deployment=...
```

The preview endpoint sends non-standard event names (`response.audio.delta` instead of `response.output_audio.delta`), forcing us to parse raw WebSocket JSON. The GA endpoint sends standard events that the OpenAI SDK can deserialize natively, simplifying the code significantly.

**Preview API versions are retired on April 30, 2026.**

## Step 1 — Create an Azure OpenAI Resource

> **Important:** Create an **Azure OpenAI** resource, NOT a "Cognitive Services" or "AI Services" multi-service resource.

1. Go to [Azure Portal](https://portal.azure.com) → **Create a resource**
2. Search for **Azure OpenAI** and select it
3. Fill in:
   - **Resource group**: your existing group or create new
   - **Region**: **West Europe** (North Europe is NOT supported for Azure OpenAI)
   - **Name**: e.g., `bodycam-openai-westeurope`
   - **Pricing tier**: Standard S0
4. Click **Review + create** → **Create**
5. Once deployed, go to the resource

Your endpoint will be:
```
https://bodycam-openai-westeurope.openai.azure.com
```

Note the domain: `*.openai.azure.com` — this is what supports `/openai/v1/`.

## Step 2 — Get Your API Key

1. In your new Azure OpenAI resource, go to **Keys and Endpoint**
2. Copy **Key 1**

## Step 3 — Deploy Models

Go to [Azure AI Foundry](https://ai.azure.com) → select your new resource → **Deployments** → **Deploy model**

### Required Deployments

| Deployment Name | Model | Version | Notes |
|---|---|---|---|
| `bodycam-realtime` | `gpt-realtime-mini` | `2025-12-15` | Cheapest realtime option |
| `bodycam-chat` | `gpt-5.4-nano` | `2026-03-17` | Cheapest chat model |
| `bodycam-vision` | `gpt-5.4-mini` | `2026-03-17` | Cheapest with vision |

### Alternative Realtime Models

| Model | Version | Pros | Cons |
|---|---|---|---|
| `gpt-realtime-mini` | `2025-10-06` / `2025-12-15` | Cheapest | Lower quality |
| `gpt-realtime` | `2025-08-28` | Good balance | More expensive |
| `gpt-realtime-1.5` | `2026-02-23` | Best quality | Most expensive |

## Step 4 — Update `.env`

```env
OPENAI_PROVIDER=azure
AZURE_OPENAI_API_KEY=your-new-key-here
AZURE_OPENAI_ENDPOINT=https://bodycam-openai-eastus2.openai.azure.com
AZURE_OPENAI_DEPLOYMENT=bodycam-realtime
AZURE_OPENAI_CHAT_DEPLOYMENT=bodycam-chat
AZURE_OPENAI_VISION_DEPLOYMENT=bodycam-vision
AZURE_OPENAI_API_VERSION=2024-10-01-preview
```

## Step 5 — Switch Code to GA Endpoint

Once the resource is working with the preview path, switch to the GA endpoint:

### In `AzureRealtimeClient.cs`

Remove the `api-version` and `deployment` query params. The GA endpoint only needs the `api-key` header:

```csharp
public override async Task<RealtimeSessionClient> StartSessionAsync(
    string model, string intent,
    RealtimeSessionClientOptions? options = null,
    CancellationToken cancellationToken = default)
{
    options ??= new();
    options.Headers["api-key"] = _apiKey;
    return await base.StartSessionAsync(model, intent, options, cancellationToken)
        .ConfigureAwait(false);
}
```

### In `ServiceExtensions.cs` and `RealtimeFixture.cs`

Change the endpoint path from `/openai/realtime` to `/openai/v1/realtime`:

```csharp
Endpoint = new Uri($"{settings.AzureEndpoint!.TrimEnd('/')}/openai/v1/realtime")
```

### In `AgentOrchestrator.cs`

If the SDK now returns typed events, replace `RunMessageLoopAsync` (raw WebSocket parsing) with `ReceiveUpdatesAsync`:

```csharp
await foreach (var update in session.ReceiveUpdatesAsync(ct))
{
    switch (update)
    {
        case RealtimeAudioDelta audioDelta:
            // handle audio
            break;
        case RealtimeAudioTranscriptDelta transcriptDelta:
            // handle transcript
            break;
        // ... etc
    }
}
```

Also replace `ConfigureSessionAsync` (manual JSON) with `ConfigureConversationSessionAsync`.

## Verifying It Works

Run the diagnostic test:

```powershell
dotnet test src/BodyCam.RealTests --filter "RealtimeDiagnosticTests" -v n
```

The output should show typed event classes (e.g., `RealtimeAudioDelta`) instead of `InternalUnknownRealtimeServerEventGA`.

## Keeping the Preview Endpoint (Fallback)

If you can't create an `openai.azure.com` resource, the preview endpoint works fine with any model — you just keep the raw WebSocket parsing code. The trade-off:

| | Preview Endpoint | GA Endpoint |
|---|---|---|
| Domain | `*.cognitiveservices.azure.com` | `*.openai.azure.com` |
| Path | `/openai/realtime?api-version=...` | `/openai/v1/realtime` |
| Event names | `response.audio.delta` | `response.output_audio.delta` |
| SDK support | Raw WebSocket parsing needed | Native typed events |
| Retirement | April 30, 2026 | Supported long-term |
