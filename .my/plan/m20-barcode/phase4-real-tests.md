# M20 Phase 4 — Real API Integration Tests

**Status:** NOT STARTED  
**Depends on:** Phase 1 (API clients), Phase 3 (unit tests)

---

## Goal

Verify each barcode API client and the `BarcodeLookupService` aggregator work
against the real APIs. Use well-known barcodes (Nutella, Coca-Cola) so the
results are stable and assertions can check specific fields.

These tests hit the network and may be rate-limited (UPCitemdb: 100 req/day).
They should be tagged `[Trait("Category", "RealAPI")]` and excluded from CI.

---

## Wave 1: BarcodeLookupRealTests

```
BodyCam.RealTests/Pipeline/BarcodeLookupRealTests.cs
```

### Test Barcodes

| Barcode | Product | Format | Expected Source |
|---------|---------|--------|-----------------|
| `3017620422003` | Nutella 400g | EAN-13 | openfoodfacts |
| `5449000000996` | Coca-Cola 330ml | EAN-13 | openfoodfacts |
| `4006381333931` | Haribo Gold-Bears 200g | EAN-13 | openfoodfacts |
| `0049000006346` | Coca-Cola 12oz (US) | UPC-A | openfoodfacts or upcitemdb |

### Tests

1. **OpenFoodFacts_Nutella_ReturnsProduct** — Query OFF directly, assert
   Name contains "Nutella", Brand contains "Ferrero", NutriScoreGrade is
   "e", EnergyKcal > 500.

2. **OpenFoodFacts_CocaCola_ReturnsProduct** — Assert Name contains
   "Coca-Cola" or "Coca Cola", Category is non-null.

3. **UpcItemDb_Nutella_ReturnsProduct** — Query UPCitemdb directly, assert
   Name is non-null, Source is "upcitemdb".

4. **OpenGtinDb_Haribo_ReturnsProduct** — Query OpenGTIN directly, assert
   Name contains "Haribo" or "Gold", Source is "opengtindb".

5. **BarcodeLookupService_Nutella_ReturnsFromFirstClient** — Wire all three
   clients into the aggregator, assert result Source is "openfoodfacts"
   (first in priority chain).

6. **BarcodeLookupService_CachesResult** — Lookup same barcode twice, second
   call should return instantly (< 50ms).

7. **UnknownBarcode_ReturnsNull** — Use a fake barcode like `0000000000000`,
   all three clients should return null.

### Setup

Each test creates its own `HttpClient` (not from DI) with a 10-second timeout
and the required `User-Agent` header for OFF. No `.env` file needed — these
APIs are free and keyless.

```csharp
private static HttpClient CreateHttpClient()
{
    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("BodyCam/1.0 (RealTests)");
    return http;
}
```

### Assertions

Use FluentAssertions. Be lenient on exact field values (products change) but
assert structural correctness:
- `result.Should().NotBeNull()`
- `result!.Barcode.Should().Be(barcode)`
- `result.Source.Should().Be("openfoodfacts")`
- `result.Name.Should().NotBeNullOrWhiteSpace()`

---

## Verification

- [ ] All 7 tests pass with network access
- [ ] Tests are skipped/ignored gracefully when offline
- [ ] `dotnet build` — 0 errors
- [ ] Tests use `[Trait("Category", "RealAPI")]`
