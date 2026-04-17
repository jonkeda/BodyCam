# Copilot Instructions — BodyCam

## MVVM

- All ViewModels inherit from `ViewModelBase` (which inherits `ObservableObject`).
- Use `SetProperty(ref _field, value)` for all property setters — never raise `PropertyChanged` manually.
- When a property change should notify dependent properties, call `OnPropertyChanged(nameof(Dependent))` after `SetProperty` returns `true`.
- Use `RelayCommand` for synchronous commands and `AsyncRelayCommand` for async commands (both in `BodyCam.Mvvm`).
- Do **not** use CommunityToolkit.Mvvm — we have our own lightweight implementations.

```csharp
// Correct
private string _name = string.Empty;
public string Name
{
    get => _name;
    set => SetProperty(ref _name, value);
}

// Wrong — don't do this
public string Name
{
    get => _name;
    set
    {
        _name = value;
        OnPropertyChanged();
    }
}
```

## Project Structure

- `Agents/` — Realtime API agents (voice, vision, conversation)
- `Converters/` — XAML value converters
- `Models/` — Data models and DTOs
- `Mvvm/` — Base MVVM infrastructure (ObservableObject, RelayCommand, etc.)
- `Orchestration/` — Agent orchestration and coordination
- `Services/` — Platform services and API clients
- `ViewModels/` — Page view models

## Testing

- Unit tests in `BodyCam.Tests`, integration tests in `BodyCam.IntegrationTests`.
- Real API tests (require keys) in `BodyCam.RealTests`.
- Use xUnit with FluentAssertions.
