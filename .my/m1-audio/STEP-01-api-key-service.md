# M1 Implementation — Step 1: API Key Service + Secure Storage

**Depends on:** M0 (scaffold complete)
**Produces:** `IApiKeyService`, `ApiKeyService`, updated `AppSettings`, `.gitignore`, `.env` setup

---

## Why First?
Every subsequent step (WebSocket connection, Realtime client) needs an API key. We can't connect to OpenAI without one. This also fulfills M7 tasks 7.1, 7.2, 7.5, 7.7 that are prerequisites for M1.

---

## Tasks

### 1.0 — Add `.gitignore` and `.env` infrastructure

The repo has **no `.gitignore`**. Fix that first, and set up `.env` for dev-time API key entry.

**File:** `.gitignore` (repo root) — NEW

Standard .NET/MAUI ignores plus:
```gitignore
# IDE
.vs/
.vscode/
*.user
*.suo

# Build output
bin/
obj/
artifacts/

# NuGet
*.nupkg
*.snupkg

# OS
Thumbs.db
.DS_Store

# Secrets — NEVER commit
.env
*.env.local

# Planning docs (optional — remove if you want these tracked)
# .my/
```

**File:** `.env.example` (repo root) — NEW (committed as template)

```env
# Copy this file to .env and fill in your values.
# .env is git-ignored and will never be committed.

# Provider: "openai" (direct) or "azure" (Azure OpenAI)
OPENAI_PROVIDER=openai

# --- Direct OpenAI ---
OPENAI_API_KEY=sk-proj-your-key-here

# --- Azure OpenAI ---
# OPENAI_PROVIDER=azure
# AZURE_OPENAI_API_KEY=your-azure-key-here
# AZURE_OPENAI_RESOURCE=your-resource-name
# AZURE_OPENAI_DEPLOYMENT=gpt-5.4-realtime
# AZURE_OPENAI_API_VERSION=2025-04-01-preview
```

**File:** `.env` (repo root) — developer creates locally, git-ignored. Example for Azure:

```env
OPENAI_PROVIDER=azure
AZURE_OPENAI_API_KEY=abc123def456...
AZURE_OPENAI_RESOURCE=mycompany-openai
AZURE_OPENAI_DEPLOYMENT=gpt-5.4-realtime
AZURE_OPENAI_API_VERSION=2025-04-01-preview
```

**How the key flows at runtime:**
1. App starts → `ApiKeyService.GetApiKeyAsync()` checks MAUI `SecureStorage`
2. No saved key → app reads `.env` file from app root (dev only) or env vars
3. Still no key → show prompt dialog (Step 9, production/first-run path)
4. Once obtained → saved to `SecureStorage` for next launch
5. Provider + Azure settings loaded from `.env` / env vars into `AppSettings` at startup

**Key safety guarantee:** `.env` is in `.gitignore` before it's ever created. The committed `.env.example` contains only placeholder text.

### 1.1 — Add `IApiKeyService` interface

**File:** `src/BodyCam/Services/IApiKeyService.cs`

```csharp
namespace BodyCam.Services;

public interface IApiKeyService
{
    Task<string?> GetApiKeyAsync();
    Task SetApiKeyAsync(string apiKey);
    Task ClearApiKeyAsync();
    bool HasKey { get; }
}
```

### 1.2 — Add `ApiKeyService` implementation (SecureStorage + .env fallback)

**File:** `src/BodyCam/Services/ApiKeyService.cs`

```csharp
namespace BodyCam.Services;

public class ApiKeyService : IApiKeyService
{
    private const string StorageKey = "openai_api_key";
    private string? _cachedKey;

    public bool HasKey => _cachedKey is not null;

    public async Task<string?> GetApiKeyAsync()
    {
        if (_cachedKey is not null)
            return _cachedKey;

        // 1. Try MAUI SecureStorage (persisted from previous launch)
        _cachedKey = await SecureStorage.Default.GetAsync(StorageKey);
        if (_cachedKey is not null)
            return _cachedKey;

        // 2. Try .env file in app base directory (dev convenience)
        //    Supports both OPENAI_API_KEY (direct) and AZURE_OPENAI_API_KEY (Azure)
        _cachedKey = ReadFromDotEnv("AZURE_OPENAI_API_KEY")
                  ?? ReadFromDotEnv("OPENAI_API_KEY");
        if (_cachedKey is not null)
        {
            await SecureStorage.Default.SetAsync(StorageKey, _cachedKey);
            return _cachedKey;
        }

        // 3. Try environment variable (CI / dev terminal)
        _cachedKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (_cachedKey is not null)
        {
            await SecureStorage.Default.SetAsync(StorageKey, _cachedKey);
            return _cachedKey;
        }

        return null; // Step 9 prompt dialog handles this case
    }

    public async Task SetApiKeyAsync(string apiKey)
    {
        await SecureStorage.Default.SetAsync(StorageKey, apiKey);
        _cachedKey = apiKey;
    }

    public Task ClearApiKeyAsync()
    {
        SecureStorage.Default.Remove(StorageKey);
        _cachedKey = null;
        return Task.CompletedTask;
    }

    private static string? ReadFromDotEnv(string key)
    {
        var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
        if (!File.Exists(envPath))
            return null;

        foreach (var line in File.ReadLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || !trimmed.Contains('='))
                continue;

            var eqIndex = trimmed.IndexOf('=');
            var envKey = trimmed[..eqIndex].Trim();
            var envVal = trimmed[(eqIndex + 1)..].Trim();

            if (envKey == key && envVal.Length > 0)
                return envVal;
        }

        return null;
    }
}
```

**Fallback priority:** SecureStorage → `.env` file → `OPENAI_API_KEY` env var → null (prompt dialog in Step 9)

### 1.3 — Update `AppSettings`

Add Realtime-specific settings. Remove the hard-coded `OpenAiApiKey` property (key now comes from `IApiKeyService`).

**File:** `src/BodyCam/AppSettings.cs`

```csharp
namespace BodyCam;

public enum OpenAiProvider { OpenAi, Azure }

public class AppSettings
{
    // Provider
    public OpenAiProvider Provider { get; set; } = OpenAiProvider.OpenAi;

    // Models
    public string RealtimeModel { get; set; } = "gpt-5.4-realtime";
    public string ChatModel { get; set; } = "gpt-5.4-mini";
    public string VisionModel { get; set; } = "gpt-5.4";

    // Realtime API — Direct OpenAI
    public string RealtimeApiEndpoint { get; set; } = "wss://api.openai.com/v1/realtime";
    public string Voice { get; set; } = "marin";
    public string TurnDetection { get; set; } = "semantic_vad";
    public string NoiseReduction { get; set; } = "near_field";
    public string SystemInstructions { get; set; } = "You are a helpful assistant.";

    // Azure OpenAI
    public string? AzureResourceName { get; set; }
    public string? AzureDeploymentName { get; set; }
    public string AzureApiVersion { get; set; } = "2025-04-01-preview";

    // Audio
    public int SampleRate { get; set; } = 24000;
    public int ChunkDurationMs { get; set; } = 50;

    /// <summary>
    /// Builds the WebSocket URI for the configured provider.
    /// </summary>
    public Uri GetRealtimeUri() => Provider switch
    {
        OpenAiProvider.Azure =>
            new Uri($"wss://{AzureResourceName}.openai.azure.com/openai/realtime"
                  + $"?api-version={AzureApiVersion}&deployment={AzureDeploymentName}"),
        _ =>
            new Uri($"{RealtimeApiEndpoint}?model={RealtimeModel}")
    };
}
```

### 1.4 — Load provider settings from `.env` at startup

At app startup (in `MauiProgram.cs`), read `.env` / env vars to populate `AppSettings` provider fields:

```csharp
// In MauiProgram.CreateMauiApp(), after creating AppSettings
var settings = new AppSettings();

var provider = EnvReader.Read("OPENAI_PROVIDER"); // helper or inline
if (string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase))
{
    settings.Provider = OpenAiProvider.Azure;
    settings.AzureResourceName = EnvReader.Read("AZURE_OPENAI_RESOURCE");
    settings.AzureDeploymentName = EnvReader.Read("AZURE_OPENAI_DEPLOYMENT");
    settings.AzureApiVersion = EnvReader.Read("AZURE_OPENAI_API_VERSION")
                             ?? settings.AzureApiVersion;
}

builder.Services.AddSingleton(settings);
```

`EnvReader.Read(key)` checks `.env` file first, then `Environment.GetEnvironmentVariable(key)`. This can reuse the same `ReadFromDotEnv` logic from `ApiKeyService`, extracted to a shared static helper.

### 1.5 — Register in DI

**File:** `src/BodyCam/MauiProgram.cs` — add to Services section:

```csharp
builder.Services.AddSingleton<IApiKeyService, ApiKeyService>();
```

### 1.5 — Update tests

- Add `ApiKeyServiceTests` (unit tests using a mock `ISecureStorage` or testing the interface contract)
- Update any tests that depended on `AppSettings.OpenAiApiKey`

---

## Verification

- [ ] Build succeeds on Windows
- [ ] All existing 50 unit tests still pass
- [ ] New `ApiKeyService` tests pass
- [ ] `IApiKeyService` resolves from DI

---

## Files Changed

| File | Action |
|------|--------|
| `.gitignore` | NEW — standard .NET ignores + `.env` |
| `.env.example` | NEW — committed template with placeholder key |
| `Services/IApiKeyService.cs` | NEW |
| `Services/ApiKeyService.cs` | NEW — SecureStorage + .env fallback + env var fallback |
| `AppSettings.cs` | MODIFY — add Realtime fields, remove `OpenAiApiKey` |
| `MauiProgram.cs` | MODIFY — register `IApiKeyService` |
| `Tests/Services/ApiKeyServiceTests.cs` | NEW |
| Existing tests referencing `OpenAiApiKey` | MODIFY if needed |
