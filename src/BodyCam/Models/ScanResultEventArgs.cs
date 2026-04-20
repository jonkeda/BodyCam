using BodyCam.Services.QrCode;

namespace BodyCam.Models;

public record ScanResultEventArgs(
    IQrContentHandler Handler,
    Dictionary<string, object> Parsed,
    string RawContent);
