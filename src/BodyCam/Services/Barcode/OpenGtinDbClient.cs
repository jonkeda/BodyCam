using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Barcode;

/// <summary>
/// Queries the Open EAN/GTIN database for European product information.
/// Returns plain-text key=value pairs (not JSON).
/// </summary>
internal sealed class OpenGtinDbClient : IBarcodeApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenGtinDbClient> _logger;

    public OpenGtinDbClient(HttpClient http, ILogger<OpenGtinDbClient> logger)
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

            var url = $"http://opengtindb.org/?ean={Uri.EscapeDataString(barcode)}&cmd=query&queryid=400000000";
            var text = await _http.GetStringAsync(url, cts.Token);

            return Parse(barcode, text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "OpenGTIN DB lookup failed for {Barcode}", barcode);
            return null;
        }
    }

    internal static ProductInfo? Parse(string barcode, string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line == "---") continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length > 0)
                fields[key] = value;
        }

        if (fields.TryGetValue("error", out var err) && err != "0")
            return null;

        var name = CombineName(
            fields.GetValueOrDefault("name"),
            fields.GetValueOrDefault("detailname"));

        if (name is null) return null;

        int contentsBits = 0;
        if (fields.TryGetValue("contents", out var c))
            int.TryParse(c, out contentsBits);

        return new ProductInfo
        {
            Barcode = barcode,
            Source = "opengtindb",
            Name = name,
            Brand = fields.GetValueOrDefault("vendor"),
            Category = fields.GetValueOrDefault("maincat"),
            Origins = fields.GetValueOrDefault("origin"),
            Labels = BuildLabels(contentsBits),
        };
    }

    private static string? CombineName(string? name, string? detail)
    {
        if (name is null && detail is null) return null;
        if (detail is null) return name;
        if (name is null) return detail;
        return $"{name} — {detail}";
    }

    internal static string? BuildLabels(int bits)
    {
        if (bits == 0) return null;

        var labels = new List<string>();
        if ((bits & 1) != 0) labels.Add("Lactose-free");
        if ((bits & 2) != 0) labels.Add("Caffeine-free");
        if ((bits & 4) != 0) labels.Add("Dietetic");
        if ((bits & 8) != 0) labels.Add("Gluten-free");
        if ((bits & 16) != 0) labels.Add("Fructose-free");
        if ((bits & 32) != 0) labels.Add("Organic");
        if ((bits & 64) != 0) labels.Add("Fairtrade");
        if ((bits & 128) != 0) labels.Add("Vegetarian");
        if ((bits & 256) != 0) labels.Add("Vegan");
        if ((bits & 512) != 0) labels.Add("Microplastic warning");

        return labels.Count > 0 ? string.Join(", ", labels) : null;
    }
}
