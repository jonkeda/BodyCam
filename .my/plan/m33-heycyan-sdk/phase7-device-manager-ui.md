# M33 Phase 7 — `GlassesDeviceManager` Wiring & UI

Wire the HeyCyan providers (P1–P5) into the M17 `GlassesDeviceManager` base,
build the connection UI (scan → list → connect → status), surface a battery
indicator on the app shell, and verify end-to-end auto-fallback against real
HeyCyan hardware using the M17 exit-criteria checklist.

**Depends on:** M33 Phase 1 (Android session), Phase 2 (camera), Phase 3
(audio), Phase 4 (button), Phase 5 (recorded media — optional), Phase 6 (iOS
session), and M17 Phase 1 (`GlassesDeviceManager` base + auto-fallback).

> **Scope note:** auto-fallback is **already implemented** by the M17
> `GlassesDeviceManager` and the M11/M12/M13/M14 manager classes. Phase 7
> only **wires HeyCyan into them** and **verifies** the behavior end-to-end.

---

## Wave 1: `HeyCyanGlassesDeviceManager`

Concrete `GlassesDeviceManager` subclass that aggregates the HeyCyan session
and providers, exposes scan/connect/disconnect, and projects session events
onto the existing `GlassesDeviceManager` observables.

```csharp
// Services/Glasses/HeyCyan/HeyCyanGlassesDeviceManager.cs
namespace BodyCam.Services.Glasses.HeyCyan;

public sealed class HeyCyanGlassesDeviceManager : GlassesDeviceManager
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly HeyCyanCameraProvider _camera;
    private readonly HeyCyanAudioInputProvider _mic;
    private readonly HeyCyanAudioOutputProvider _speaker;
    private readonly HeyCyanButtonProvider _button;
    private readonly HeyCyanMediaTransfer? _media; // P5 optional
    private readonly ILogger<HeyCyanGlassesDeviceManager> _log;

    private HeyCyanDeviceInfo? _lastDevice;

    public HeyCyanGlassesDeviceManager(
        IHeyCyanGlassesSession session,
        HeyCyanCameraProvider camera,
        HeyCyanAudioInputProvider mic,
        HeyCyanAudioOutputProvider speaker,
        HeyCyanButtonProvider button,
        HeyCyanMediaTransfer? media,
        ILogger<HeyCyanGlassesDeviceManager> log)
        : base(camera, mic, speaker, button)
    {
        _session = session;
        _camera = camera;
        _mic = mic;
        _speaker = speaker;
        _button = button;
        _media = media;
        _log = log;

        _session.StateChanged       += OnSessionStateChanged;
        _session.BatteryUpdated     += OnBattery;
        _session.MediaCountUpdated  += OnMediaCount;
        _session.ButtonPressed      += OnButton;
    }

    // Status surface consumed by GlassesViewModel ----------------------------
    public HeyCyanBattery?      Battery     { get; private set; }
    public HeyCyanVersionInfo?  Version     { get; private set; }
    public HeyCyanMediaCount?   MediaCount  { get; private set; }
    public string?              MacAddress  => Version?.MacAddress;

    public event EventHandler? StatusChanged;

    // Scan / connect / disconnect --------------------------------------------
    public async Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(
        TimeSpan timeout, CancellationToken ct)
        => await _session.ScanAsync(timeout, ct);

    public async Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
    {
        _lastDevice = device;
        await _session.ConnectAsync(device, ct);

        Version    = await _session.GetVersionAsync(ct);
        Battery    = await _session.GetBatteryAsync(ct);
        await _session.SyncTimeAsync(ct);

        // Activate providers — managers auto-prefer them via priority.
        await _camera.StartAsync(ct);
        await _mic.StartAsync(ct);
        await _speaker.StartAsync(ct);
        await _button.StartAsync(ct);

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        try { await _session.DisconnectAsync(ct); }
        finally { /* base + managers handle fallback via Disconnected event */ }
    }

    // Session → manager projection -------------------------------------------
    private void OnSessionStateChanged(object? s, HeyCyanState state)
    {
        State = state switch
        {
            HeyCyanState.Disconnected   => GlassesConnectionState.Disconnected,
            HeyCyanState.Scanning       => GlassesConnectionState.Scanning,
            HeyCyanState.Connecting     => GlassesConnectionState.Connecting,
            HeyCyanState.Connected      => GlassesConnectionState.Connected,
            HeyCyanState.TransferMode   => GlassesConnectionState.Connected,
            _                           => GlassesConnectionState.Disconnected,
        };
        RaiseStateChanged();
    }

    private void OnBattery(object? s, HeyCyanBattery b)
    {
        Battery = b;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaCount(object? s, HeyCyanMediaCount c)
    {
        MediaCount = c;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnButton(object? s, HeyCyanButtonEvent e)
        => _button.RaiseGesture(e.Gesture); // forwards into M14 ButtonInputManager
}
```

DI registration (Android + iOS):

```csharp
// MauiProgram.cs (per-platform partial)
services.AddSingleton<IHeyCyanGlassesSession, AndroidHeyCyanGlassesSession>();
services.AddSingleton<HeyCyanCameraProvider>();
services.AddSingleton<HeyCyanAudioInputProvider>();
services.AddSingleton<HeyCyanAudioOutputProvider>();
services.AddSingleton<HeyCyanButtonProvider>();
services.AddSingleton<HeyCyanMediaTransfer>();
services.AddSingleton<GlassesDeviceManager, HeyCyanGlassesDeviceManager>();
services.AddSingleton(sp => (HeyCyanGlassesDeviceManager)sp.GetRequiredService<GlassesDeviceManager>());
```

### Verify
- [ ] `HeyCyanGlassesDeviceManager` resolves from DI on Android + iOS
- [ ] `ScanAsync` returns BLE device list from `IHeyCyanGlassesSession`
- [ ] `ConnectAsync` populates `Battery` / `Version` / `MediaCount`
- [ ] `StateChanged` fires on every QCSDK state transition
- [ ] Unit tests with a fake `IHeyCyanGlassesSession` pass

---

## Wave 2: `GlassesPage.xaml` (scan / list / connect / status)

Reuse the M17 Phase 3 `GlassesViewModel` shape; extend with HeyCyan-specific
status bindings (MAC, firmware, media counts).

### `GlassesViewModel` additions

```csharp
public sealed class GlassesViewModel : ViewModelBase
{
    private readonly HeyCyanGlassesDeviceManager _glasses;

    public GlassesViewModel(HeyCyanGlassesDeviceManager glasses)
    {
        _glasses = glasses;
        _glasses.StateChanged   += (_, _) => RefreshAll();
        _glasses.StatusChanged  += (_, _) => RefreshAll();

        ScanCommand       = new AsyncRelayCommand(ScanAsync);
        ConnectCommand    = new AsyncRelayCommand(ConnectAsync, () => SelectedDevice is not null);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
    }

    public ObservableCollection<HeyCyanDeviceInfo> Devices { get; } = new();

    private HeyCyanDeviceInfo? _selectedDevice;
    public HeyCyanDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set { if (SetProperty(ref _selectedDevice, value)) ConnectCommand.NotifyCanExecuteChanged(); }
    }

    public bool   IsConnected   => _glasses.State == GlassesConnectionState.Connected;
    public int    BatteryPct    => _glasses.Battery?.Percentage ?? 0;
    public bool   IsCharging    => _glasses.Battery?.IsCharging ?? false;
    public string Mac           => _glasses.MacAddress ?? "—";
    public string Firmware      => _glasses.Version?.Firmware ?? "—";
    public string Hardware      => _glasses.Version?.Hardware ?? "—";
    public int    Photos        => _glasses.MediaCount?.Photos ?? 0;
    public int    Videos        => _glasses.MediaCount?.Videos ?? 0;
    public int    AudioFiles    => _glasses.MediaCount?.AudioFiles ?? 0;

    public string StatusText => _glasses.State switch
    {
        GlassesConnectionState.Disconnected => "Not connected",
        GlassesConnectionState.Scanning     => "Scanning…",
        GlassesConnectionState.Connecting   => "Connecting…",
        GlassesConnectionState.Connected    => $"Connected — {BatteryPct}%{(IsCharging ? " ⚡" : "")}",
        _ => "Unknown",
    };

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }

    private async Task ScanAsync(CancellationToken ct)
    {
        Devices.Clear();
        var found = await _glasses.ScanAsync(TimeSpan.FromSeconds(8), ct);
        foreach (var d in found) Devices.Add(d);
    }

    private Task ConnectAsync(CancellationToken ct)
        => SelectedDevice is null ? Task.CompletedTask : _glasses.ConnectAsync(SelectedDevice, ct);

    private Task DisconnectAsync(CancellationToken ct) => _glasses.DisconnectAsync(ct);

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(BatteryPct));
        OnPropertyChanged(nameof(IsCharging));
        OnPropertyChanged(nameof(Mac));
        OnPropertyChanged(nameof(Firmware));
        OnPropertyChanged(nameof(Hardware));
        OnPropertyChanged(nameof(Photos));
        OnPropertyChanged(nameof(Videos));
        OnPropertyChanged(nameof(AudioFiles));
        OnPropertyChanged(nameof(StatusText));
        DisconnectCommand.NotifyCanExecuteChanged();
    }
}
```

### `GlassesPage.xaml`

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             x:Class="BodyCam.Views.GlassesPage"
             Title="Glasses">
    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="14">

            <!-- Status header -->
            <Frame Padding="12" CornerRadius="10">
                <HorizontalStackLayout Spacing="10">
                    <Label Text="●" FontSize="22"
                           TextColor="{Binding IsConnected, Converter={StaticResource BoolToColorConverter}}" />
                    <Label Text="{Binding StatusText}" FontSize="18" VerticalOptions="Center" />
                </HorizontalStackLayout>
            </Frame>

            <!-- Scan + device list -->
            <Button Text="Scan for Glasses" Command="{Binding ScanCommand}"
                    IsVisible="{Binding IsConnected, Converter={StaticResource InverseBoolConverter}}" />

            <CollectionView ItemsSource="{Binding Devices}"
                            SelectionMode="Single"
                            SelectedItem="{Binding SelectedDevice}"
                            IsVisible="{Binding IsConnected, Converter={StaticResource InverseBoolConverter}}">
                <CollectionView.ItemTemplate>
                    <DataTemplate>
                        <Frame Padding="8" Margin="0,4">
                            <VerticalStackLayout>
                                <Label Text="{Binding Name}" FontAttributes="Bold" />
                                <Label Text="{Binding Address}" FontSize="12" TextColor="Gray" />
                                <Label Text="{Binding Rssi, StringFormat='RSSI {0} dBm'}" FontSize="11" TextColor="Gray" />
                            </VerticalStackLayout>
                        </Frame>
                    </DataTemplate>
                </CollectionView.ItemTemplate>
            </CollectionView>

            <Button Text="Connect" Command="{Binding ConnectCommand}"
                    IsVisible="{Binding IsConnected, Converter={StaticResource InverseBoolConverter}}" />

            <!-- Status panel (connected) -->
            <Frame Padding="12" CornerRadius="10" IsVisible="{Binding IsConnected}">
                <VerticalStackLayout Spacing="8">

                    <HorizontalStackLayout Spacing="8">
                        <Label Text="🔋" FontSize="20" />
                        <ProgressBar Progress="{Binding BatteryPct, Converter={StaticResource PercentConverter}}"
                                     WidthRequest="180" VerticalOptions="Center" />
                        <Label Text="{Binding BatteryPct, StringFormat='{0}%'}" VerticalOptions="Center" />
                    </HorizontalStackLayout>

                    <Grid ColumnDefinitions="120,*" RowDefinitions="Auto,Auto,Auto,Auto" RowSpacing="4">
                        <Label Grid.Row="0" Grid.Column="0" Text="MAC"      TextColor="Gray" />
                        <Label Grid.Row="0" Grid.Column="1" Text="{Binding Mac}" />
                        <Label Grid.Row="1" Grid.Column="0" Text="Firmware" TextColor="Gray" />
                        <Label Grid.Row="1" Grid.Column="1" Text="{Binding Firmware}" />
                        <Label Grid.Row="2" Grid.Column="0" Text="Hardware" TextColor="Gray" />
                        <Label Grid.Row="2" Grid.Column="1" Text="{Binding Hardware}" />
                        <Label Grid.Row="3" Grid.Column="0" Text="Media"    TextColor="Gray" />
                        <Label Grid.Row="3" Grid.Column="1"
                               Text="{Binding Photos, StringFormat='{0} photos'}" />
                    </Grid>

                    <HorizontalStackLayout Spacing="12">
                        <Label Text="{Binding Photos,     StringFormat='📷 {0}'}" />
                        <Label Text="{Binding Videos,     StringFormat='🎬 {0}'}" />
                        <Label Text="{Binding AudioFiles, StringFormat='🎙️ {0}'}" />
                    </HorizontalStackLayout>

                </VerticalStackLayout>
            </Frame>

            <Button Text="Disconnect" Command="{Binding DisconnectCommand}"
                    IsVisible="{Binding IsConnected}"
                    BackgroundColor="Red" TextColor="White" />

        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

### Verify
- [ ] Scan populates device list with HeyCyan glasses
- [ ] Connect transitions UI to status panel within ~3 s
- [ ] Battery / MAC / firmware / hardware render correctly
- [ ] Media counts update live as photos/videos/audio are captured
- [ ] Disconnect returns to scan list

---

## Wave 3: Battery widget on the app shell

Always-visible glasses indicator + battery on `MainPage` (or `AppShell`).

```xml
<!-- AppShell.xaml header / MainPage.xaml status bar -->
<HorizontalStackLayout Spacing="4" VerticalOptions="Center"
                       IsVisible="{Binding GlassesConnected}">
    <Label Text="🕶️" FontSize="16" />
    <Label Text="{Binding GlassesBatteryPct, StringFormat='{0}%'}" FontSize="12" />
    <Label Text="⚡" FontSize="12" IsVisible="{Binding GlassesCharging}" />
    <HorizontalStackLayout.GestureRecognizers>
        <TapGestureRecognizer Command="{Binding NavigateToGlassesCommand}" />
    </HorizontalStackLayout.GestureRecognizers>
</HorizontalStackLayout>
```

```csharp
// In MainViewModel / AppShellViewModel
public MainViewModel(HeyCyanGlassesDeviceManager glasses, INavigationService nav)
{
    _glasses = glasses;
    _glasses.StateChanged  += (_, _) => RefreshGlasses();
    _glasses.StatusChanged += (_, _) => RefreshGlasses();
    NavigateToGlassesCommand = new AsyncRelayCommand(() => nav.GoToAsync("//glasses"));
}

public bool GlassesConnected   => _glasses.State == GlassesConnectionState.Connected;
public int  GlassesBatteryPct  => _glasses.Battery?.Percentage ?? 0;
public bool GlassesCharging    => _glasses.Battery?.IsCharging ?? false;

private void RefreshGlasses()
{
    OnPropertyChanged(nameof(GlassesConnected));
    OnPropertyChanged(nameof(GlassesBatteryPct));
    OnPropertyChanged(nameof(GlassesCharging));
}
```

### Verify
- [ ] Widget is hidden when disconnected
- [ ] Widget shows live battery %, updates within 1 s of `BatteryUpdated`
- [ ] Charging bolt appears when glasses are on the cradle
- [ ] Tap navigates to `GlassesPage`

---

## Wave 4: End-to-end fallback verification

Auto-fallback is implemented in M17 (`GlassesDeviceManager` raises
`Disconnected`; `CameraManager` / `AudioInputManager` / `AudioOutputManager`
/ `ButtonInputManager` re-pick the next provider by priority). Phase 7 only
**verifies** it survives a real HeyCyan disconnect mid-conversation.

### Test script (real hardware)

1. Connect glasses; start a Realtime conversation through `VoiceAgent`.
2. Confirm: live mic = glasses HFP, speaker = glasses A2DP, vision frame
   source = `HeyCyanCameraProvider`.
3. Power off glasses (or step out of BLE range).
4. Within target latency **≤ 2 s**, observe:
   - `AudioInputManager.Active` → `PlatformMicProvider`
   - `AudioOutputManager.Active` → `PlatformSpeakerProvider`
   - `CameraManager.Active` → `PhoneCameraProvider`
   - `ButtonInputManager` re-binds keyboard / phone-button provider
   - Conversation **does not drop** — audio simply re-routes.
5. Toast/notification: "Glasses disconnected — switched to phone audio".
6. Power glasses back on → auto-reconnect (3 attempts, exponential backoff)
   restores all four providers without user action.

### Verify
- [ ] Camera fallback ≤ 2 s, no agent stall
- [ ] Mic fallback ≤ 2 s, agent keeps hearing the user
- [ ] Speaker fallback ≤ 2 s, agent reply is audible on phone
- [ ] Button fallback re-binds within 1 s
- [ ] Auto-reconnect restores glasses without manual scan
- [ ] No exceptions logged during the disconnect/reconnect cycle

---

## Wave 5: M17 exit-criteria checklist on real hardware

Run the M17 milestone exit-criteria checklist with HeyCyan glasses as the
backing device. This is the milestone-level acceptance test for M33.

### Manual test plan (logged in `TestResults/m33-phase7/`)

| # | Step | Expected | Pass? |
|---|------|----------|-------|
| 1 | Cold-boot phone, open BodyCam | Glasses widget hidden | [ ] |
| 2 | `GlassesPage` → Scan | HeyCyan device appears with RSSI | [ ] |
| 3 | Connect | Status panel populated, widget shows battery | [ ] |
| 4 | Start conversation | Mic/speaker/camera all on glasses | [ ] |
| 5 | Tap glasses button | Configured action fires | [ ] |
| 6 | Double-tap | Photo captured, vision agent receives JPG | [ ] |
| 7 | Long-press | Conversation ends | [ ] |
| 8 | Power off glasses mid-call | Fallback within 2 s, call continues | [ ] |
| 9 | Power glasses back on | Auto-reconnect, providers re-bind | [ ] |
| 10 | Disconnect manually | Returns to scan list | [ ] |
| 11 | (Optional P5) Open recorded media gallery | OPUS / MP4 / JPG download via WiFi-Direct | [ ] |

### Integration test harness

```csharp
// BodyCam.IntegrationTests/Glasses/HeyCyanEndToEndTests.cs
[Trait("Category", "RealHardware")]
[Collection("HeyCyanHardware")]
public class HeyCyanEndToEndTests
{
    [Fact(Skip = "Requires paired HeyCyan glasses + HEYCYAN_E2E=1")]
    public async Task Connect_Disconnect_Reconnect_FallsBackAndRestores()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("HEYCYAN_E2E") == "1");

        var mgr = TestHost.Resolve<HeyCyanGlassesDeviceManager>();
        var devices = await mgr.ScanAsync(TimeSpan.FromSeconds(10), default);
        devices.Should().NotBeEmpty();

        await mgr.ConnectAsync(devices[0], default);
        mgr.State.Should().Be(GlassesConnectionState.Connected);
        mgr.Battery!.Percentage.Should().BeGreaterThan(0);
        mgr.Version!.MacAddress.Should().NotBeNullOrEmpty();

        await mgr.DisconnectAsync(default);
        await Task.Delay(2_500); // fallback window
        TestHost.Resolve<CameraManager>().Active.Should().BeOfType<PhoneCameraProvider>();
    }
}
```

### Verify
- [ ] All 11 manual steps pass on real hardware (Android + iOS)
- [ ] `HeyCyanEndToEndTests` green when `HEYCYAN_E2E=1`
- [ ] Test results archived under `TestResults/m33-phase7/<date>/`
- [ ] No crashes in `logcat` / Console during the full sequence

---

## Phase 7 → M33 exit-criteria mapping

| M33 exit criterion | Covered by |
|--------------------|------------|
| Photo via `HeyCyanCameraProvider` round-trips through `VisionAgent` | Wave 5 step 6 |
| BT live mic + speaker route through glasses during a conversation  | Wave 5 step 4 |
| Glasses button (tap/double/long) triggers configured actions       | Wave 5 steps 5–7 |
| Auto-fallback to phone camera + mic + speaker on disconnect        | Wave 4 + Wave 5 step 8 |
| Battery + firmware shown in status panel                            | Wave 2 + Wave 3 |
| M17 exit criteria pass end-to-end against HeyCyan hardware         | Wave 5 |

When every checkbox in Waves 1–5 is ticked, M33 is complete.
