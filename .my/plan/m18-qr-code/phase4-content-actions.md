# M18 Phase 4 — Content-Aware Actions

**Status:** NOT STARTED  
**Depends on:** M18 Phase 3

---

## Goal

Detect the type of QR code content and offer contextual actions. The AI announces what was found and asks the user what to do — the user responds by voice.

Uses a **plugin pattern** (same as `ITool` / `ToolBase<T>`) — each content type is a separate class implementing `IQrContentHandler`, registered in DI. Adding a new content type = add one class + one DI line. No switches, no classifier.

---

## Content Types + Actions

| Content Type | Handler Class | Offered Actions |
|-------------|---------------|-----------------|
| URL (`http://`, `https://`) | `UrlContentHandler` | "Open in browser", "Save to memory", "Ignore" |
| WiFi (`WIFI:S:...`) | `WifiContentHandler` | "Connect to this network", "Save password", "Ignore" |
| vCard (`BEGIN:VCARD`) | `VCardContentHandler` | "Save contact", "Read aloud", "Ignore" |
| Email (`mailto:`) | `EmailContentHandler` | "Compose email", "Save address", "Ignore" |
| Phone (`tel:`) | `PhoneContentHandler` | "Call this number", "Save number", "Ignore" |
| Plain text | `PlainTextContentHandler` | "Save to memory", "Read aloud", "Ignore" |

---

## Implementation

### 1. `IQrContentHandler` Interface

```
Services/QrCode/IQrContentHandler.cs
```

```csharp
/// <summary>
/// Handles a specific type of QR code content.
/// Registered in DI — the resolver iterates all handlers to find a match.
/// </summary>
public interface IQrContentHandler
{
    /// <summary>Display name for the content type (e.g. "url", "wifi").</summary>
    string ContentType { get; }

    /// <summary>Emoji icon for the overlay card.</summary>
    string Icon { get; }

    /// <summary>Human-readable title (e.g. "WiFi Network", "Website").</summary>
    string DisplayName { get; }

    /// <summary>Returns true if this handler can process the given content.</summary>
    bool CanHandle(string content);

    /// <summary>
    /// Parse raw content into structured data the AI can announce clearly.
    /// Returns a dictionary of key-value pairs added to the tool response JSON.
    /// </summary>
    Dictionary<string, object> Parse(string content);

    /// <summary>
    /// Format parsed content into a short summary for the UI overlay.
    /// E.g. "CoffeeShop (WPA)" for WiFi, or the URL for links.
    /// </summary>
    string Summarize(Dictionary<string, object> parsed);

    /// <summary>Actions the AI should offer the user.</summary>
    IReadOnlyList<string> SuggestedActions { get; }
}
```

### 2. `QrContentResolver`

```
Services/QrCode/QrContentResolver.cs
```

Iterates registered handlers (order = registration order) and returns the first match. Falls back to `PlainTextContentHandler` which always matches.

```csharp
public class QrContentResolver
{
    private readonly IEnumerable<IQrContentHandler> _handlers;

    public QrContentResolver(IEnumerable<IQrContentHandler> handlers)
        => _handlers = handlers;

    public IQrContentHandler Resolve(string content)
        => _handlers.FirstOrDefault(h => h.CanHandle(content))
           ?? throw new InvalidOperationException(
               "No handler matched. Ensure PlainTextContentHandler is registered as a fallback.");
}
```

### 3. Handler Implementations

All handlers live in `Services/QrCode/Handlers/`.

#### `UrlContentHandler.cs`

```csharp
public class UrlContentHandler : IQrContentHandler
{
    public string ContentType => "url";
    public string Icon => "🔗";
    public string DisplayName => "Website";

    public bool CanHandle(string content)
        => content.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || content.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object> Parse(string content)
        => new() { ["url"] = content };

    public string Summarize(Dictionary<string, object> parsed)
        => parsed.TryGetValue("url", out var url) ? url.ToString()! : "";

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["open in browser", "save to memory", "ignore"];
}
```

#### `WifiContentHandler.cs`

Parses `WIFI:S:NetworkName;T:WPA;P:password;;` format.

```csharp
public class WifiContentHandler : IQrContentHandler
{
    public string ContentType => "wifi";
    public string Icon => "📶";
    public string DisplayName => "WiFi Network";

    public bool CanHandle(string content)
        => content.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object> Parse(string content)
    {
        var fields = new Dictionary<string, object>();
        // WIFI:S:<ssid>;T:<type>;P:<password>;H:<hidden>;;
        foreach (var part in content[5..].TrimEnd(';').Split(';'))
        {
            var sep = part.IndexOf(':');
            if (sep < 0) continue;
            var key = part[..sep];
            var val = part[(sep + 1)..];
            switch (key)
            {
                case "S": fields["ssid"] = val; break;
                case "T": fields["security"] = val; break;
                case "P": fields["password"] = val; break;
                case "H": fields["hidden"] = val == "true"; break;
            }
        }
        return fields;
    }

    public string Summarize(Dictionary<string, object> parsed)
    {
        var ssid = parsed.TryGetValue("ssid", out var s) ? s.ToString() : "Unknown";
        var security = parsed.TryGetValue("security", out var t) ? t.ToString() : "";
        return string.IsNullOrEmpty(security) ? ssid! : $"{ssid} ({security})";
    }

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["connect to this network", "save password", "ignore"];
}
```

#### `VCardContentHandler.cs`

```csharp
public class VCardContentHandler : IQrContentHandler
{
    public string ContentType => "vcard";
    public string Icon => "👤";
    public string DisplayName => "Contact";

    public bool CanHandle(string content)
        => content.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object> Parse(string content)
    {
        var fields = new Dictionary<string, object> { ["raw"] = content };
        // Extract common fields from vCard text
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("FN:")) fields["name"] = trimmed[3..];
            else if (trimmed.StartsWith("TEL:") || trimmed.StartsWith("TEL;")) fields["phone"] = trimmed[(trimmed.IndexOf(':') + 1)..];
            else if (trimmed.StartsWith("EMAIL:") || trimmed.StartsWith("EMAIL;")) fields["email"] = trimmed[(trimmed.IndexOf(':') + 1)..];
            else if (trimmed.StartsWith("ORG:")) fields["organization"] = trimmed[4..];
        }
        return fields;
    }

    public string Summarize(Dictionary<string, object> parsed)
    {
        var name = parsed.TryGetValue("name", out var n) ? n.ToString() : "Unknown";
        var org = parsed.TryGetValue("organization", out var o) ? $" — {o}" : "";
        return $"{name}{org}";
    }

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["save contact", "read aloud", "ignore"];
}
```

#### `EmailContentHandler.cs`

```csharp
public class EmailContentHandler : IQrContentHandler
{
    public string ContentType => "email";
    public string Icon => "✉️";
    public string DisplayName => "Email";

    public bool CanHandle(string content)
        => content.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object> Parse(string content)
        => new() { ["address"] = content[7..] };

    public string Summarize(Dictionary<string, object> parsed)
        => parsed.TryGetValue("address", out var a) ? a.ToString()! : "";

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["compose email", "save address", "ignore"];
}
```

#### `PhoneContentHandler.cs`

```csharp
public class PhoneContentHandler : IQrContentHandler
{
    public string ContentType => "phone";
    public string Icon => "📞";
    public string DisplayName => "Phone Number";

    public bool CanHandle(string content)
        => content.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object> Parse(string content)
        => new() { ["number"] = content[4..] };

    public string Summarize(Dictionary<string, object> parsed)
        => parsed.TryGetValue("number", out var n) ? n.ToString()! : "";

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["call this number", "save number", "ignore"];
}
```

#### `PlainTextContentHandler.cs`

Catch-all fallback — **must be registered last**.

```csharp
public class PlainTextContentHandler : IQrContentHandler
{
    public string ContentType => "text";
    public string Icon => "📝";
    public string DisplayName => "Text";

    public bool CanHandle(string content) => true;

    public Dictionary<string, object> Parse(string content)
        => new() { ["text"] = content };

    public string Summarize(Dictionary<string, object> parsed)
    {
        var text = parsed.TryGetValue("text", out var t) ? t.ToString()! : "";
        return text.Length > 80 ? text[..80] + "…" : text;
    }

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["save to memory", "read aloud", "ignore"];
}
```

### 4. Enrich ScanQrCodeTool Response

`ScanQrCodeTool` injects `QrContentResolver` and enriches the response:

```csharp
// In ScanQrCodeTool.ExecuteAsync, after successful scan:
var handler = _contentResolver.Resolve(result.Content);
var parsed = handler.Parse(result.Content);

return ToolResult.Success(new Dictionary<string, object>
{
    ["found"] = true,
    ["content"] = result.Content,
    ["format"] = result.Format.ToString(),
    ["content_type"] = handler.ContentType,
    ["suggested_actions"] = handler.SuggestedActions,
    ["details"] = parsed
});
```

Example response for a WiFi QR code:

```json
{
  "found": true,
  "content": "WIFI:S:CoffeeShop;T:WPA;P:latte123;;",
  "format": "QrCode",
  "content_type": "wifi",
  "suggested_actions": ["connect to this network", "save password", "ignore"],
  "details": {
    "ssid": "CoffeeShop",
    "security": "WPA",
    "password": "latte123"
  }
}
```

The AI's system prompt already instructs it to read results aloud and ask what to do. The `suggested_actions` + `details` guide the AI to announce it naturally ("I found a WiFi network called CoffeeShop, secured with WPA").

### 5. DI Registration

In `ServiceExtensions.cs`, add a new method:

```csharp
public static IServiceCollection AddQrContentHandlers(this IServiceCollection services)
{
    // Order matters — PlainTextContentHandler must be last (catch-all)
    services.AddSingleton<IQrContentHandler, UrlContentHandler>();
    services.AddSingleton<IQrContentHandler, WifiContentHandler>();
    services.AddSingleton<IQrContentHandler, VCardContentHandler>();
    services.AddSingleton<IQrContentHandler, EmailContentHandler>();
    services.AddSingleton<IQrContentHandler, PhoneContentHandler>();
    services.AddSingleton<IQrContentHandler, PlainTextContentHandler>(); // fallback — last
    services.AddSingleton<QrContentResolver>();

    return services;
}
```

Adding a new content type = create one class, add one DI line. Nothing else changes.

### 6. Action Dispatch

Actions are handled by existing tools and services — no new tools needed:
- **Open URL** → `Launcher.OpenAsync(uri)` (MAUI built-in)
- **Save to memory** → `SaveMemoryTool` (already exists)
- **Call number** → `MakePhoneCallTool` (already exists)
- **Connect WiFi** → platform-specific (future, stub for now)
- **Save contact** → platform-specific (future, stub for now)

The AI decides which existing tool to call based on the user's voice response.

### 7. Tests

| Test | File |
|------|------|
| Each handler `CanHandle` matches correct prefix | `QrContentHandlerTests.cs` |
| Each handler `CanHandle` rejects wrong content | `QrContentHandlerTests.cs` |
| `PlainTextContentHandler` matches everything | `QrContentHandlerTests.cs` |
| `WifiContentHandler.Parse` extracts SSID + password | `QrContentHandlerTests.cs` |
| `VCardContentHandler.Parse` extracts name + phone | `QrContentHandlerTests.cs` |
| `QrContentResolver` picks first matching handler | `QrContentResolverTests.cs` |
| `QrContentResolver` falls back to plain text | `QrContentResolverTests.cs` |
| Tool response includes `content_type` + `details` | `ScanQrCodeToolTests.cs` |

---

## File Summary

```
Services/QrCode/
    IQrContentHandler.cs
    QrContentResolver.cs
    Handlers/
        UrlContentHandler.cs
        WifiContentHandler.cs
        VCardContentHandler.cs
        EmailContentHandler.cs
        PhoneContentHandler.cs
        PlainTextContentHandler.cs
```

---

## Exit Criteria

1. Each content type is a separate `IQrContentHandler` class — no switches or classifiers
2. `QrContentResolver` iterates handlers and picks the first match
3. Tool response includes `content_type`, `suggested_actions`, and parsed `details`
4. AI reads content type-appropriately ("I found a WiFi network called...")
5. AI asks what to do → user responds → action dispatched via existing tools
6. Adding a new content type = one new class + one DI registration line
