using BodyCam.Models;
using BodyCam.Services.QrCode;

namespace BodyCam.Services.Barcode;

public enum ProductBarcodeLookupStatus
{
    Found,
    CameraUnavailable,
    NoBarcodeDetected,
    UnsupportedFormat,
    NotFound,
    Error,
}

public sealed record ProductBarcodeLookupResult(
    ProductBarcodeLookupStatus Status,
    string? Barcode,
    ProductInfo? Product,
    string Message,
    QrCodeFormat? Format = null,
    string? Error = null)
{
    public bool Found => Status == ProductBarcodeLookupStatus.Found && Product is not null;
}

public interface IProductBarcodeLookupWorkflow
{
    Task<ProductBarcodeLookupResult> LookupAsync(
        Func<CancellationToken, Task<byte[]?>> captureFrame,
        string? barcode = null,
        CancellationToken ct = default);
}

public sealed class ProductBarcodeLookupWorkflow : IProductBarcodeLookupWorkflow
{
    private readonly IQrCodeScanner _scanner;
    private readonly IBarcodeLookupService _lookup;

    private static readonly HashSet<QrCodeFormat> ProductFormats =
    [
        QrCodeFormat.Ean13,
        QrCodeFormat.UpcA,
        QrCodeFormat.Code128
    ];

    public ProductBarcodeLookupWorkflow(
        IQrCodeScanner scanner,
        IBarcodeLookupService lookup)
    {
        _scanner = scanner;
        _lookup = lookup;
    }

    public async Task<ProductBarcodeLookupResult> LookupAsync(
        Func<CancellationToken, Task<byte[]?>> captureFrame,
        string? barcode = null,
        CancellationToken ct = default)
    {
        var resolvedBarcode = Normalize(barcode);
        QrCodeFormat? format = null;

        if (resolvedBarcode is null)
        {
            var frame = await captureFrame(ct).ConfigureAwait(false);
            if (frame is null)
            {
                return new ProductBarcodeLookupResult(
                    ProductBarcodeLookupStatus.CameraUnavailable,
                    null,
                    null,
                    "Camera not available.");
            }

            var scan = await _scanner.ScanAsync(frame, ct).ConfigureAwait(false);
            if (scan is null)
            {
                return new ProductBarcodeLookupResult(
                    ProductBarcodeLookupStatus.NoBarcodeDetected,
                    null,
                    null,
                    "No barcode detected in the image.");
            }

            if (!ProductFormats.Contains(scan.Format))
            {
                return new ProductBarcodeLookupResult(
                    ProductBarcodeLookupStatus.UnsupportedFormat,
                    scan.Content,
                    null,
                    $"Detected a {scan.Format} code, not a product barcode. Use scan_qr_code for QR codes.",
                    scan.Format);
            }

            resolvedBarcode = scan.Content;
            format = scan.Format;
        }

        try
        {
            var product = await _lookup.LookupAsync(resolvedBarcode, ct).ConfigureAwait(false);
            if (product is null)
            {
                return new ProductBarcodeLookupResult(
                    ProductBarcodeLookupStatus.NotFound,
                    resolvedBarcode,
                    null,
                    $"Product not found in any database. Barcode: {resolvedBarcode}",
                    format);
            }

            return new ProductBarcodeLookupResult(
                ProductBarcodeLookupStatus.Found,
                resolvedBarcode,
                product,
                ProductDisplayName(product),
                format);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProductBarcodeLookupResult(
                ProductBarcodeLookupStatus.Error,
                resolvedBarcode,
                null,
                $"Product lookup error: {ex.Message}",
                format,
                ex.Message);
        }
    }

    public static string ProductDisplayName(ProductInfo product) =>
        !string.IsNullOrWhiteSpace(product.Name)
            ? product.Name.Trim()
            : product.Barcode;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
