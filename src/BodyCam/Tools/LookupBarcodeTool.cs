using System.ComponentModel;
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
    private readonly IProductBarcodeLookupWorkflow _workflow;

    public override string Name => "lookup_barcode";
    public override string Description =>
        "Scan a product barcode and look up product information including " +
        "name, brand, nutrition, allergens, and pricing. " +
        "Use when the user asks about a product or wants to know what something is.";

    public LookupBarcodeTool(IProductBarcodeLookupWorkflow workflow)
    {
        _workflow = workflow;
    }

    public LookupBarcodeTool(IQrCodeScanner scanner, IBarcodeLookupService lookup)
        : this(new ProductBarcodeLookupWorkflow(scanner, lookup))
    {
    }

    protected override async Task<ToolResult> ExecuteAsync(
        LookupBarcodeArgs args, ToolContext context, CancellationToken ct)
    {
        var lookup = await _workflow.LookupAsync(context.CaptureFrame, args.Barcode, ct);
        if (!lookup.Found)
        {
            if (lookup.Status is ProductBarcodeLookupStatus.NotFound)
            {
                return ToolResult.Success(new
                {
                    found = false,
                    barcode = lookup.Barcode,
                    message = lookup.Message
                });
            }

            return ToolResult.Fail(lookup.Message);
        }

        var product = lookup.Product!;
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
