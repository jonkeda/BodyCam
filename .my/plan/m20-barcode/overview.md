# M20 — Barcode Product Lookup

Scan product barcodes from the camera feed and look up product information via
open APIs. Read product name, brand, nutrition, and pricing aloud. User asks
follow-up questions via conversation.

Depends on M18 Phase 2 (barcode format decoding via ZXing.Net).

---

## API Research

### 1. Open Food Facts ⭐ PRIMARY — Food & Drink

**URL:** `GET https://world.openfoodfacts.org/api/v2/product/{barcode}.json`
**Auth:** None for reads — requires custom `User-Agent: BodyCam/1.0 (contact@example.com)`
**Rate limit:** 100 req/min per IP
**License:** Open Database License (ODbL) — free for any use
**Coverage:** 3M+ food products worldwide
**Format:** JSON

**Example:**
```
GET https://world.openfoodfacts.org/api/v2/product/737628064502.json
```

**Key response fields:**
```json
{
  "status": 1,
  "product": {
    "product_name": "Thai peanut noodle kit",
    "brands": "Simply Asia, Thai Kitchen",
    "categories": "Noodles, Rice Noodles",
    "quantity": "155 g",
    "allergens": "en:peanuts",
    "ingredients_text": "Rice Noodles (rice, water), seasoning packet...",
    "nutriments": {
      "energy-kcal_100g": 385,
      "fat_100g": 7.69,
      "sugars_100g": 13.46,
      "salt_100g": 0.72,
      "proteins_100g": 9.62,
      "fiber_100g": 1.9
    },
    "nutriscore_grade": "d",
    "nova_group": 4,
    "labels": "No gluten, Vegetarian, Vegan",
    "origins": "Thailand",
    "image_front_url": "https://images.openfoodfacts.org/..."
  }
}
```

**Relevant fields to extract:**
| Field | Use |
|-------|-----|
| `product_name` | Primary product name |
| `brands` | Brand / manufacturer |
| `categories` | Product category |
| `quantity` | Package size |
| `nutriments.*_100g` | Nutritional values per 100g |
| `nutriscore_grade` | Nutri-Score (a–e) |
| `nova_group` | NOVA processing level (1–4) |
| `allergens` | Allergen warnings |
| `labels` | Dietary labels (vegan, gluten-free, etc.) |
| `ingredients_text` | Full ingredients list |
| `image_front_url` | Product photo |

**Sister databases (same API, same format):**
- `world.openbeautyfacts.org` — cosmetics & personal care
- `world.openpetfoodfacts.org` — pet food
- `world.openproductsfacts.org` — other products

Strategy: query all four in parallel, return first hit.

---

### 2. UPCitemdb — General Products

**URL:** `POST https://api.upcitemdb.com/prod/trial/lookup` (free tier)
**URL:** `POST https://api.upcitemdb.com/prod/v1/lookup` (paid tier)
**Auth:** Free tier: no key needed. Paid: `user_key` + `key_type: 3scale` headers
**Rate limit:** Free: 100 req/day. Dev plan: 1,000 req/day ($10/mo)
**License:** Commercial — no redistribution
**Coverage:** 500M+ items — electronics, household, clothing, etc.
**Format:** JSON

**Request:**
```json
POST /prod/trial/lookup
Content-Type: application/json

{"upc": "4002293401102"}
```

**Key response fields:**
```json
{
  "code": "OK",
  "total": 1,
  "items": [{
    "ean": "4002293401102",
    "title": "Product Name",
    "brand": "Brand",
    "description": "Description text",
    "category": "Google taxonomy category",
    "weight": "500g",
    "lowest_recorded_price": 12.99,
    "highest_recorded_price": 19.99,
    "currency": "USD",
    "images": ["https://..."],
    "offers": [{
      "merchant": "Store Name",
      "price": 14.99,
      "link": "https://..."
    }]
  }]
}
```

**Best for:** Electronics, household goods, clothing — products NOT in Open Food Facts.

---

### 3. Open EAN/GTIN Database — European Products

**URL:** `GET http://opengtindb.org/?ean={ean}&cmd=query&queryid={userid}`
**Auth:** Requires free user registration for `queryid`
**Rate limit:** Not documented (fair use)
**License:** Open — free for non-commercial
**Coverage:** German/European focus
**Format:** Plain text (key=value pairs, NOT JSON)

**Response example:**
```
error=0
---
name=Natürliches Mineralwasser
detailname=Bad Vilbeler RIED Quelle
vendor=H. Kroner GmbH & CO. KG
maincat=Getränke, Alkohol
subcat=
contents=19
pack=1
origin=Deutschland
validated=25 %
---
```

**`contents` field is a bitmask:**
| Bit | Meaning |
|-----|---------|
| 1 | Lactose-free |
| 2 | Caffeine-free |
| 4 | Dietetic food |
| 8 | Gluten-free |
| 16 | Fructose-free |
| 32 | Organic (EU) |
| 64 | Fairtrade |
| 128 | Vegetarian |
| 256 | Vegan |
| 512 | Microplastic warning |

**Best for:** European products, especially German-market items. Falls back here
when Open Food Facts has no hit. Requires text parsing (not JSON).

---

## API Strategy

```
Scan barcode
    │
    ▼
┌─────────────────────────┐
│ Open Food Facts (v2 API)│  ← Try first (free, richest data for food)
│ + Beauty/Pet/Products   │
└────────┬────────────────┘
         │ status == 0 (not found)
         ▼
┌─────────────────────────┐
│ UPCitemdb (trial/lookup) │  ← Fallback for non-food products
└────────┬────────────────┘
         │ code == "NOT_FOUND"
         ▼
┌─────────────────────────┐
│ Open EAN/GTIN DB        │  ← Last resort, European products
└────────┬────────────────┘
         │ error != 0
         ▼
    "Product not found"
```

**Rationale:**
1. Open Food Facts first — free, unlimited reads, richest nutrition data
2. UPCitemdb second — broad general product coverage, pricing
3. Open GTIN last — European niche, non-JSON format adds complexity

---

## ProductInfo Model

Unified model across all API sources:

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
    public bool? IsVegan { get; init; }
    public bool? IsVegetarian { get; init; }
    public bool? IsGlutenFree { get; init; }

    // Pricing (from UPCitemdb)
    public decimal? LowestPrice { get; init; }
    public decimal? HighestPrice { get; init; }
    public string? Currency { get; init; }
}
```

---

## Architecture

```
Services/
  Barcode/
    IBarcodeLookupService.cs      ← Interface
    BarcodeLookupService.cs       ← Aggregator (chains APIs, caches)
    OpenFoodFactsClient.cs        ← OFF API v2 client
    UpcItemDbClient.cs            ← UPCitemdb trial/v1 client
    OpenGtinDbClient.cs           ← Open EAN/GTIN text parser
    ProductInfo.cs                ← Unified model
    BarcodeHistoryService.cs      ← Persist scanned products (Phase 3)

Tools/
    LookupBarcodeTool.cs          ← ITool — scan + lookup + speak
```

Each API client implements:
```csharp
internal interface IBarcodeApiClient
{
    Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct);
}
```

`BarcodeLookupService` chains them with in-memory cache (`ConcurrentDictionary`):
```csharp
public class BarcodeLookupService : IBarcodeLookupService
{
    private readonly IBarcodeApiClient[] _clients;
    private readonly ConcurrentDictionary<string, ProductInfo> _cache = new();

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

---

## HTTP Client Guidelines

- Custom `User-Agent: BodyCam/1.0` on all requests (OFF requires it)
- 5-second timeout per API call
- Retry once on transient failure (5xx), no retry on 4xx
- Use `IHttpClientFactory` via DI for proper `HttpClient` lifecycle
- AOT-safe: use `System.Text.Json` source generators for deserialization
- No API keys stored in code — UPCitemdb key (if paid) goes in `AppSettings`

---

## Spoken Response Template

When a product is found, the AI summarizes concisely for spoken output:

**Food product (from OFF):**
> "This is Thai Peanut Noodle Kit by Simply Asia. 155 grams. Nutri-Score D.
> 385 calories per 100 grams. Contains peanuts. Labeled vegan and gluten-free."

**General product (from UPCitemdb):**
> "This is a Samsung Galaxy S24 Ultra case by Spigen. Prices range from
> $12.99 to $19.99."

**Not found:**
> "I couldn't find this product in any database. The barcode is 1234567890123."

---

## Open Questions

1. **UPCitemdb paid plan?** Free tier is 100 req/day. Enough for personal use.
   Upgrade to Dev ($10/mo, 1000/day) if needed.
2. **Open GTIN DB worth the complexity?** Plain text parsing adds code. Could
   skip initially and add later if European coverage gaps appear.
3. **Offline cache?** Could cache lookups to SQLite for offline re-scan. Deferred
   to Phase 3.
