using System.Windows.Input;

namespace BodyCam.Models;

/// <summary>
/// A tappable action button shown inline on a transcript entry
/// when actionable content (URL, phone number, email, etc.) is detected.
/// </summary>
public class ContentAction
{
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public required ICommand Command { get; init; }
    public string? Url { get; init; }

    public string AccessibleLabel => $"{Label}: {Url ?? string.Empty}";
}
