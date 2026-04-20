using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Barcode;

/// <summary>
/// Queries UPCitemdb free trial API for general product information.
/// Free tier: 100 requests/day, no API key required.
/// </summary>
internal sealed class UpcItemDbClient : IBarcodeApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<UpcItemDbClient> _logger;

    public UpcItemDbClient(HttpClient http, ILogger<UpcItemDbClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var content = JsonContent.Create(new { upc = barcode });
            using var response = await _http.PostAsync(
                "https://api.upcitemdb.com/prod/trial/lookup", content, cts.Token);

            if (!response.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);

            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var code) || code.GetString() != "OK")
                return null;

            if (!root.TryGetProperty("items", out var items)
                || items.GetArrayLength() == 0)
                return null;

            var item = items[0];

            decimal? lowestPrice = null, highestPrice = null;
            if (item.TryGetProperty("lowest_recorded_price", out var lp) && lp.TryGetDecimal(out var lpv))
                lowestPrice = lpv;
            if (item.TryGetProperty("highest_recorded_price", out var hp) && hp.TryGetDecimal(out var hpv))
                highestPrice = hpv;

            return new ProductInfo
            {
                Barcode = barcode,
                Source = "upcitemdb",
                Name = GetString(item, "title"),
                Brand = GetString(item, "brand"),
                Category = GetString(item, "category"),
                Description = GetString(item, "description"),
                Quantity = GetString(item, "weight"),
                LowestPrice = lowestPrice,
                HighestPrice = highestPrice,
                Currency = "USD",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "UPCitemdb lookup failed for {Barcode}", barcode);
            return null;
        }
    }

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
