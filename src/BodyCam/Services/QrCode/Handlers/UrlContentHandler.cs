namespace BodyCam.Services.QrCode.Handlers;

public class UrlContentHandler : IQrContentHandler
{
    public string ContentType => "url";
    public string Icon => "\ud83d\udd17";
    public string DisplayName => "Website";

    public bool CanHandle(string content)
        => content.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || content.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object> Parse(string content)
        => new() { ["url"] = content };

    public string Summarize(Dictionary<string, object> parsed)
        => parsed.TryGetValue("url", out var url) ? url.ToString()! : "";

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["open in browser", "save to memory", "ignore"];
}
