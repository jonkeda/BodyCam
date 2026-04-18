# M17 Phase 3 — Connection UI & Auto-Fallback

Build the glasses connection management UI and wire automatic fallback when
glasses disconnect.

**Depends on:** Phase 1 (BT audio), Phase 2 (buttons + camera).

---

## Wave 1: GlassesPage XAML

New page in the app for managing glasses connections.

### GlassesViewModel

```csharp
// ViewModels/GlassesViewModel.cs
public class GlassesViewModel : ViewModelBase
{
    private readonly GlassesDeviceManager _glasses;

    private GlassesConnectionState _state;
    public GlassesConnectionState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    private GlassesBatteryInfo? _battery;
    public GlassesBatteryInfo? Battery
    {
        get => _battery;
        set => SetProperty(ref _battery, value);
    }

    private ObservableCollection<GlassesDeviceInfo> _devices = new();
    public ObservableCollection<GlassesDeviceInfo> Devices
    {
        get => _devices;
        set => SetProperty(ref _devices, value);
    }

    private GlassesDeviceInfo? _selectedDevice;
    public GlassesDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }

    public GlassesViewModel(GlassesDeviceManager glasses)
    {
        _glasses = glasses;
        _glasses.StateChanged += (_, state) =>
        {
            State = state;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(StatusText));
        };

        ScanCommand = new AsyncRelayCommand(ScanAsync);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => SelectedDevice != null);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
    }

    public bool IsConnected => State == GlassesConnectionState.Connected;

    public string StatusText => State switch
    {
        GlassesConnectionState.Disconnected => "Not connected",
        GlassesConnectionState.Scanning => "Scanning...",
        GlassesConnectionState.Connecting => "Connecting...",
        GlassesConnectionState.Connected => $"Connected — {Battery?.Percentage ?? 0}%",
        _ => "Unknown"
    };

    private async Task ScanAsync(CancellationToken ct)
    {
        State = GlassesConnectionState.Scanning;
        Devices.Clear();

        var found = await _glasses.ScanAsync(ct);
        foreach (var device in found)
            Devices.Add(device);

        State = GlassesConnectionState.Disconnected;
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        if (SelectedDevice == null) return;
        await _glasses.ConnectAsync(SelectedDevice, ct);
    }

    private async Task DisconnectAsync(CancellationToken ct)
    {
        await _glasses.DisconnectAsync(ct);
    }
}
```

### GlassesPage.xaml

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             Title="Glasses">

    <VerticalStackLayout Padding="16" Spacing="12">

        <!-- Status -->
        <Frame BackgroundColor="{Binding IsConnected, Converter={StaticResource BoolToColorConverter}}"
               Padding="12">
            <HorizontalStackLayout Spacing="8">
                <Label Text="●" FontSize="20"
                       TextColor="{Binding IsConnected, Converter={StaticResource BoolToColorConverter}}" />
                <Label Text="{Binding StatusText}" FontSize="18" VerticalOptions="Center" />
            </HorizontalStackLayout>
        </Frame>

        <!-- Battery -->
        <HorizontalStackLayout Spacing="8" IsVisible="{Binding IsConnected}">
            <Label Text="🔋" FontSize="20" />
            <ProgressBar Progress="{Binding Battery.Percentage, Converter={StaticResource PercentConverter}}"
                         WidthRequest="200" />
            <Label Text="{Binding Battery.Percentage, StringFormat='{0}%'}" />
        </HorizontalStackLayout>

        <!-- Scan button -->
        <Button Text="Scan for Glasses"
                Command="{Binding ScanCommand}"
                IsVisible="{Binding IsConnected, Converter={StaticResource InverseBoolConverter}}" />

        <!-- Device list -->
        <CollectionView ItemsSource="{Binding Devices}"
                        SelectionMode="Single"
                        SelectedItem="{Binding SelectedDevice}">
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Frame Padding="8" Margin="0,4">
                        <VerticalStackLayout>
                            <Label Text="{Binding Name}" FontSize="16" FontAttributes="Bold" />
                            <Label Text="{Binding Address}" FontSize="12" TextColor="Gray" />
                        </VerticalStackLayout>
                    </Frame>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>

        <!-- Connect / Disconnect -->
        <Button Text="Connect" Command="{Binding ConnectCommand}"
                IsVisible="{Binding IsConnected, Converter={StaticResource InverseBoolConverter}}" />
        <Button Text="Disconnect" Command="{Binding DisconnectCommand}"
                IsVisible="{Binding IsConnected}"
                BackgroundColor="Red" TextColor="White" />

    </VerticalStackLayout>
</ContentPage>
```

### Tests
```csharp
public class GlassesViewModelTests
{
    [Fact]
    public void StatusText_WhenDisconnected_ShowsNotConnected()
    {
        var manager = Substitute.For<GlassesDeviceManager>();
        var vm = new GlassesViewModel(manager);

        vm.StatusText.Should().Be("Not connected");
    }

    [Fact]
    public void IsConnected_WhenConnected_IsTrue()
    {
        var vm = CreateConnectedViewModel();
        vm.IsConnected.Should().BeTrue();
    }
}
```

### Verify
- [ ] GlassesPage renders
- [ ] Scan discovers devices
- [ ] Connect/Disconnect buttons work
- [ ] Battery level displays
- [ ] Status indicator updates
- [ ] Tests pass

---

## Wave 2: Auto-Fallback on Disconnect

When glasses disconnect unexpectedly, all providers should deactivate and the
managers should fall back to phone/platform providers automatically.

### GlassesDeviceManager Disconnect Handler

```csharp
// In GlassesDeviceManager — monitor all provider Disconnected events
private void WireDisconnectHandlers()
{
    foreach (var provider in _allGlassesProviders)
    {
        provider.Disconnected += OnProviderDisconnected;
    }
}

private async void OnProviderDisconnected(object? sender, EventArgs e)
{
    // Any glasses provider disconnected = glasses are gone
    State = GlassesConnectionState.Disconnected;
    StateChanged?.Invoke(this, State);

    // Stop all glasses providers
    foreach (var provider in _allGlassesProviders)
    {
        try { await provider.StopAsync(); } catch { }
    }

    // Managers automatically fall back to next available provider
    // AudioInputManager → PlatformMicProvider
    // AudioOutputManager → PlatformSpeakerProvider
    // CameraManager → PhoneCameraProvider
    // ButtonInputManager → KeyboardShortcutProvider (dev) or phone buttons

    // Notify user
    _notificationService?.ShowNotification(
        "Glasses Disconnected",
        "Switched to phone audio and camera.");

    // Attempt auto-reconnect after delay
    _ = AutoReconnectAsync();
}

private async Task AutoReconnectAsync()
{
    var delay = TimeSpan.FromSeconds(5);
    for (int attempt = 0; attempt < 3; attempt++)
    {
        await Task.Delay(delay);
        try
        {
            if (_lastDevice != null)
            {
                await ConnectAsync(_lastDevice, CancellationToken.None);
                return; // Reconnected
            }
        }
        catch
        {
            delay *= 2; // Exponential backoff
        }
    }
}
```

### Verify
- [ ] Disconnect triggers fallback to phone providers
- [ ] User gets notification
- [ ] Auto-reconnect attempts (3x with backoff)
- [ ] Manual reconnect still works
- [ ] No crash on unexpected disconnect

---

## Wave 3: MainPage Status Indicator

Add a small glasses status indicator to the main page so the user always knows
the connection state.

```csharp
// In MainViewModel — expose glasses state
public GlassesConnectionState GlassesState =>
    _glassesManager.State;

public string GlassesIcon => GlassesState switch
{
    GlassesConnectionState.Connected => "🕶️",
    GlassesConnectionState.Connecting => "⏳",
    _ => "👓" // disconnected, faded
};
```

```xml
<!-- In MainPage.xaml — status bar area -->
<Label Text="{Binding GlassesIcon}" FontSize="20"
       Opacity="{Binding GlassesState, Converter={StaticResource ConnectedOpacityConverter}}"
       ToolTipProperties.Text="{Binding GlassesStatusText}">
    <Label.GestureRecognizers>
        <TapGestureRecognizer Command="{Binding NavigateToGlassesCommand}" />
    </Label.GestureRecognizers>
</Label>
```

### Verify
- [ ] Icon shows on main page
- [ ] Opacity/color reflects connection state
- [ ] Tap navigates to GlassesPage
- [ ] State updates in real time

---

## Wave 4: Build & Integration Test

### Build Verification
```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj
```

### Manual Integration Test (with glasses)
1. Open GlassesPage → Scan → find glasses
2. Connect → verify BT audio routes (mic + speaker)
3. Speak → verify voice goes through glasses mic
4. Ask BodyCam a question → verify response plays on glasses speaker
5. Tap glasses button → verify action triggers
6. Double-tap → verify photo capture from glasses camera
7. Turn off glasses → verify fallback to phone
8. Turn on glasses → verify auto-reconnect

### Verify
- [ ] 0 build errors (both platforms)
- [ ] All unit tests pass
- [ ] End-to-end glasses conversation works
- [ ] Fallback to phone works
- [ ] Auto-reconnect works
- [ ] Battery monitoring works
- [ ] No regressions
