# M33 Phase 7 — Wave 2: `GlassesPage` (scan / list / connect / status)

**Parent:** [../phase7-device-manager-ui.md](../phase7-device-manager-ui.md)
**Siblings:** [wave1-heycyan-device-manager.md](wave1-heycyan-device-manager.md) ·
[wave3-shell-battery-widget.md](wave3-shell-battery-widget.md) ·
[wave4-fallback-verification.md](wave4-fallback-verification.md) ·
[wave5-real-hardware-checklist.md](wave5-real-hardware-checklist.md)

## Goal

Build the user-facing glasses-management page on top of the Wave 1 manager.
Reuse the M17 Phase 3 `GlassesViewModel` shape (Scan / Connect / Disconnect
commands + device list) and extend it with a HeyCyan-specific **status
panel** showing battery%, MAC, hardware version, firmware version, and live
photo / video / audio file counts. The page is the primary diagnostic
surface for Wave 5's manual hardware checklist.

## Steps

1. **Update `src/BodyCam/ViewModels/GlassesViewModel.cs`.** Replace the
   M17-stub VM with a HeyCyan-aware version. Inject the concrete
   `HeyCyanGlassesDeviceManager` (Wave 1) — the base type does not expose
   battery/version/media counts. All property setters use `SetProperty`
   per `.github/copilot-instructions.md`; commands use `AsyncRelayCommand`
   from `BodyCam.Mvvm`.

   ```csharp
   namespace BodyCam.ViewModels;

   public sealed class GlassesViewModel : ViewModelBase
   {
       private readonly HeyCyanGlassesDeviceManager _glasses;

       public GlassesViewModel(HeyCyanGlassesDeviceManager glasses)
       {
           _glasses = glasses;
           _glasses.StateChanged  += (_, _) => RefreshAll();
           _glasses.StatusChanged += (_, _) => RefreshAll();

           ScanCommand       = new AsyncRelayCommand(ScanAsync);
           StopScanCommand   = new RelayCommand(StopScan);
           ConnectCommand    = new AsyncRelayCommand(
               ConnectAsync, () => SelectedDevice is not null);
           DisconnectCommand = new AsyncRelayCommand(
               DisconnectAsync, () => IsConnected);
       }

       private CancellationTokenSource? _scanCts;

       private bool _isScanning;
       public bool IsScanning
       {
           get => _isScanning;
           private set => SetProperty(ref _isScanning, value);
       }

       public ObservableCollection<HeyCyanDeviceInfo> Devices { get; } = new();

       private HeyCyanDeviceInfo? _selectedDevice;
       public HeyCyanDeviceInfo? SelectedDevice
       {
           get => _selectedDevice;
           set
           {
               if (SetProperty(ref _selectedDevice, value))
                   ConnectCommand.NotifyCanExecuteChanged();
           }
       }

       public bool   IsConnected => _glasses.State == GlassesConnectionState.Connected;
       public int    BatteryPct  => _glasses.Battery?.Percentage ?? 0;
       public bool   IsCharging  => _glasses.Battery?.IsCharging ?? false;
       public string Mac         => _glasses.MacAddress ?? "—";
       public string Firmware    => _glasses.Version?.Firmware ?? "—";
       public string Hardware    => _glasses.Version?.Hardware ?? "—";
       public int    Photos      => _glasses.MediaCount?.Photos ?? 0;
       public int    Videos      => _glasses.MediaCount?.Videos ?? 0;
       public int    AudioFiles  => _glasses.MediaCount?.AudioFiles ?? 0;

       public string StatusText => _glasses.State switch
       {
           GlassesConnectionState.Disconnected => "Not connected",
           GlassesConnectionState.Scanning     => "Scanning…",
           GlassesConnectionState.Connecting   => "Connecting…",
           GlassesConnectionState.Connected    =>
               $"Connected — {BatteryPct}%{(IsCharging ? " ⚡" : string.Empty)}",
           _ => "Unknown",
       };

       public AsyncRelayCommand ScanCommand       { get; }
       public RelayCommand StopScanCommand           { get; }
       public AsyncRelayCommand ConnectCommand    { get; }
       public AsyncRelayCommand DisconnectCommand { get; }

       private async Task ScanAsync(CancellationToken ct)
       {
           _scanCts = new CancellationTokenSource();
           IsScanning = true;
           try
           {
               Devices.Clear();
               var found = await _glasses.ScanAsync(TimeSpan.FromSeconds(8), _scanCts.Token);
               foreach (var d in found) Devices.Add(d);
           }
           catch (OperationCanceledException) { }
           finally
           {
               IsScanning = false;
               _scanCts?.Dispose();
               _scanCts = null;
           }
       }

       private void StopScan()
       {
           _scanCts?.Cancel();
       }

       private Task ConnectAsync(CancellationToken ct)
           => SelectedDevice is null
               ? Task.CompletedTask
               : _glasses.ConnectAsync(SelectedDevice, ct);

       private Task DisconnectAsync(CancellationToken ct)
           => _glasses.DisconnectAsync(ct);

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

2. **Create `src/BodyCam/Views/GlassesPage.xaml`** (and a code-behind that
   only resolves the VM from DI). Layout: status header → scan button →
   device list → connect button — these three are visible only when
   *disconnected* — and a status panel + disconnect button visible only
   when *connected*.

   ```xml
   <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                x:Class="BodyCam.Views.GlassesPage"
                Title="Glasses">
       <ScrollView>
           <VerticalStackLayout Padding="16" Spacing="14">

               <!-- Status header -->
               <Frame Padding="12" CornerRadius="10">
                   <HorizontalStackLayout Spacing="10">
                       <Label Text="●" FontSize="22"
                              TextColor="{Binding IsConnected,
                                          Converter={StaticResource BoolToColorConverter}}" />
                       <Label Text="{Binding StatusText}" FontSize="18"
                              VerticalOptions="Center" />
                   </HorizontalStackLayout>
               </Frame>

               <!-- Scan + device list (disconnected only) -->
               <Button Text="Scan for Glasses"
                       Command="{Binding ScanCommand}"
                       IsVisible="{Binding IsScanning,
                                   Converter={StaticResource InverseBoolConverter}}" />

               <HorizontalStackLayout Spacing="10"
                                      IsVisible="{Binding IsScanning}">
                   <ActivityIndicator IsRunning="{Binding IsScanning}"
                                      WidthRequest="24" HeightRequest="24"
                                      VerticalOptions="Center" />
                   <Button Text="Stop Scanning"
                           Command="{Binding StopScanCommand}" />
               </HorizontalStackLayout>

               <CollectionView ItemsSource="{Binding Devices}"
                               SelectionMode="Single"
                               SelectedItem="{Binding SelectedDevice}"
                               IsVisible="{Binding IsConnected,
                                           Converter={StaticResource InverseBoolConverter}}">
                   <CollectionView.ItemTemplate>
                       <DataTemplate>
                           <Frame Padding="8" Margin="0,4">
                               <VerticalStackLayout>
                                   <Label Text="{Binding Name}" FontAttributes="Bold" />
                                   <Label Text="{Binding Address}"
                                          FontSize="12" TextColor="Gray" />
                                   <Label Text="{Binding Rssi, StringFormat='RSSI {0} dBm'}"
                                          FontSize="11" TextColor="Gray" />
                               </VerticalStackLayout>
                           </Frame>
                       </DataTemplate>
                   </CollectionView.ItemTemplate>
               </CollectionView>

               <Button Text="Connect"
                       Command="{Binding ConnectCommand}"
                       IsVisible="{Binding IsConnected,
                                   Converter={StaticResource InverseBoolConverter}}" />

               <!-- Status panel (connected only) -->
               <Frame Padding="12" CornerRadius="10"
                      IsVisible="{Binding IsConnected}">
                   <VerticalStackLayout Spacing="8">

                       <HorizontalStackLayout Spacing="8">
                           <Label Text="🔋" FontSize="20" />
                           <ProgressBar
                               Progress="{Binding BatteryPct,
                                          Converter={StaticResource PercentConverter}}"
                               WidthRequest="180" VerticalOptions="Center" />
                           <Label Text="{Binding BatteryPct, StringFormat='{0}%'}"
                                  VerticalOptions="Center" />
                           <Label Text="⚡" IsVisible="{Binding IsCharging}"
                                  VerticalOptions="Center" />
                       </HorizontalStackLayout>

                       <Grid ColumnDefinitions="120,*"
                             RowDefinitions="Auto,Auto,Auto" RowSpacing="4">
                           <Label Grid.Row="0" Grid.Column="0"
                                  Text="MAC" TextColor="Gray" />
                           <Label Grid.Row="0" Grid.Column="1" Text="{Binding Mac}" />
                           <Label Grid.Row="1" Grid.Column="0"
                                  Text="Firmware" TextColor="Gray" />
                           <Label Grid.Row="1" Grid.Column="1" Text="{Binding Firmware}" />
                           <Label Grid.Row="2" Grid.Column="0"
                                  Text="Hardware" TextColor="Gray" />
                           <Label Grid.Row="2" Grid.Column="1" Text="{Binding Hardware}" />
                       </Grid>

                       <HorizontalStackLayout Spacing="14">
                           <Label Text="{Binding Photos,     StringFormat='📷 {0}'}" />
                           <Label Text="{Binding Videos,     StringFormat='🎬 {0}'}" />
                           <Label Text="{Binding AudioFiles, StringFormat='🎙️ {0}'}" />
                       </HorizontalStackLayout>

                   </VerticalStackLayout>
               </Frame>

               <Button Text="Disconnect"
                       Command="{Binding DisconnectCommand}"
                       IsVisible="{Binding IsConnected}"
                       BackgroundColor="Red" TextColor="White" />

           </VerticalStackLayout>
       </ScrollView>
   </ContentPage>
   ```

3. **Register the VM + page** in DI (`MauiProgram.cs`) and add a route to
   `AppShell` (`//glasses`) so Wave 3's tap-to-navigate can reach it.

4. **Reuse existing converters** from `src/BodyCam/Converters/`:
   `BoolToColorConverter`, `InverseBoolConverter`, `PercentConverter`. Add
   any missing converter only if it doesn't already exist — do not
   duplicate.

5. **Unit tests** in `BodyCam.Tests/ViewModels/GlassesViewModelTests.cs`
   covering: status text per state; `IsConnected` flips on
   `StateChanged`; `Photos`/`Videos`/`AudioFiles` reflect
   `MediaCount`; `ConnectCommand.CanExecute` toggles with
   `SelectedDevice`.

## Verify

- [ ] Scan populates the device list with HeyCyan glasses (name, MAC, RSSI)
- [ ] Scan button changes to "Stop Scanning" with spinner while scanning
- [ ] Clicking "Stop Scanning" cancels the scan and returns to "Scan for Glasses"
- [ ] Connect transitions UI to the status panel within ~3 s
- [ ] Battery %, MAC, firmware, hardware all render correctly
- [ ] Charging bolt appears when `IsCharging` is true
- [ ] Photo / video / audio counts update live as captures occur
- [ ] Disconnect returns the UI to scan + list
- [ ] `GlassesViewModelTests` pass (xUnit + FluentAssertions)
- [ ] No `CommunityToolkit.Mvvm` references introduced
