namespace BodyCam.Services.QrCode.Handlers;

public class WifiContentHandler : IQrContentHandler
{
    public string ContentType => "wifi";
    public string Icon => "\ud83d\udcf6";
    public string DisplayName => "WiFi Network";

    public bool CanHandle(string content)
        => content.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase);

    public Dictionary<string, object> Parse(string content)
    {
        var fields = new Dictionary<string, object>();
        // WIFI:S:<ssid>;T:<type>;P:<password>;H:<hidden>;;
        foreach (var part in content[5..].TrimEnd(';').Split(';'))
        {
            var sep = part.IndexOf(':');
            if (sep < 0) continue;
            var key = part[..sep];
            var val = part[(sep + 1)..];
            switch (key)
            {
                case "S": fields["ssid"] = val; break;
                case "T": fields["security"] = val; break;
                case "P": fields["password"] = val; break;
                case "H": fields["hidden"] = val == "true"; break;
            }
        }
        return fields;
    }

    public string Summarize(Dictionary<string, object> parsed)
    {
        var ssid = parsed.TryGetValue("ssid", out var s) ? s.ToString() : "Unknown";
        var security = parsed.TryGetValue("security", out var t) ? t.ToString() : "";
        return string.IsNullOrEmpty(security) ? ssid! : $"{ssid} ({security})";
    }

    public IReadOnlyList<string> SuggestedActions { get; } =
        ["connect to this network", "save password", "ignore"];
}
