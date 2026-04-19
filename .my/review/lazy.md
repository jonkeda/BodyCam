# Review: Removal of Lazy<T> Wrappers

## Problem

Two `Lazy<T>` wrappers were used to defer service creation:

1. **`Lazy<IChatClient>`** in `MauiProgram.cs` — a 20-line factory lambda that called
   `apiKeyService.GetApiKeyAsync().GetAwaiter().GetResult()`, blocking the calling thread.
   On Android, this deadlocked because `SecureStorage.GetAsync` requires the main thread
   but `.GetResult()` was already holding it.

2. **`Lazy<IWakeWordService>`** in `ServiceExtensions.cs` — wrapped a service whose
   constructor (`PorcupineWakeWordService`) only stores a single field. No reason to defer.

Both caused `Lazy<T>` to leak into constructor signatures across agents and orchestrators,
forcing every consumer and test to deal with `.Value` indirection.

## What Changed

| File | Before | After |
|------|--------|-------|
| `Services/AppChatClient.cs` | *(new)* | `IChatClient` impl that resolves API key async on first call |
| `MauiProgram.cs` | 20-line `Lazy<IChatClient>` factory with `.GetResult()` | `AddSingleton<IChatClient, AppChatClient>()` |
| `ServiceExtensions.cs` | `Lazy<IWakeWordService>` factory registration | Removed — direct `IWakeWordService` |
| `Agents/VisionAgent.cs` | `Lazy<IChatClient>` field + `.Value` | `IChatClient` directly |
| `Agents/ConversationAgent.cs` | `Lazy<IChatClient>` field + `.Value` | `IChatClient` directly |
| `Orchestration/AgentOrchestrator.cs` | `Lazy<IWakeWordService>` field + `.Value` | `IWakeWordService` directly |
| 2 test files | `new Lazy<IWakeWordService>(() => mock)` | Pass mock directly |

## Design: AppChatClient

```
IChatClient (interface from Microsoft.Extensions.AI)
  └── AppChatClient (our impl)
        ├── constructor: stores IApiKeyService + AppSettings (no I/O)
        ├── GetResponseAsync: await GetOrCreateClientAsync() → delegate
        ├── GetStreamingResponseAsync: same pattern
        └── GetOrCreateClientAsync(): double-checked lock via SemaphoreSlim
              ├── reads API key via await (no .GetResult())
              ├── throws clear error if key is missing
              └── creates real OpenAI/Azure client, caches in _inner
```

Key properties:
- **No blocking** — API key is read with `await`, never `.GetResult()`
- **Thread-safe** — `SemaphoreSlim` with double-check pattern
- **Fail-at-use** — missing key throws at call time with actionable message, not at startup
- **Cacheable** — inner client created once, reused for all subsequent calls
- **Testable** — agents take `IChatClient`, tests pass mocks directly

## What Was Wrong With Lazy<T> Here

1. **Hidden sync-over-async** — The `Lazy<IChatClient>` factory called `.GetAwaiter().GetResult()`,
   which is always risky and specifically deadlocks on Android's main thread with `SecureStorage`.

2. **API leakage** — `Lazy<T>` is an implementation detail of *when* something is created. It
   shouldn't appear in constructor signatures. Agents don't care about deferred creation; they
   care about having an `IChatClient`.

3. **Test friction** — Every test had to wrap mocks in `new Lazy<T>(() => mock)` for no reason.

4. **Invisible failures** — `Lazy<T>.Value` throws on first access with no stack trace back to
   the registration site. `AppChatClient` throws at the exact async call with a clear message.

5. **Unnecessary for IWakeWordService** — `PorcupineWakeWordService` constructor does zero I/O.
   The Lazy wrapper added complexity with no benefit.

## Verification

- Build: 0 errors
- Tests: 372 passed, 0 failed
- Android ANR root cause (blocking `.GetResult()` on main thread) is eliminated
