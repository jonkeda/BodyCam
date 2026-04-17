# M7 — OpenAI Authentication & Key Management — Research

**Date:** 2026-04-16
**Status:** COMPLETE

---

## 1. OpenAI API Authentication Methods

OpenAI provides **one** authentication mechanism for its API: **API keys** sent as HTTP Bearer tokens.

```
Authorization: Bearer sk-proj-...
```

There is **no OAuth flow**, no OIDC, no PKCE, and no delegated-auth mechanism for the OpenAI API directly. Every request must carry a valid API key.

### Key Types

| Type | Scope | Recommended |
|------|-------|-------------|
| **Project API key** (`sk-proj-...`) | Scoped to a single project | **YES — use this** |
| **User API key** (legacy) | Tied to user account, all orgs | NO — being deprecated |
| **Service account key** | For CI/CD and server-side workloads | NO — overkill for mobile app |

**Decision: Use project-scoped API keys.** They limit blast radius if compromised — only that project's quota is affected.

### Optional Headers

| Header | Purpose |
|--------|---------|
| `OpenAI-Organization: org-...` | Target a specific org (only if user belongs to multiple) |
| `OpenAI-Project: proj-...` | Target a specific project (only needed with legacy user keys) |

With project-scoped keys these headers are unnecessary — the key already implies the project.

---

## 2. Can We Use OAuth? — Analysis

### Direct OpenAI API: NO
OpenAI does not expose an OAuth authorization server. There is no `/authorize` endpoint, no token exchange, no refresh tokens. The API is API-key-only.

### Azure OpenAI Service: YES (via Microsoft Entra ID)
Azure OpenAI **does** support Microsoft Entra ID (formerly Azure AD) authentication as an alternative to API keys:

| Auth Method | How |
|-------------|-----|
| **API key** | `api-key: {key}` header |
| **Entra ID Bearer token** | `Authorization: Bearer {entra-token}` header |
| **Managed Identity** | System-assigned or user-assigned (Azure VMs, Functions, etc.) |

#### Entra ID flow for mobile apps
```
1. App registers in Azure AD as a native/public client
2. User signs in via MSAL (Microsoft Authentication Library)
3. App requests token for scope: https://cognitiveservices.azure.com/.default
4. Token sent as: Authorization: Bearer {entra-token}
5. Azure OpenAI validates token, checks RBAC role assignment
```

Required Azure RBAC role: **Cognitive Services OpenAI User** or **Cognitive Services OpenAI Contributor**.

#### MSAL.NET for MAUI
```xml
<PackageReference Include="Microsoft.Identity.Client" Version="4.*" />
```
MSAL.NET supports MAUI natively (Android, iOS, Windows). It handles:
- Interactive login (system browser / embedded WebView)
- Token caching (automatic, per-platform secure storage)
- Token refresh (automatic, silent)
- PKCE (automatic for public client flows)

### Verdict

| Approach | OAuth? | Pros | Cons |
|----------|--------|------|------|
| **OpenAI direct + API key** | NO | Simple, no Azure dependency, cheaper | Key in app, no per-user auth |
| **Azure OpenAI + Entra ID** | YES | Per-user auth, RBAC, no keys on device, audit trail | Azure subscription required, more complex setup, Azure pricing |
| **Own backend proxy** | YES (custom) | Full control, can add any auth (Google, GitHub, etc.) | Must host/maintain a server, adds latency |

---

## 3. Recommended Architecture: API Key with Secure On-Device Storage

For an open-source companion app where each user brings their own OpenAI key, the simplest and most practical approach is:

### User enters their own API key → stored securely on device → sent directly to OpenAI

This is the standard pattern for open-source AI apps (e.g., OpenCat, Chatbox, Enchanted, etc.).

### Why NOT a backend proxy?
- BodyCam is open-source — no hosted infrastructure
- Users don't want to trust a third-party server with their API calls
- Adds latency to a real-time voice pipeline (every audio chunk would need to hop through our server)
- We'd need to pay for hosting

### Why NOT Azure OpenAI + Entra ID?
- Requires an Azure subscription (friction for open-source users)
- Azure OpenAI has different model availability, pricing, and regional restrictions
- Adds Microsoft-specific infrastructure dependency
- Overkill for a personal device companion app

---

## 4. Secure Key Storage in .NET MAUI

### MAUI SecureStorage

```csharp
// Save
await SecureStorage.Default.SetAsync("openai_api_key", apiKey);

// Read
var apiKey = await SecureStorage.Default.GetAsync("openai_api_key");

// Remove
SecureStorage.Default.Remove("openai_api_key");
```

#### Platform backing stores

| Platform | Underlying Storage | Encrypted? |
|----------|-------------------|------------|
| **Android** | Android Keystore + EncryptedSharedPreferences | YES — hardware-backed on most devices |
| **iOS** | iOS Keychain | YES — hardware Secure Enclave |
| **Windows** | DPAPI (Data Protection API) | YES — tied to Windows user account |
| **macOS** | macOS Keychain | YES |

**All platforms encrypt at rest.** The key is never stored in plain text, never in app preferences, never in a file.

### Gotchas

1. **Android backup:** By default, Android may back up SharedPreferences to Google Drive. `SecureStorage` uses `EncryptedSharedPreferences` which is NOT backed up. But verify `android:allowBackup="false"` or exclude the encrypted prefs from backup rules.

2. **Windows uninstall:** DPAPI-protected data survives app uninstall. Not a security issue, but worth knowing.

3. **iOS app deletion:** Keychain items persist across app reinstalls by default on iOS. Can be confusing if user reinstalls expecting a clean slate. Use `kSecAttrAccessibleWhenUnlockedThisDeviceOnly` (MAUI default) to limit.

4. **Key rotation:** If user rotates their OpenAI key, they need to re-enter it in the app. Provide clear UI for this.

### SecureStorage vs Preferences

| | SecureStorage | Preferences |
|---|---|---|
| Encryption | YES (platform-native) | NO (plain text) |
| Use for API keys | **YES** | NEVER |
| Use for settings (model name, voice, etc.) | Overkill | **YES** |
| Performance | Slightly slower (crypto) | Fast |

---

## 5. Key Entry UX

### Options

| Approach | UX | Security |
|----------|-----|----------|
| **Settings page text field** | User pastes key, saved to SecureStorage | Standard — good enough |
| **QR code scan** | Generate QR on platform.openai.com, scan in app | Better UX for glasses users |
| **Deep link / intent** | `bodycam://set-key?key=sk-proj-...` | Convenient but key in URL history |
| **File import** | Drop a `.env` or `.json` file | Developer-friendly |

**Decision: Start with a Settings page text field (M6 scope).** It's simple, universally understood, and secure. QR scan could be a nice M5/M6 enhancement for the glasses use case.

### Key validation
Before saving, validate the key by making a lightweight API call:
```
GET https://api.openai.com/v1/models
Authorization: Bearer {key}
```
If it returns 200, the key is valid. If 401, show an error. This also confirms network connectivity.

---

## 6. WebSocket Auth (Realtime API)

The Realtime API WebSocket connection authenticates via query parameter OR header:

### Header auth (preferred — keeps key out of server logs)
```
GET wss://api.openai.com/v1/realtime?model=gpt-5.4-realtime
Authorization: Bearer sk-proj-...
```

### .NET ClientWebSocket implementation
```csharp
var ws = new ClientWebSocket();
ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
await ws.ConnectAsync(new Uri("wss://api.openai.com/v1/realtime?model=gpt-5.4-realtime"), ct);
```

`ClientWebSocket` supports custom headers — no need to put the key in the URL query string. This is more secure because:
- URLs get logged in proxy servers, load balancers, CDNs
- URLs appear in browser history (not relevant for us, but good practice)
- Headers are encrypted in TLS (same as URL, but not logged)

---

## 7. Security Considerations

### Threat Model for On-Device API Key

| Threat | Risk | Mitigation |
|--------|------|------------|
| Key extracted from device storage | LOW — SecureStorage uses hardware-backed encryption | Platform encryption (Keystore, Keychain, DPAPI) |
| Key intercepted in transit | LOW — all OpenAI API calls use TLS 1.2+ | Certificate pinning (optional, adds complexity) |
| Key exposed in logs/crash reports | MEDIUM | Never log the key. Mask in debug output: `sk-proj-...xxxx` |
| Key exposed via device backup | LOW | EncryptedSharedPreferences excluded from Android backup |
| Key exposed in memory dump | LOW | .NET manages memory; use `SecureString` only if paranoid (not worth it in MAUI) |
| Reverse engineering the app | LOW | Key isn't compiled in — user enters at runtime. No secret to extract |
| User shares their key accidentally | MEDIUM | Warn user in UI that the key is sensitive. Don't display it in full after entry |

### Best Practices to Implement

1. **Never log the full API key** — mask all but last 4 chars
2. **Clear key from memory** when no longer needed (set variable to null, let GC collect)
3. **Use SecureStorage exclusively** — never `Preferences`, never file system
4. **Validate on entry** — confirm key works before saving
5. **Show masked key in settings** — `sk-proj-...7f2a` — never display full key after initial entry
6. **Allow easy key removal** — user should be able to clear their key and re-enter
7. **No key in source code** — not even in `appsettings.json` defaults (use empty string)

---

## 8. Azure OpenAI as Optional Backend (Future)

For users who prefer Azure OpenAI (enterprise, compliance needs), we could add it as an **optional alternative** backend:

### Configuration
```json
{
  "OpenAI": {
    "Provider": "openai",          // "openai" or "azure"
    "ApiKey": "",                   // For direct OpenAI
    "AzureEndpoint": "",           // For Azure: https://{name}.openai.azure.com
    "AzureDeploymentName": "",     // For Azure: deployment name
    "UseEntraId": false            // For Azure: true = Entra ID, false = API key
  }
}
```

### Azure Entra ID flow (MSAL)
```csharp
// Azure OpenAI with MSAL token
var app = PublicClientApplicationBuilder
    .Create("{client-id}")
    .WithRedirectUri("bodycam://auth")
    .Build();

var result = await app.AcquireTokenInteractive(
    new[] { "https://cognitiveservices.azure.com/.default" }
).ExecuteAsync();

// Use result.AccessToken as Bearer token for Azure OpenAI
ws.Options.SetRequestHeader("Authorization", $"Bearer {result.AccessToken}");
```

**This is NOT for M1-M3.** It's a future enhancement for users who want enterprise-grade auth. Park it for M5 or M6.

---

## 9. Key Decisions Summary

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **API key auth (direct OpenAI)** as primary method | Simplest, no infra, open-source friendly |
| 2 | **User brings their own key** (BYOK) | No shared key, no backend, no billing surprises |
| 3 | **MAUI SecureStorage** for key persistence | Hardware-backed encryption on all platforms |
| 4 | **Settings page text field** for key entry | Simple UX, universally understood |
| 5 | **Header auth** for WebSocket (not query string) | Key stays out of URL/logs |
| 6 | **Validate key on entry** via GET /v1/models | Immediate feedback, confirms connectivity |
| 7 | **Azure OpenAI + Entra ID as optional future backend** | Enterprise users, compliance — NOT for initial release |
| 8 | **No backend proxy** | Open-source, no infra, no latency penalty |

---

## 10. Implementation Impact on Existing Code

### AppSettings changes needed
```csharp
public class AppSettings
{
    // Existing
    public string ChatModel { get; set; } = "gpt-5.4-realtime";
    public string VisionModel { get; set; } = "gpt-5.4-realtime";
    
    // Auth (loaded from SecureStorage at startup, NOT from appsettings.json)
    // ApiKey is never serialized to disk in plain text
}
```

### New service needed
```csharp
public interface IApiKeyService
{
    Task<string?> GetApiKeyAsync();
    Task SetApiKeyAsync(string apiKey);
    Task ClearApiKeyAsync();
    Task<bool> ValidateApiKeyAsync(string apiKey, CancellationToken ct = default);
}
```

### Settings page additions (M6 scope)
- API key entry field (password-masked)
- "Validate" button
- Status indicator (valid / invalid / not set)
- "Clear key" button
- Masked display of saved key

---

## 11. Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| OpenAI adds OAuth in the future | LOW (positive) — we'd just add it as an option | Monitor OpenAI changelog |
| User loses their API key | LOW — they regenerate on platform.openai.com | Link to key management page in settings |
| SecureStorage fails on old Android | LOW — EncryptedSharedPreferences requires API 23+ (Android 6) | MAUI already requires API 21+; verify on target devices |
| DPAPI compromise on shared Windows PC | MEDIUM — other users on same account could access | Warn user; suggest Windows Hello or separate user account |
| Rate limit hit due to shared project key | N/A — each user has their own key | BYOK model eliminates this |
