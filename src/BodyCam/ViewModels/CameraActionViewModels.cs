using System.Windows.Input;
using BodyCam.Mvvm;
using BodyCam.Services.Camera.Commands;

namespace BodyCam.ViewModels;

public sealed class CameraActionItemViewModel : ObservableObject
{
    private bool _isActive;
    private static readonly Color ActiveBg = Color.FromArgb("#512BD4");
    private static readonly Color ActiveText = Colors.White;
    private static readonly Color InactiveBg = Color.FromArgb("#F2F2F2");
    private static readonly Color InactiveDarkBg = Color.FromArgb("#2D2D2D");
    private static readonly Color InactiveText = Color.FromArgb("#222222");
    private static readonly Color InactiveDarkText = Color.FromArgb("#F5F5F5");

    public CameraActionItemViewModel(
        string actionId,
        string commandId,
        string label,
        IReadOnlyList<CameraActionVariantViewModel> variants,
        Func<CameraActionItemViewModel, Task> activateAsync)
    {
        ActionId = actionId;
        CommandId = commandId;
        Label = label;
        Variants = variants;
        ActivateCommand = new AsyncRelayCommand(() => activateAsync(this));
    }

    public string ActionId { get; }
    public string CommandId { get; }
    public string Label { get; }
    public IReadOnlyList<CameraActionVariantViewModel> Variants { get; }
    public ICommand ActivateCommand { get; }
    public string AutomationId => $"CameraActionButton_{NormalizeId(ActionId)}";
    public string SemanticDescription => Label;
    public string SemanticHint => $"Shows {Label} camera options";

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(ButtonColor));
                OnPropertyChanged(nameof(TextColor));
            }
        }
    }

    public Color ButtonColor => IsActive
        ? ActiveBg
        : (IsLightTheme ? InactiveBg : InactiveDarkBg);

    public Color TextColor => IsActive
        ? ActiveText
        : (IsLightTheme ? InactiveText : InactiveDarkText);

    private static bool IsLightTheme =>
        Application.Current?.RequestedTheme != AppTheme.Dark;

    internal static string NormalizeId(string value) =>
        new string(value
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');
}

public sealed class CameraActionVariantViewModel
{
    private static readonly Color PrimaryBg = Color.FromArgb("#512BD4");
    private static readonly Color PrimaryText = Colors.White;
    private static readonly Color InactiveBg = Color.FromArgb("#F2F2F2");
    private static readonly Color InactiveDarkBg = Color.FromArgb("#2D2D2D");
    private static readonly Color InactiveText = Color.FromArgb("#222222");
    private static readonly Color InactiveDarkText = Color.FromArgb("#F5F5F5");

    public CameraActionVariantViewModel(
        string actionId,
        string commandId,
        CameraActionVariantDefinition definition,
        Func<CameraActionVariantViewModel, Task> executeAsync)
    {
        ActionId = actionId;
        CommandId = commandId;
        Key = definition.Key;
        Label = definition.DisplayName;
        TranscriptText = definition.Text;
        Options = definition.Options;
        Query = definition.Query;
        IsDefault = definition.IsDefault;
        ExecuteCommand = new AsyncRelayCommand(() => executeAsync(this));
    }

    public string ActionId { get; }
    public string CommandId { get; }
    public string Key { get; }
    public string Label { get; }
    public string TranscriptText { get; }
    public object? Options { get; }
    public string? Query { get; }
    public bool IsDefault { get; }
    public ICommand ExecuteCommand { get; }
    public string AutomationId => $"CameraActionVariantButton_{CameraActionItemViewModel.NormalizeId(ActionId)}_{CameraActionItemViewModel.NormalizeId(Key)}";
    public string SemanticDescription => Label;
    public string SemanticHint => $"Captures a frame and runs {Label}";

    public Color ButtonColor => IsDefault
        ? PrimaryBg
        : (IsLightTheme ? InactiveBg : InactiveDarkBg);

    public Color TextColor => IsDefault
        ? PrimaryText
        : (IsLightTheme ? InactiveText : InactiveDarkText);

    public string Caption(string actionLabel) =>
        string.Equals(actionLabel, Label, StringComparison.OrdinalIgnoreCase)
            ? $"Captured frame for {actionLabel}"
            : $"Captured frame for {actionLabel} - {Label}";

    private static bool IsLightTheme =>
        Application.Current?.RequestedTheme != AppTheme.Dark;
}
