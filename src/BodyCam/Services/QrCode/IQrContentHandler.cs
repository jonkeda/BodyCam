namespace BodyCam.Services.QrCode;

/// <summary>
/// Handles a specific type of QR code content.
/// Registered in DI — the resolver iterates all handlers to find a match.
/// </summary>
public interface IQrContentHandler
{
    /// <summary>Display name for the content type (e.g. "url", "wifi").</summary>
    string ContentType { get; }

    /// <summary>Emoji icon for the overlay card.</summary>
    string Icon { get; }

    /// <summary>Human-readable title (e.g. "WiFi Network", "Website").</summary>
    string DisplayName { get; }

    /// <summary>Returns true if this handler can process the given content.</summary>
    bool CanHandle(string content);

    /// <summary>
    /// Parse raw content into structured data the AI can announce clearly.
    /// Returns a dictionary of key-value pairs added to the tool response JSON.
    /// </summary>
    Dictionary<string, object> Parse(string content);

    /// <summary>
    /// Format parsed content into a short summary for the UI overlay.
    /// </summary>
    string Summarize(Dictionary<string, object> parsed);

    /// <summary>Actions the AI should offer the user.</summary>
    IReadOnlyList<string> SuggestedActions { get; }
}
