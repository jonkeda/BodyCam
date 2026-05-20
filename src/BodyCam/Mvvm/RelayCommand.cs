using System.Windows.Input;

namespace BodyCam.Mvvm;

/// <summary>
/// Basic ICommand implementation for synchronous actions.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged()
    {
        try
        {
            if (MainThread.IsMainThread)
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            else
                MainThread.BeginInvokeOnMainThread(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
        catch
        {
            // Headless unit tests do not always have a MAUI dispatcher.
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
