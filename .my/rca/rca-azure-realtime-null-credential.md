# RCA — ArgumentNullException in Azure Realtime Client Construction

## Error

```
System.ArgumentNullException: 'Value cannot be null.'
   at OpenAI.Argument.AssertNotNull[T](T value, String name)
   at OpenAI.OpenAIClient.CreateApiKeyAuthenticationPolicy(ApiKeyCredential credential)
   at OpenAI.Realtime.RealtimeClient..ctor(ApiKeyCredential credential, RealtimeClientOptions options)
   at OpenAI.OpenAIClient.GetRealtimeClient()
   at BodyCam.ServiceExtensions.<>c.<AddOrchestration>b__4_1(IServiceProvider sp)
     in ServiceExtensions.cs:line 124
```

## Trigger

App startup when `Provider == OpenAiProvider.Azure` — the `IRealtimeClient` singleton is resolved from DI.

## Root Cause

The Azure path constructs an `AzureOpenAIClient` with an `ApiKeyCredential`, then calls `GetRealtimeClient()`:

```csharp
var credential = new System.ClientModel.ApiKeyCredential(apiKey);
var azureClient = new AzureOpenAIClient(new Uri(endpoint), credential);
var sdkRtClient = azureClient.GetRealtimeClient();  // ← CRASH
```

`AzureOpenAIClient` extends `OpenAIClient`. However, `AzureOpenAIClient` stores the `ApiKeyCredential` in its own Azure auth pipeline — it does **not** set the base `OpenAIClient._credential` field. When `GetRealtimeClient()` runs, it delegates to `OpenAIClient.GetRealtimeClient()` which passes `this._credential` (null) into `new RealtimeClient(credential, ...)`, triggering `ArgumentNullException`.

This is a known limitation of Azure.AI.OpenAI SDK — `GetRealtimeClient()` is an OpenAI-only convenience. Azure callers must construct the realtime client directly with the credential.

## Fix

Construct the `OpenAI.Realtime.RealtimeClient` directly with the `ApiKeyCredential` and a `RealtimeClientOptions` pointing at the Azure Realtime WebSocket URL. This bypasses `AzureOpenAIClient.GetRealtimeClient()` entirely:

```csharp
var credential = new ApiKeyCredential(apiKey);
var rtOptions = new RealtimeClientOptions
{
    Endpoint = new Uri($"{endpoint}/openai/realtime?api-version={version}&deployment={deployment}")
};
var sdkRtClient = new RealtimeClient(credential, rtOptions);
baseClient = new OpenAIRealtimeClient(sdkRtClient, deployment);
```

The deployment name and API version are passed as query parameters in the endpoint URL, matching the Azure Realtime API contract. The `RealtimeClient` handles the WebSocket handshake with the credential directly.

## Impact

- **Azure provider is completely broken** — app crashes on startup when Azure is selected.
- OpenAI provider is unaffected (uses `new OpenAIRealtimeClient(apiKey, model)` directly).

## Files

| File | Line | Change |
|---|---|---|
| `src/BodyCam/ServiceExtensions.cs` | 119–126 | Remove `GetRealtimeClient()` call; pass `azureClient` directly to `OpenAIRealtimeClient` |
