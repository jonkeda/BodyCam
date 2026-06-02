# M33 Phase 7 — Wave 3: Shell battery widget

**Parent:** [../phase7-device-manager-ui.md](../phase7-device-manager-ui.md)
**Siblings:** [wave1-heycyan-device-manager.md](wave1-heycyan-device-manager.md) ·
[wave2-glasses-page.md](wave2-glasses-page.md) ·
[wave4-fallback-verification.md](wave4-fallback-verification.md) ·
[wave5-real-hardware-checklist.md](wave5-real-hardware-checklist.md)

## Goal

Add a small, always-visible glasses indicator + battery percentage to the
app shell (header on `AppShell.xaml`, fallback location on `MainPage.xaml`).
The widget binds directly to the **current session's** battery — i.e., the
`HeyCyanGlassesDeviceManager.Battery` value populated in Wave 1 — so it
updates live as `BatteryUpdated` notify frames arrive over BLE. Tapping the
widget navigates to `GlassesPage` (Wave 2).

## Steps

1. **Extend `MainViewModel`** (or the existing `AppShellViewModel` if one
   exists) to project the manager state. Inject the concrete
   `HeyCyanGlassesDeviceManager` (not the base) so we can read
   `Battery.Percentage` and `Battery.IsCharging`. Use `SetProperty`-free
   computed getters and refresh them via `OnPropertyChanged` from the
   manager's events — same pattern as Wave 2.

   ```csharp
   // src/BodyCam/ViewModels/MainViewModel.cs (or AppShellViewModel)
   public partial class MainViewModel : ViewModelBase
   {
       private readonly HeyCyanGlassesDeviceManager _glasses;
       private readonly INavigationService _nav;

       public MainViewModel(
           HeyCyanGlassesDeviceManager glasses,
           INavigationService nav)
       {
           _glasses = glasses;
           _nav = nav;

           _glasses.StateChanged  += (_, _) => RefreshGlasses();
           _glasses.StatusChanged += (_, _) => RefreshGlasses();

           NavigateToGlassesCommand =
               new AsyncRelayCommand(() => _nav.GoToAsync("//glasses"));
       }

       public bool GlassesConnected  => _glasses.State == GlassesConnectionState.Connected;
       public int  GlassesBatteryPct => _glasses.Battery?.Percentage ?? 0;
       public bool GlassesCharging   => _glasses.Battery?.IsCharging  ?? false;

       public AsyncRelayCommand NavigateToGlassesCommand { get; }

       private void RefreshGlasses()
       {
           OnPropertyChanged(nameof(GlassesConnected));
           OnPropertyChanged(nameof(GlassesBatteryPct));
           OnPropertyChanged(nameof(GlassesCharging));
       }
   }
   ```

2. **Add the widget markup** to the existing shell. Prefer
   `AppShell.xaml`'s `Shell.TitleView` (visible from every page); fall
   back to a header row in `MainPage.xaml` if `Shell.TitleView` is not
   used. The widget is hidden when disconnected so the shell stays
   uncluttered.

   ```xml
   <!-- AppShell.xaml -->
   <Shell.TitleView>
       <Grid ColumnDefinitions="*,Auto" Padding="12,0">
           <Label Grid.Column="0" Text="BodyCam"
                  FontSize="18" VerticalOptions="Center" />

           <HorizontalStackLayout Grid.Column="1"
                                  Spacing="4" VerticalOptions="Center"
                                  IsVisible="{Binding GlassesConnected}">
               <Label Text="🕶️" FontSize="16" />
               <Label Text="{Binding GlassesBatteryPct, StringFormat='{0}%'}"
                      FontSize="12" />
               <Label Text="⚡" FontSize="12"
                      IsVisible="{Binding GlassesCharging}" />
               <HorizontalStackLayout.GestureRecognizers>
                   <TapGestureRecognizer
                       Command="{Binding NavigateToGlassesCommand}" />
               </HorizontalStackLayout.GestureRecognizers>
           </HorizontalStackLayout>
       </Grid>
   </Shell.TitleView>
   ```

3. **Wire the BindingContext** of `Shell.TitleView` to `MainViewModel`
   (or `AppShellViewModel`). If `AppShell` doesn't currently expose a VM,
   resolve it from DI in the code-behind constructor:

   ```csharp
   public AppShell(MainViewModel vm)
   {
       InitializeComponent();
       BindingContext = vm;
       Routing.RegisterRoute("glasses", typeof(Views.GlassesPage));
   }
   ```

4. **Battery freshness contract.** The widget MUST reflect changes
   within **≤ 1 s** of an SDK `BatteryUpdated` event. The SDK pushes
   battery roughly every 30 s on its own; force an immediate read on
   `ConnectAsync` (already done in Wave 1) so the widget is correct
   the moment it appears.

5. **Optional low-battery affordance.** When `GlassesBatteryPct ≤ 15`
   and not charging, tint the percentage red. Implement with a value
   converter or a calculated `GlassesBatteryColor` property — do *not*
   add app-wide notifications here; user prompts belong in
   `INotificationService` (already used by M17 fallback).

   ```csharp
   public Color GlassesBatteryColor =>
       (!GlassesCharging && GlassesBatteryPct <= 15)
           ? Colors.Red
           : Colors.White;
   ```

6. **Tests.** Add a small test class
   `BodyCam.Tests/ViewModels/MainViewModelGlassesTests.cs`:
   - `GlassesConnected_ReflectsManagerState`
   - `GlassesBatteryPct_UpdatesOnStatusChanged`
   - `NavigateToGlassesCommand_CallsGoToAsync_GlassesRoute`

   Use a fake `HeyCyanGlassesDeviceManager` (or expose a test seam — the
   existing M17 base ctor accepts mockable providers; otherwise use
   NSubstitute on the base type).

## Verify

- [ ] Widget is **hidden** when `GlassesConnected == false`
- [ ] Widget is **visible** with live battery % once connected
- [ ] Battery % updates within 1 s of an `IHeyCyanGlassesSession.BatteryUpdated` event
- [ ] Charging bolt appears whenever `Battery.IsCharging` is true
- [ ] Tapping the widget navigates to `GlassesPage` (`//glasses`)
- [ ] Low-battery red tint kicks in at ≤ 15 % when not charging
- [ ] `MainViewModelGlassesTests` pass
