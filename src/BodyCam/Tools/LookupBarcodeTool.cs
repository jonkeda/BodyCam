using System.ComponentModel;
using BodyCam.Models;
using BodyCam.Services.Barcode;
using BodyCam.Services.QrCode;

namespace BodyCam.Tools;

public class LookupBarcodeArgs
{
    [Description("Optional: barcode string if already known from a previous scan")]
    public string? Barcode { get; set; }
}

public class LookupBarcodeTool : ToolBase<LookupBarcodeArgs>
{
    private readonly IQrCodeScanner _scanner;
    private readonly IBarcodeLookupService _lookup;

    public override string Name => "lookup_barcode";
    public override string Description =>
        "Scan a product barcode and look up product information including " +
        "name, brand, nutrition, allergens, and pricing. " +
        "Use when the user asks about a product or wants to know what something is.";

    public LookupBarcodeTool(IQrCodeScanner scanner, IBarcodeLookupService lookup)
    {
        _scanner = scanner;
        _lookup = lookup;
    }

    private static readonly HashSet<QrCodeFormat> ProductFormats =
    [
        QrCodeFormat.Ean13,
        QrCodeFormat.UpcA,
        QrCodeFormat.Code128
    ];

    protected override async Task<ToolResult> ExecuteAsync(
        LookupBarcodeArgs args, ToolContext context, CancellationToken ct)
    {
        string barcode;

        if (!string.IsNullOrWhiteSpace(args.Barcode))
        {
            barcode = args.Barcode;
        }
        else
        {
            var frame = await context.CaptureFrame(ct);
            if (frame is null)
                return ToolResult.Fail("Camera not available.");

            var scan = await _scanner.ScanAsync(frame, ct);
            if (scan is null)
                return ToolResult.Fail("No barcode detected in the image.");

            if (!ProductFormats.Contains(scan.Format))
                return ToolResult.Fail(
                    $"Detected a {scan.Format} code, not a product barcode. " +
                    "Use scan_qr_code for QR codes.");

            barcode = scan.Content;
        }

        var product = await _lookup.LookupAsync(barcode, ct);

        if (product is null)
            return ToolResult.Success(new
            {
                found = false,
                barcode,
                message = $"Product not found in any database. Barcode: {barcode}"
            });

        var result = new Dictionary<string, object?>
        {
            ["found"] = true,
            ["barcode"] = product.Barcode,
            ["source"] = product.Source,
            ["name"] = product.Name,
            ["brand"] = product.Brand,
            ["category"] = product.Category,
            ["quantity"] = product.Quantity,
        };

        // Food-specific fields
        if (product.NutriScoreGrade is not null)
            result["nutri_score"] = product.NutriScoreGrade.ToUpperInvariant();
        if (product.NovaGroup is not null)
            result["nova_group"] = product.NovaGroup;
        if (product.EnergyKcal is not null)
            result["calories_per_100g"] = product.EnergyKcal;
        if (product.Allergens is not null)
            result["allergens"] = product.Allergens;
        if (product.Labels is not null)
            result["labels"] = product.Labels;
        if (product.IngredientsText is not null)
            result["ingredients"] = product.IngredientsText;

        // Pricing fields
        if (product.LowestPrice is not null)
            result["price_range"] = $"{product.LowestPrice}–{product.HighestPrice} {product.Currency}";

        return ToolResult.Success(result);
    }
}
