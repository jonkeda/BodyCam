# M20 Phase 5 - Product Lookup UI

**Status:** NOT STARTED
**Depends on:** M20 Phase 2 (lookup tool/services), M18 camera capture and barcode decoding, transcript actions

---

## Goal

Allow product barcode lookup to be started from a button or other UI action, not
only from voice/Realtime tool routing. Register the lookup outcome in the
transcript, but keep the transcript compact: show only the product name as a
button. When the user taps that button, open a product detail page with the full
lookup result.

---

## User Flow

```
User taps Product / Barcode lookup in the UI
        |
        v
App captures a camera frame
        |
        v
ZXing scans for EAN-13 / UPC-A / Code 128
        |
        v
BarcodeLookupService looks up product info
        |
        +--> found:
        |       Transcript gets one button: [Product Name]
        |       User taps button -> ProductDetailPage opens
        |
        +--> not found:
                Transcript records a compact not-found result
```

---

## UI Entry Point

Add a product lookup button to the main action surface.

Recommended location:

```
Pages/Main/Views/ActionsDrawerView.xaml
```

Suggested button:

```xml
<Button AutomationId="ProductLookupButton"
        Text="Product"
        Command="{Binding ProductLookupCommand}"
        SemanticProperties.Description="Product lookup"
        SemanticProperties.Hint="Scans a product barcode and opens product details"
        Style="{StaticResource ActionButton}" />
```

The existing generic `Scan` button can remain for QR codes and raw barcode
content. The new `Product` button means "scan a product barcode and look up its
metadata".

Other UI surfaces can trigger the same command later, for example:

- a toolbar button on the camera preview
- a button on a generic barcode scan result
- a hardware/button mapping that invokes the same product lookup action

---

## Reusable Lookup Workflow

Avoid duplicating scan/lookup logic between `LookupBarcodeTool` and the UI.
Extract the shared work into a small service, for example:

```
Services/Barcode/IProductBarcodeLookupWorkflow.cs
Services/Barcode/ProductBarcodeLookupWorkflow.cs
```

```csharp
public interface IProductBarcodeLookupWorkflow
{
    Task<ProductBarcodeLookupResult> LookupAsync(
        Func<CancellationToken, Task<byte[]?>> captureFrame,
        string? barcode = null,
        CancellationToken ct = default);
}

public record ProductBarcodeLookupResult(
    bool Found,
    string? Barcode,
    ProductInfo? Product,
    string? Message,
    string? Error);
```

Workflow behavior:

1. If `barcode` is supplied, skip scanning.
2. Otherwise capture one frame.
3. Decode with `IQrCodeScanner`.
4. Accept only EAN-13, UPC-A, and Code 128.
5. Call `IBarcodeLookupService.LookupAsync`.
6. Return the full `ProductInfo` for UI detail display.

Then:

| Caller | Uses workflow for |
|--------|-------------------|
| `LookupBarcodeTool` | voice/Realtime product lookup |
| `MainViewModel.ProductLookupCommand` | button/UI product lookup |

---

## MainViewModel Command

Add a UI command:

```csharp
public ICommand ProductLookupCommand { get; }
```

Suggested flow:

```csharp
ProductLookupCommand = new AsyncRelayCommand(async () =>
{
    IsActionsDrawerExpanded = false;
    await LookupProductFromUiAsync();
});
```

`LookupProductFromUiAsync` should:

1. Add a temporary transcript entry such as `Product lookup...`.
2. Run `IProductBarcodeLookupWorkflow`.
3. Replace the temporary entry with the final compact transcript result.
4. If found, show only a product-name button.
5. If not found, show a short not-found transcript entry with the barcode when available.

---

## Transcript Result

Use the existing transcript action model, but keep text minimal.

Current transcript rendering always shows a label for `TranscriptEntry.Text`, so
Phase 5 should add one of these small UI affordances:

1. Add `TranscriptEntry.HasText` and hide the text label when `Text` is blank.
2. Or add `TranscriptEntry.IsActionsOnly` and render only the action row.

Recommended result shape:

```csharp
var entry = new TranscriptEntry
{
    Role = "Product",
    Text = string.Empty
};

entry.Actions.Add(new ContentAction
{
    Label = product.Name ?? product.Barcode,
    Icon = "",
    Command = new AsyncRelayCommand(() => OpenProductDetailAsync(product))
});
entry.NotifyActionsChanged();
Entries.Add(entry);
```

Visible transcript:

```
[Thai Peanut Noodle Kit]
```

The button label must be the product name only. If `Name` is missing, fall back
to the barcode.

Add product role support:

```csharp
public string AutomationId => Role switch
{
    "Product" => "TranscriptProductEntryLabel",
    ...
};
```

Role color can match scan/orange or use a distinct product color, but the
important behavior is that the product details are not dumped into the
transcript.

---

## Product Detail Page

Create a detail page for the full product result:

```
Pages/Products/ProductDetailPage.xaml
Pages/Products/ProductDetailPage.xaml.cs
ViewModels/ProductDetailViewModel.cs
```

Register the route in `MauiProgram`/routing setup:

```csharp
Routing.RegisterRoute("product-detail", typeof(ProductDetailPage));
```

Open it from the transcript button:

```csharp
await Shell.Current.GoToAsync(
    "product-detail",
    new Dictionary<string, object>
    {
        ["product"] = product
    });
```

`ProductDetailViewModel` can implement `IQueryAttributable` to receive the
`ProductInfo`.

### Detail Page Content

Show available fields in compact sections:

| Section | Fields |
|---------|--------|
| Header | image, name, brand, quantity |
| Identity | barcode, source, category, origins |
| Food | Nutri-Score, NOVA group, calories per 100g |
| Nutrition | fat, saturated fat, sugars, salt, proteins, fiber |
| Warnings | allergens, dietary labels |
| Ingredients | ingredients text |
| Pricing | lowest/highest price, currency |
| Description | general product description |

Only show sections with available data.

---

## Not Found / Error Behavior

| Scenario | Transcript behavior |
|----------|---------------------|
| Camera unavailable | `Product lookup: camera not available.` |
| No barcode detected | `Product lookup: no product barcode detected.` |
| QR/Data Matrix detected | `Product lookup: detected QR/Data Matrix; use Scan for QR codes.` |
| Product API returns no result | `Product not found: {barcode}` |
| API/network error | `Product lookup error: {short error}` |

Do not open the detail page for not-found/error results unless a future page for
raw barcode diagnostics is added.

---

## Tests

| Test | File |
|------|------|
| UI command captures frame and looks up product | `MainViewModelProductLookupTests.cs` |
| Product transcript entry contains one action | `MainViewModelProductLookupTests.cs` |
| Product transcript visible label is product name only | `MainViewModelProductLookupTests.cs` / UI test |
| Product action navigates to detail page | `MainViewModelProductLookupTests.cs` |
| Detail view model receives `ProductInfo` | `ProductDetailViewModelTests.cs` |
| Detail page hides empty sections | `ProductDetailViewModelTests.cs` |
| Product button exists in actions drawer | `QuickActionTests.cs` or `ProductLookupUiTests.cs` |
| Not-found result does not create detail button | `MainViewModelProductLookupTests.cs` |

---

## Exit Criteria

1. Product barcode lookup can be started from a button/UI action.
2. The UI path does not depend on Realtime model tool selection.
3. Successful lookup adds a transcript outcome.
4. The transcript shows only the product name as a button.
5. Tapping the product-name button opens a detail page.
6. The detail page shows all available `ProductInfo` fields in readable sections.
7. Not-found and error cases are recorded compactly in the transcript.
