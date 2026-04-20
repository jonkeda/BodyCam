using System.Collections.Concurrent;

namespace BodyCam.Services.Barcode;

public class BarcodeLookupService : IBarcodeLookupService
{
    private readonly IBarcodeApiClient[] _clients;
    private readonly ConcurrentDictionary<string, ProductInfo> _cache = new();

    public BarcodeLookupService(IEnumerable<IBarcodeApiClient> clients)
    {
        _clients = clients.ToArray();
    }

    public async Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(barcode, out var cached))
            return cached;

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
