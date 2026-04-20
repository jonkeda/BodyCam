namespace BodyCam.Services.Barcode;

public record ProductInfo
{
    public required string Barcode { get; init; }
    public required string Source { get; init; }
    public string? Name { get; init; }
    public string? Brand { get; init; }
    public string? Category { get; init; }
    public string? Description { get; init; }
    public string? Quantity { get; init; }
    public string? ImageUrl { get; init; }
    public string? IngredientsText { get; init; }
    public string? Origins { get; init; }

    // Nutrition (per 100g)
    public double? EnergyKcal { get; init; }
    public double? Fat { get; init; }
    public double? SaturatedFat { get; init; }
    public double? Sugars { get; init; }
    public double? Salt { get; init; }
    public double? Proteins { get; init; }
    public double? Fiber { get; init; }

    // Scores
    public string? NutriScoreGrade { get; init; }
    public int? NovaGroup { get; init; }

    // Dietary
    public string? Allergens { get; init; }
    public string? Labels { get; init; }

    // Pricing (from UPCitemdb)
    public decimal? LowestPrice { get; init; }
    public decimal? HighestPrice { get; init; }
    public string? Currency { get; init; }
}
