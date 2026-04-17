using BodyCam.Mvvm;

namespace BodyCam.Models;

/// <summary>
/// A single chat transcript entry (AI or user message).
/// Text is observable so the UI updates as streaming deltas arrive.
/// </summary>
public class TranscriptEntry : ObservableObject
{
    private string _text = string.Empty;

    public required string Role { get; init; }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
                OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string DisplayText => $"{Role}: {Text}";

    public ImageSource? Image { get; set; }
    public string? ImageCaption { get; set; }
    public bool HasImage => Image is not null;

    public Color RoleColor => Role switch
    {
        "You" => Color.FromArgb("#4CAF50"),
        "AI" => Color.FromArgb("#2196F3"),
        _ => Color.FromArgb("#999999")
    };
}
