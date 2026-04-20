namespace BodyCam.Services.Barcode;

public interface IBarcodeLookupService
{
    Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct = default);
}

public interface IBarcodeApiClient
{
    Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct);
}
