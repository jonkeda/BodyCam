# RCA — Real Tests Build Failure (RealtimeFixture & M5ToolFixture)

**Date:** 2026-04-19
**Project:** `BodyCam.RealTests`
**Symptom:** 4 build errors — `RealtimeClient` type not found

---

## Root Cause

The M25 MAF migration removed the custom `BodyCam.Services.Realtime.RealtimeClient` WebSocket client class and the entire `BodyCam.Services.Realtime` namespace (including `ServerEventParser`). Both test fixtures (`RealtimeFixture`, `M5ToolFixture`) directly instantiate and use `RealtimeClient`, which no longer exists.

### What Was Removed

| Type | Old Location | Replacement |
|---|---|---|
| `RealtimeClient` | `BodyCam.Services.Realtime` | `Microsoft.Extensions.AI.OpenAIRealtimeClient` (MAF wrapper around `OpenAI.Realtime.RealtimeClient`) |
| `ServerEventParser` | `BodyCam.Services.Realtime` | No equivalent — MAF handles event parsing internally |
| `IRealtimeClient` | `BodyCam.Services` | `Microsoft.Extensions.AI.IRealtimeClient` |

### Old API Surface Used by Fixtures

The old `RealtimeClient` had custom events that the fixtures subscribed to:
- `RawMessageReceived` — raw JSON from server
- `AudioDelta` — audio data chunks
- `OutputTranscriptDelta` / `OutputTranscriptCompleted` — transcript streaming
- `InputTranscriptCompleted` — user speech transcript
- `ResponseDone` — response completion with `RealtimeResponseInfo`
- `ErrorOccurred` — error messages
- `FunctionCallReceived` — function call with `FunctionCallInfo`

And methods:
- `ConnectAsync()` / `DisconnectAsync()`
- `SendTextInputAsync(text)`
- `SendFunctionCallOutputAsync(callId, output)`
- `CancelResponseAsync()`

### New MAF API

MAF uses `IRealtimeClient` → `CreateSessionAsync()` → `IRealtimeClientSession` with:
- `SendAsync(RealtimeClientMessage)` for sending
- `GetStreamingAsync()` returning `IAsyncEnumerable<RealtimeServerMessage>` for receiving
- Server messages are typed via `RealtimeServerMessageType` (audio, transcript, function call, etc.)

## Fix

The fixtures need a complete rewrite to use the MAF `IRealtimeClient`/`IRealtimeClientSession` API:

1. Replace `RealtimeClient` with `OpenAIRealtimeClient` (construct from API key + model)
2. Replace event-driven pattern with `GetStreamingAsync()` message loop
3. Replace `ServerEventParser` with MAF's typed `RealtimeServerMessage` pattern matching
4. Map old methods to MAF equivalents:
   - `SendTextInputAsync` → `session.SendAsync(new RealtimeClientMessage(...))`
   - `ConnectAsync` → `client.CreateSessionAsync(options)`
   - `DisconnectAsync` → `session.DisposeAsync()`
5. Update all test files that depend on fixture events (`EventTracking/*`, `Pipeline/*`)
