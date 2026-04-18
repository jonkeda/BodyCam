using BodyCam.Services.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace BodyCam.Platforms.Windows.Input;

/// <summary>
/// Windows keyboard shortcut provider — maps F5-F9 to button events.
/// </summary>
public class KeyboardShortcutProvider : IButtonInputProvider
{
    public string DisplayName => "Keyboard Shortcuts";
    public string ProviderId => "keyboard";
    public bool IsAvailable => true;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;
    public event EventHandler? Disconnected;

    private static readonly Dictionary<VirtualKey, string> KeyMap = new()
    {
        [VirtualKey.F5] = "look",
        [VirtualKey.F6] = "photo",
        [VirtualKey.F7] = "read",
        [VirtualKey.F8] = "find",
        [VirtualKey.F9] = "toggle-session",
    };

    private UIElement? _content;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsActive) return Task.CompletedTask;

        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()
            ?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window?.Content is UIElement content)
        {
            _content = content;
            _content.KeyDown += OnKeyDown;
            _content.KeyUp += OnKeyUp;
        }

        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsActive) return Task.CompletedTask;

        if (_content is not null)
        {
            _content.KeyDown -= OnKeyDown;
            _content.KeyUp -= OnKeyUp;
            _content = null;
        }

        IsActive = false;
        return Task.CompletedTask;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!KeyMap.TryGetValue(e.Key, out var buttonId)) return;
        if (e.KeyStatus.WasKeyDown) return; // Ignore key repeat

        RawButtonEvent?.Invoke(this, new RawButtonEvent
        {
            ProviderId = ProviderId,
            ButtonId = buttonId,
            EventType = RawButtonEventType.ButtonDown,
            TimestampMs = Environment.TickCount64,
        });

        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!KeyMap.TryGetValue(e.Key, out var buttonId)) return;

        RawButtonEvent?.Invoke(this, new RawButtonEvent
        {
            ProviderId = ProviderId,
            ButtonId = buttonId,
            EventType = RawButtonEventType.ButtonUp,
            TimestampMs = Environment.TickCount64,
        });

        e.Handled = true;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
