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
        set => SetProperty(ref _text, value);
    }
}
