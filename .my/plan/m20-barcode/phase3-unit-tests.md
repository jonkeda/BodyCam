# M20 Phase 3 — Unit Tests

**Status:** NOT STARTED  
**Depends on:** M20 Phase 2 (tool + DI)

---

## Goal

Comprehensive unit tests for all barcode API clients, the aggregator service,
and the lookup tool. Each client test uses pre-recorded JSON/text responses
to verify parsing without hitting real APIs.

---

## Wave 1: OpenFoodFactsClient Tests

```
BodyCam.Tests/Services/OpenFoodFactsClientTests.cs
```

| Test | Scenario |
|------|----------|
| `ReturnsProductInfo_WhenFoodProductFound` | Status 1, full product JSON → all fields mapped |
| `ReturnsNull_WhenProductNotFound` | Status 0 → null |
| `ReturnsNull_WhenResponseIs404` | HTTP 404 → null |
| `ExtractsNutrition_FromNutrimentsObject` | Verify all `*_100g` fields parse as doubles |
| `ExtractsNutriScore_AndNovaGroup` | Verify score string + integer |
| `HandlesTimeout_Gracefully` | Simulated timeout → null (no throw) |
| `HandlesMissingFields_Gracefully` | Product exists but optional fields missing → null fields |

Use `MockHttpMessageHandler` to return canned JSON responses.

---

## Wave 2: UpcItemDbClient Tests

```
BodyCam.Tests/Services/UpcItemDbClientTests.cs
```

| Test | Scenario |
|------|----------|
| `ReturnsProductInfo_WhenItemFound` | code=OK, items array → fields mapped |
| `ReturnsNull_WhenNotFound` | code=NOT_FOUND → null |
| `ExtractsPricing_FromItem` | lowest/highest recorded price → decimals |
| `ReturnsNull_OnHttpError` | 500 response → null |

---

## Wave 3: OpenGtinDbClient Tests

```
BodyCam.Tests/Services/OpenGtinDbClientTests.cs
```

| Test | Scenario |
|------|----------|
| `ReturnsProductInfo_WhenFound` | error=0, full text → fields parsed |
| `ReturnsNull_WhenErrorNonZero` | error=1 → null |
| `ParsesContentsBitmask_ForDietaryLabels` | contents=264 → "Gluten-free, Vegan" |
| `CombinesNameAndDetailName` | name + detailname → "name — detailname" |
| `HandlesEmptyFields` | Missing keys → null fields |

---

## Wave 4: BarcodeLookupService Tests

```
BodyCam.Tests/Services/BarcodeLookupServiceTests.cs
```

| Test | Scenario |
|------|----------|
| `ReturnsFirstClientResult` | Client 1 returns result → returns it, skips others |
| `FallsThrough_WhenFirstClientReturnsNull` | Client 1 null, Client 2 result → returns Client 2 |
| `ReturnsNull_WhenAllClientsReturnNull` | All null → null |
| `CachesResult_OnSubsequentCalls` | Second call same barcode → cached, no client calls |
| `DifferentBarcodes_NotCached` | Different barcode → client called again |

Use stub `IBarcodeApiClient` implementations.

---

## Wave 5: LookupBarcodeTool Tests

```
BodyCam.Tests/Tools/LookupBarcodeToolTests.cs
```

| Test | Scenario |
|------|----------|
| `ReturnsProductInfo_WhenBarcodeScanned` | Scanner returns EAN-13 → lookup succeeds → structured JSON |
| `ReturnsProductInfo_WhenBarcodeProvided` | Args has barcode string → skips scanner → lookup |
| `ReturnsNotFound_WhenLookupReturnsNull` | Lookup null → `{ found: false }` |
| `ReturnsNotFound_WhenNoBarcodeDetected` | Scanner returns null → fail message |
| `RejectQrCode_Format` | Scanner returns QR format → fail (wrong tool) |
| `ReturnsFail_WhenCameraUnavailable` | CaptureFrame null → camera error |

---

## Exit Criteria

1. All client tests pass with mock HTTP responses
2. BarcodeLookupService tests verify chaining + caching behavior
3. LookupBarcodeTool tests verify scan → lookup → response flow
4. No real API calls in unit tests
5. `dotnet test` — all tests pass including new ones
