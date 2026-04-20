# M20 Phase 1 — API Clients & Lookup Service

**Status:** NOT STARTED  
**Depends on:** M18 Phase 3 (barcode format decoding)

---

## Goal

Implement three barcode lookup API clients and a chaining aggregator service that
queries them in priority order: Open Food Facts → UPCitemdb → Open EAN/GTIN DB.
Return a unified `ProductInfo` model regardless of source.

---

## Wave 1: ProductInfo Model

### 1.1 `ProductInfo` Record

```
Services/Barcode/ProductInfo.cs
```

```csharp
public record ProductInfo
{
    public required string Barcode { get; init; }
    public required string Source { get; init; }        // "openfoodfacts", "upcitemdb", "opengtindb"
    public string? Name { get; init; }
    public string? Brand { get; init; }
    public string? Category { get; init; }
    public string? Description { get; init; }
    public string? Quantity { get; init; }
    public string? ImageUrl { get; init; }
    public string? IngredientsText { get; init; }
    public string? Origins { get; init; }

    // Nutrition (per 100g, from Open Food Facts)
    public double? EnergyKcal { get; init; }
    public double? Fat { get; init; }
    public double? SaturatedFat { get; init; }
    public double? Sugars { get; init; }
    public double? Salt { get; init; }
    public double? Proteins { get; init; }
    public double? Fiber { get; init; }

    // Scores
    public string? NutriScoreGrade { get; init; }       // a–e
    public int? NovaGroup { get; init; }                 // 1–4

    // Dietary
    public string? Allergens { get; init; }
    public string? Labels { get; init; }                 // "Vegan, Gluten-free"

    // Pricing (from UPCitemdb)
    public decimal? LowestPrice { get; init; }
    public decimal? HighestPrice { get; init; }
    public string? Currency { get; init; }
}
```

### 1.2 Interfaces

```
Services/Barcode/IBarcodeLookupService.cs
```

```csharp
public interface IBarcodeLookupService
{
    Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct = default);
}

internal interface IBarcodeApiClient
{
    Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct);
}
```

---

## Wave 2: Open Food Facts Client

```
Services/Barcode/OpenFoodFactsClient.cs
```

Primary lookup source. Queries all four sister databases in parallel:
- `world.openfoodfacts.org` — food & drink
- `world.openbeautyfacts.org` — cosmetics
- `world.openpetfoodfacts.org` — pet food
- `world.openproductsfacts.org` — general products

**API:** `GET /api/v2/product/{barcode}.json`  
**Auth:** None — requires `User-Agent: BodyCam/1.0`  
**Rate limit:** 100 req/min per IP  
**Timeout:** 5 seconds

Key implementation details:
- Query all four hosts via `Task.WhenAll`, return first non-null result
- Check `status == 1` and `product` property exists
- Extract nutrition from `nutriments` sub-object using `*_100g` keys
- Use `System.Text.Json.JsonDocument` for manual parsing (AOT-safe)
- Swallow individual host failures — log at Debug level

---

## Wave 3: UPCitemdb Client

```
Services/Barcode/UpcItemDbClient.cs
```

Fallback for non-food products (electronics, household, clothing).

**API:** `POST /prod/trial/lookup` with JSON body `{"upc": "{barcode}"}`  
**Auth:** None (free tier)  
**Rate limit:** 100 req/day  
**Timeout:** 5 seconds

Key implementation details:
- POST with `JsonContent.Create(new { upc = barcode })`
- Check `code == "OK"` and `items` array non-empty
- Extract first item's fields: `title`, `brand`, `category`, `description`, `weight`
- Extract pricing: `lowest_recorded_price`, `highest_recorded_price` (USD)
- Swallow failures — log at Debug level

---

## Wave 4: Open EAN/GTIN DB Client

```
Services/Barcode/OpenGtinDbClient.cs
```

Last-resort fallback, European product focus. Plain text response (NOT JSON).

**API:** `GET http://opengtindb.org/?ean={barcode}&cmd=query&queryid=400000000`  
**Auth:** None (public query ID `400000000`)  
**Rate limit:** Fair use  
**Timeout:** 5 seconds

Key implementation details:
- Response is newline-separated `key=value` pairs, blocks separated by `---`
- Parse `error` field first — `0` means success
- Extract: `name`, `detailname`, `vendor`, `maincat`, `subcat`, `origin`
- Parse `contents` bitmask for dietary flags:
  - Bit 1: Lactose-free, Bit 8: Gluten-free, Bit 128: Vegetarian, Bit 256: Vegan
- Build labels string from decoded bitmask
- Combine `name` + `detailname` into product name

---

## Wave 5: BarcodeLookupService (Aggregator)

```
Services/Barcode/BarcodeLookupService.cs
```

```csharp
public class BarcodeLookupService : IBarcodeLookupService
{
    private readonly IBarcodeApiClient[] _clients;
    private readonly ConcurrentDictionary<string, ProductInfo> _cache = new();

    public BarcodeLookupService(IEnumerable<IBarcodeApiClient> clients)
    {
        _clients = clients.ToArray();
    }

    public async Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct)
    {
        if (_cache.TryGetValue(barcode, out var cached)) return cached;

        foreach (var client in _clients)
        {
            var result = await client.LookupAsync(barcode, ct);
            if (result is not null)
            {
                _cache.TryAdd(barcode, result);
                return result;
            }
        }
        return null;
    }
}
```

- Chains clients in registration order (OFF → UPCitemdb → OpenGTIN)
- In-memory `ConcurrentDictionary` cache per barcode (no persistence)
- Short-circuits on first successful result

---

## Wave 6: HTTP Client Setup

All three clients use `IHttpClientFactory` via named HttpClients:
- `"OpenFoodFacts"` — `User-Agent: BodyCam/1.0`
- `"UpcItemDb"` — `Content-Type: application/json`
- `"OpenGtinDb"` — no special headers

Each configured with 5-second timeout in DI registration.

---

## Exit Criteria

1. `ProductInfo` record compiles with all fields from overview
2. `OpenFoodFactsClient` queries all 4 sister databases in parallel
3. `UpcItemDbClient` posts to free trial endpoint
4. `OpenGtinDbClient` parses plain-text response + bitmask
5. `BarcodeLookupService` chains in order, caches results
6. All clients handle timeouts and errors gracefully (no throws)
7. Unit tests cover each client's JSON/text parsing
