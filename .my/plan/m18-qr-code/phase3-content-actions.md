# M18 Phase 3 — Content-Aware Actions

**Status:** NOT STARTED  
**Depends on:** M18 Phase 2

---

## Goal

Detect the type of QR code content and offer contextual actions. The AI announces what was found and asks the user what to do — the user responds by voice.

---

## Content Types + Actions

| Content Type | Detection | Offered Actions |
|-------------|-----------|-----------------|
| URL (`http://`, `https://`) | Prefix match | "Open in browser", "Save to memory", "Ignore" |
| WiFi (`WIFI:S:...`) | `WIFI:` prefix | "Connect to this network", "Save password", "Ignore" |
| vCard (`BEGIN:VCARD`) | `BEGIN:VCARD` prefix | "Save contact", "Read aloud", "Ignore" |
| Email (`mailto:`) | Prefix match | "Compose email", "Save address", "Ignore" |
| Phone (`tel:`) | Prefix match | "Call this number", "Save number", "Ignore" |
| Plain text | Fallback | "Save to memory", "Read aloud", "Ignore" |

## Implementation

### 1. QrContentClassifier

```
Services/QrCode/QrContentClassifier.cs
```

Static utility that classifies content string → `QrContentType` enum.

```csharp
public enum QrContentType { Url, Wifi, VCard, Email, Phone, Text }

public static QrContentType Classify(string content) => content switch
{
    _ when content.StartsWith("http://") || content.StartsWith("https://") => QrContentType.Url,
    _ when content.StartsWith("WIFI:") => QrContentType.Wifi,
    _ when content.StartsWith("BEGIN:VCARD") => QrContentType.VCard,
    _ when content.StartsWith("mailto:") => QrContentType.Email,
    _ when content.StartsWith("tel:") => QrContentType.Phone,
    _ => QrContentType.Text
};
```

### 2. Enrich ScanQrCodeTool Response

Add `content_type` and `suggested_actions` to the JSON returned to the AI:

```json
{
  "found": true,
  "content": "https://example.com/menu",
  "format": "QrCode",
  "content_type": "url",
  "suggested_actions": ["open in browser", "save to memory", "ignore"]
}
```

The AI's system prompt already instructs it to read results aloud and ask what to do. The `suggested_actions` field guides the AI's response.

### 3. Action Dispatch

Actions are handled by existing tools and services:
- **Open URL** → `Launcher.OpenAsync(uri)` (MAUI built-in)
- **Save to memory** → `SaveMemoryTool` (already exists)
- **Connect WiFi** → platform-specific (future, stub for now)
- **Save contact** → platform-specific (future, stub for now)

No new tools needed — the AI decides which existing tool to call based on user's voice response.

### 4. WiFi QR Parser

```
Services/QrCode/WifiQrParser.cs
```

Parse `WIFI:S:NetworkName;T:WPA;P:password;;` format into structured data for the AI to announce clearly.

### 5. Tests

| Test | File |
|------|------|
| Classify URL | `QrContentClassifierTests.cs` |
| Classify WiFi | `QrContentClassifierTests.cs` |
| Classify vCard | `QrContentClassifierTests.cs` |
| Classify plain text fallback | `QrContentClassifierTests.cs` |
| Tool response includes content_type | `ScanQrCodeToolTests.cs` |
| WiFi parser extracts SSID + password | `WifiQrParserTests.cs` |

---

## Exit Criteria

1. Tool response includes `content_type` and `suggested_actions`
2. AI reads content type-appropriately ("I found a WiFi network called...")
3. AI asks what to do → user responds → action dispatched
4. WiFi QR codes parsed into readable components
