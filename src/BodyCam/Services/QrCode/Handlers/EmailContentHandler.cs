namespace BodyCam.Services.QrCode.Handlers;

public class EmailContentHandler : IQrContentHandler
{
    public string ContentType => "email";
    public string Icon => "\u2709\ufe0f";
    public string DisplayName => "Email";

    public bool CanHandle(string content)
        => content.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object> Parse(string content)
        => new() { ["address"] = content[7..] };

    public string Summarize(Dictionary<string, object> parsed)
        => parsed.TryGetValue("address", out var a) ? a.ToString()! : "";

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["compose email", "save address", "ignore"];
}
