namespace BodyCam.Services.QrCode.Handlers;

public class VCardContentHandler : IQrContentHandler
{
    public string ContentType => "vcard";
    public string Icon => "\ud83d\udc64";
    public string DisplayName => "Contact";

    public bool CanHandle(string content)
        => content.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object> Parse(string content)
    {
        var fields = new Dictionary<string, object> { ["raw"] = content };
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("FN:"))
                fields["name"] = trimmed[3..];
            else if (trimmed.StartsWith("TEL:") || trimmed.StartsWith("TEL;"))
                fields["phone"] = trimmed[(trimmed.IndexOf(':') + 1)..];
            else if (trimmed.StartsWith("EMAIL:") || trimmed.StartsWith("EMAIL;"))
                fields["email"] = trimmed[(trimmed.IndexOf(':') + 1)..];
            else if (trimmed.StartsWith("ORG:"))
                fields["organization"] = trimmed[4..];
        }
        return fields;
    }

    public string Summarize(Dictionary<string, object> parsed)
    {
        var name = parsed.TryGetValue("name", out var n) ? n.ToString() : "Unknown";
        var org = parsed.TryGetValue("organization", out var o) ? $" \u2014 {o}" : "";
        return $"{name}{org}";
    }

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["save contact", "read aloud", "ignore"];
}
