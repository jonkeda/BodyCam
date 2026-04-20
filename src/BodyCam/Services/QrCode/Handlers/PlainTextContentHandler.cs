namespace BodyCam.Services.QrCode.Handlers;

public class PlainTextContentHandler : IQrContentHandler
{
    public string ContentType => "text";
    public string Icon => "\ud83d\udcdd";
    public string DisplayName => "Text";

    public bool CanHandle(string content) => true;

    public Dictionary<string, object> Parse(string content)
        => new() { ["text"] = content };

    public string Summarize(Dictionary<string, object> parsed)
    {
        var text = parsed.TryGetValue("text", out var t) ? t.ToString()! : "";
        return text.Length > 80 ? text[..80] + "\u2026" : text;
    }

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["save to memory", "read aloud", "ignore"];
}
