namespace BodyCam.Services.QrCode;

public class QrContentResolver
{
    private readonly IEnumerable<IQrContentHandler> _handlers;

    public QrContentResolver(IEnumerable<IQrContentHandler> handlers)
        => _handlers = handlers;

    public IQrContentHandler Resolve(string content)
        => _handlers.FirstOrDefault(h => h.CanHandle(content))
           ?? throw new InvalidOperationException(
               "No handler matched. Ensure PlainTextContentHandler is registered as a fallback.");
}
