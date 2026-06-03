using System.Collections.ObjectModel;
using BodyCam.Mvvm;
using Microsoft.Maui.Controls;

namespace BodyCam.Models;

/// <summary>
/// A single chat transcript entry (AI or user message).
/// Text is observable so the UI updates as streaming deltas arrive.
/// </summary>
public class TranscriptEntry : ObservableObject
{
    private string _text = string.Empty;
    private bool _isThinking;
    private bool _isActionsOnly;

    public required string Role { get; init; }

    public bool IsThinking
    {
        get => _isThinking;
        set
        {
            if (SetProperty(ref _isThinking, value))
                OnPropertyChanged(nameof(AccessibleText));
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(FormattedText));
                if (!IsThinking)
                    OnPropertyChanged(nameof(AccessibleText));
            }
        }
    }

    public bool IsActionsOnly
    {
        get => _isActionsOnly;
        set
        {
            if (SetProperty(ref _isActionsOnly, value))
            {
                OnPropertyChanged(nameof(ShowText));
                OnPropertyChanged(nameof(AccessibleText));
            }
        }
    }

    public bool ShowText => !IsActionsOnly;

    public string DisplayText => $"{Role}: {Text}";
    public FormattedString FormattedText => TranscriptMarkdownFormatter.ToFormattedString(Role, Text, RoleColor);

    public string AutomationId => Role switch
    {
        "You" => "TranscriptYouEntryLabel",
        "AI" => "TranscriptAiEntryLabel",
        "Scan" => "TranscriptScanEntryLabel",
        "Product" => "TranscriptProductEntryLabel",
        _ => "TranscriptEntryLabel"
    };

    public string AccessibleText => IsThinking
        ? $"{Role} is thinking"
        : string.IsNullOrEmpty(Text)
            ? Role
            : $"{Role}: {TranscriptMarkdownFormatter.ToPlainText(Text)}";

    public ImageSource? Image { get; set; }
    public string? ImageCaption { get; set; }
    public bool HasImage => Image is not null;

    public ObservableCollection<ContentAction> Actions { get; } = [];
    public bool HasActions => Actions.Count > 0;

    public void NotifyActionsChanged() => OnPropertyChanged(nameof(HasActions));

    public Color RoleColor => (Role, IsLightTheme) switch
    {
        ("You", true)  => Color.FromArgb("#2E7D32"),
        ("You", false) => Color.FromArgb("#81C784"),
        ("AI", true)   => Color.FromArgb("#1565C0"),
        ("AI", false)  => Color.FromArgb("#64B5F6"),
        ("Scan", true) => Color.FromArgb("#E65100"),
        ("Scan", false) => Color.FromArgb("#FFB74D"),
        ("Product", true) => Color.FromArgb("#00695C"),
        ("Product", false) => Color.FromArgb("#80CBC4"),
        (_, true)      => Color.FromArgb("#616161"),
        (_, false)     => Color.FromArgb("#BDBDBD"),
    };

    private static bool IsLightTheme =>
        Application.Current?.RequestedTheme != AppTheme.Dark;
}
