# M18 Phase 4 - Content-Aware Actions

**Status:** IMPLEMENTED
**Depends on:** M18 Phase 3

---

## Goal

Classify decoded QR/barcode content and return enough structure for both:

1. The AI to announce the result naturally.
2. The UI to show a summary and action buttons.

---

## Current Content Types

| Content type | Handler | Match rule | Suggested actions |
|--------------|---------|------------|-------------------|
| URL | `UrlContentHandler` | starts with `http://` or `https://` | `open in browser`, `save to memory`, `ignore` |
| WiFi | `WifiContentHandler` | starts with `WIFI:` | `connect to this network`, `save password`, `ignore` |
| vCard | `VCardContentHandler` | starts with `BEGIN:VCARD` | `save contact`, `read aloud`, `ignore` |
| Email | `EmailContentHandler` | starts with `mailto:` | `compose email`, `save address`, `ignore` |
| Phone | `PhoneContentHandler` | starts with `tel:` | `call this number`, `save number`, `ignore` |
| Plain text | `PlainTextContentHandler` | catch-all fallback | `save to memory`, `read aloud`, `ignore` |

All handlers live in:

```
src/BodyCam/Services/QrCode/Handlers/
```

---

## Handler Contract

```
src/BodyCam/Services/QrCode/IQrContentHandler.cs
```

```csharp
public interface IQrContentHandler
{
    string ContentType { get; }
    string Icon { get; }
    string DisplayName { get; }
    bool CanHandle(string content);
    Dictionary<string, object> Parse(string content);
    string Summarize(Dictionary<string, object> parsed);
    IReadOnlyList<string> SuggestedActions { get; }
}
```

Notes:

| Member | Used by |
|--------|---------|
| `ContentType` | tool JSON, recall JSON, prompts |
| `Icon` | overlay and scan transcript entry |
| `DisplayName` | overlay title and transcript text |
| `Parse` | `details` payload |
| `Summarize` | transcript text and overlay summary |
| `SuggestedActions` | overlay buttons and AI choices |

---

## Resolver

```
src/BodyCam/Services/QrCode/QrContentResolver.cs
```

`QrContentResolver` iterates registered handlers in DI order and returns the first handler whose `CanHandle` returns true.

Registration order matters. `PlainTextContentHandler` must remain last because it accepts all content.

---

## Current Scan Result Enrichment

The original plan had `ScanQrCodeTool` enrich the response directly. Current code does this in `ScanCommand`, then `ScanQrCodeTool` returns the command data.

```
ScanCommand.ExecuteAsync
    -> IQrCodeScanner.ScanAsync
    -> QrCodeService.Add
    -> QrContentResolver.Resolve
    -> handler.Parse
    -> handler.Summarize
    -> CameraCommandResult.Data
```

Successful scan data includes:

```json
{
  "command": "scan",
  "mode": "FullAuto",
  "origin": "ActionsDrawer",
  "found": true,
  "content": "WIFI:S:CoffeeShop;T:WPA;P:latte123;;",
  "format": "QrCode",
  "content_type": "wifi",
  "suggested_actions": ["connect to this network", "save password", "ignore"],
  "details": {
    "ssid": "CoffeeShop",
    "security": "WPA",
    "password": "latte123"
  },
  "requires_confirmation": true
}
```

No-code scan data includes:

```json
{
  "found": false,
  "message": "No QR code or barcode detected in the image."
}
```

---

## DI Registration

```
src/BodyCam/ServiceExtensions.cs
```

Current registration lives in `AddQrCodeServices()`:

```csharp
services.AddSingleton<IQrCodeScanner, ZXingQrScanner>();
services.AddSingleton<QrCodeService>();
services.AddSingleton<IQrContentHandler, UrlContentHandler>();
services.AddSingleton<IQrContentHandler, WifiContentHandler>();
services.AddSingleton<IQrContentHandler, VCardContentHandler>();
services.AddSingleton<IQrContentHandler, EmailContentHandler>();
services.AddSingleton<IQrContentHandler, PhoneContentHandler>();
services.AddSingleton<IQrContentHandler, PlainTextContentHandler>();
services.AddSingleton<QrContentResolver>();
```

There is not a separate `AddQrContentHandlers()` method in current code.

---

## Action Dispatch

Suggested actions are labels and prompts, not direct command IDs. Current flow:

| User path | Behavior |
|-----------|----------|
| Tap overlay action while session is running | `MainViewModel.ExecuteScanAction` sends a text input to the active session |
| Tap overlay action while session is not running | overlay dismisses; no AI action is sent |
| Voice response after AI prompt | Realtime model chooses an existing tool/action |

External action confirmation is represented by:

```csharp
data["requires_confirmation"] = context.Settings.ConfirmExternalScanActions;
```

---

## Tests

| Area | Current test files |
|------|--------------------|
| Handler matching/parsing/summary | `src/BodyCam.Tests/Services/QrContentHandlerTests.cs` |
| Resolver fallback/order | `src/BodyCam.Tests/Services/QrContentResolverTests.cs` |
| Scan command enriched data | `src/BodyCam.Tests/Services/Camera/Commands/ScanCommandTests.cs` |
| Scan tool wrapper JSON | `src/BodyCam.Tests/Tools/ScanQrCodeToolTests.cs` |

---

## Exit Criteria

1. Each supported content type has one `IQrContentHandler`.
2. `QrContentResolver` picks the first matching handler.
3. Successful scan data includes `content_type`, `suggested_actions`, `details`, and `requires_confirmation`.
4. UI and voice flows consume the same enriched scan result.
5. Adding a new content type requires one handler class and one DI registration line.
