namespace BodyCam.Services.QrCode.Handlers;

public class PhoneContentHandler : IQrContentHandler
{
    public string ContentType => "phone";
    public string Icon => "\ud83d\udcde";
    public string DisplayName => "Phone Number";

    public bool CanHandle(string content)
        => content.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object> Parse(string content)
        => new() { ["number"] = content[4..] };

    public string Summarize(Dictionary<string, object> parsed)
        => parsed.TryGetValue("number", out var n) ? n.ToString()! : "";

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["call this number", "save number", "ignore"];
}
