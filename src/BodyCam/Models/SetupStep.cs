using BodyCam.Mvvm;

namespace BodyCam.Models;

public enum SetupStepKind { Permission, ApiKey }

public class SetupStep : ObservableObject
{
    // Immutable properties set at construction
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public required SetupStepKind Kind { get; init; }

    // Mutable status: "pending", "granted", "denied", "skipped"
    private string _status = "pending";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
