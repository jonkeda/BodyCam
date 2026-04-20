using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Barcode;

/// <summary>
/// Queries Open Food Facts v2 API and sister databases (beauty, pet food, products)
/// in parallel, returning the first hit.
/// </summary>
internal sealed class OpenFoodFactsClient : IBarcodeApiClient
{
    private static readonly string[] Hosts =
    [
        "https://world.openfoodfacts.org",
        "https://world.openbeautyfacts.org",
        "https://world.openpetfoodfacts.org",
        "https://world.openproductsfacts.org"
    ];

    private readonly HttpClient _http;
    private readonly ILogger<OpenFoodFactsClient> _logger;

    public OpenFoodFactsClient(HttpClient http, ILogger<OpenFoodFactsClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var tasks = Hosts.Select(host => QueryAsync(host, barcode, cts.Token)).ToArray();

        try
        {
            var results = await Task.WhenAll(tasks);
            return results.FirstOrDefault(r => r is not null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Open Food Facts request timed out for {Barcode}", barcode);
            return null;
        }
    }

    private async Task<ProductInfo?> QueryAsync(string host, string barcode, CancellationToken ct)
    {
        try
        {
            var url = $"{host}/api/v2/product/{Uri.EscapeDataString(barcode)}.json";
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var root = doc.RootElement;
            if (!root.TryGetProperty("status", out var status) || status.GetInt32() != 1)
                return null;

            if (!root.TryGetProperty("product", out var p))
                return null;

            var nutriments = p.TryGetProperty("nutriments", out var n) ? n : (JsonElement?)null;

            return new ProductInfo
            {
                Barcode = barcode,
                Source = "openfoodfacts",
                Name = GetString(p, "product_name"),
                Brand = GetString(p, "brands"),
                Category = GetString(p, "categories"),
                Quantity = GetString(p, "quantity"),
                ImageUrl = GetString(p, "image_front_url"),
                IngredientsText = GetString(p, "ingredients_text"),
                Origins = GetString(p, "origins"),
                Allergens = GetString(p, "allergens"),
                Labels = GetString(p, "labels"),
                NutriScoreGrade = GetString(p, "nutriscore_grade"),
                NovaGroup = GetInt(p, "nova_group"),
                EnergyKcal = GetDouble(nutriments, "energy-kcal_100g"),
                Fat = GetDouble(nutriments, "fat_100g"),
                SaturatedFat = GetDouble(nutriments, "saturated-fat_100g"),
                Sugars = GetDouble(nutriments, "sugars_100g"),
                Salt = GetDouble(nutriments, "salt_100g"),
                Proteins = GetDouble(nutriments, "proteins_100g"),
                Fiber = GetDouble(nutriments, "fiber_100g"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Open Food Facts query failed for {Host}", host);
            return null;
        }
    }

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static double? GetDouble(JsonElement? el, string prop)
        => el.HasValue && el.Value.TryGetProperty(prop, out var v) && v.TryGetDouble(out var d) ? d : null;
}
