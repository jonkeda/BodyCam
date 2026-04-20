# M20 Phase 2 — Lookup Tool & DI Registration

**Status:** NOT STARTED  
**Depends on:** M20 Phase 1 (API clients + lookup service)

---

## Goal

Create a `LookupBarcodeTool` that scans a barcode from the camera, looks up
product info, and returns a structured response the AI reads aloud. Register all
barcode services in DI and update the system prompt.

---

## Wave 1: LookupBarcodeTool

```
Tools/LookupBarcodeTool.cs
```

```csharp
public class LookupBarcodeArgs
{
    [Description("Optional: barcode string if already known")]
    public string? Barcode { get; set; }
}

public class LookupBarcodeTool : ToolBase<LookupBarcodeArgs>
{
    public override string Name => "lookup_barcode";
    public override string Description =>
        "Scan a product barcode and look up product information including " +
        "name, brand, nutrition, allergens, and pricing.";
}
```

Execution flow:
1. If `args.Barcode` is null → capture frame → scan with `IQrCodeScanner`
2. If barcode format is not a product code (EAN-13, UPC-A, Code128) → fail
3. Call `IBarcodeLookupService.LookupAsync(barcode)`
4. If no result → return `{ found: false, barcode: "...", message: "Product not found" }`
5. If found → return structured dict with product fields

**Product code validation:** Only EAN-13, UPC-A, and Code-128 are product barcodes.
QR codes and DataMatrix are handled by `ScanQrCodeTool` instead.

**Spoken response template (via tool result):**

Food products include: name, brand, quantity, Nutri-Score, calories, allergens, labels.  
General products include: name, brand, price range.

---

## Wave 2: DI Registration

In `ServiceExtensions.cs`, add new extension method:

```csharp
public static IServiceCollection AddBarcodeServices(this IServiceCollection services)
{
    services.AddHttpClient<OpenFoodFactsClient>(client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BodyCam/1.0");
        client.Timeout = TimeSpan.FromSeconds(5);
    });
    services.AddHttpClient<UpcItemDbClient>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(5);
    });
    services.AddHttpClient<OpenGtinDbClient>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(5);
    });

    services.AddSingleton<IBarcodeApiClient>(sp => sp.GetRequiredService<OpenFoodFactsClient>());
    services.AddSingleton<IBarcodeApiClient>(sp => sp.GetRequiredService<UpcItemDbClient>());
    services.AddSingleton<IBarcodeApiClient>(sp => sp.GetRequiredService<OpenGtinDbClient>());
    services.AddSingleton<IBarcodeLookupService, BarcodeLookupService>();

    return services;
}
```

Call `.AddBarcodeServices()` in `MauiProgram.cs` before `.AddTools()`.

Register `LookupBarcodeTool` in `AddTools()`:
```csharp
services.AddSingleton<ITool, LookupBarcodeTool>();
```

---

## Wave 3: System Prompt Update

Add to `AppSettings.SystemInstructions`:
```
- When the user scans a product barcode or asks about a product, use
  the lookup_barcode tool to find product information.
- For food products, mention the name, brand, Nutri-Score, calories,
  and any allergens.
- For other products, mention the name, brand, and price range if available.
```

---

## Exit Criteria

1. `LookupBarcodeTool` scans barcode from camera if not provided as argument
2. Only product barcode formats (EAN-13, UPC-A, Code-128) are accepted
3. Tool returns structured JSON with product fields the AI can speak
4. DI registration uses `IHttpClientFactory` typed clients
5. API clients registered as `IBarcodeApiClient` in correct priority order
6. System prompt guides AI to use the tool appropriately
7. Build succeeds, existing tests pass
