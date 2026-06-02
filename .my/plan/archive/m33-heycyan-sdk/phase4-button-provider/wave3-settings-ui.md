# M33 Phase 4 — Wave 3: Settings UI Integration

**Parent:** [`../phase4-button-provider.md`](../phase4-button-provider.md)
**Siblings:** [wave1](wave1-heycyan-button-provider.md) · [wave2](wave2-default-gesture-mapping.md) · [wave4](wave4-tests.md)
**Depends on:** Wave 1, Wave 2, M14 Phase 3 (`ButtonMappingsPage`,
`IButtonMappingStore`).

## Goal

Add a **Glasses Button** section to the existing `ButtonMappingsPage` that
exposes the three fixed HeyCyan gestures with a `Picker` of available
`ButtonAction` values per row. Persistence flows through the same
`IButtonMappingStore` used by every other provider (BTHome, GATT, AVRCP,
keyboard). The section is hidden when no `IHeyCyanGlassesSession` is
registered (i.e. on platforms without HeyCyan support).

## Steps

1. **Create the row view-model** at
   `src/BodyCam/ViewModels/HeyCyanGestureRowViewModel.cs`:

    ```csharp
    namespace BodyCam.ViewModels;

    public sealed class HeyCyanGestureRowViewModel : ViewModelBase
    {
        private readonly IButtonMappingStore _store;
        private readonly string _provider;
        private readonly string _button;
        private readonly ButtonGesture _gesture;
        private ButtonAction _action;

        public HeyCyanGestureRowViewModel(
            IButtonMappingStore store, string provider, string button, ButtonGesture gesture)
        {
            _store    = store;
            _provider = provider;
            _button   = button;
            _gesture  = gesture;
            _action   = store.Get(provider, button, gesture);
        }

        public string Label => _gesture switch
        {
            ButtonGesture.Tap       => "Tap",
            ButtonGesture.DoubleTap => "Double Tap",
            ButtonGesture.LongPress => "Long Press",
            _ => _gesture.ToString(),
        };

        public ButtonAction Action
        {
            get => _action;
            set
            {
                if (SetProperty(ref _action, value))
                    _store.Set(_provider, _button, _gesture, value);
            }
        }
    }
    ```

2. **Create the page view-model** at
   `src/BodyCam/ViewModels/HeyCyanButtonMappingsViewModel.cs`:

    ```csharp
    namespace BodyCam.ViewModels;

    public sealed class HeyCyanButtonMappingsViewModel : ViewModelBase
    {
        public HeyCyanButtonMappingsViewModel(IButtonMappingStore store)
        {
            AvailableActions = Enum.GetValues<ButtonAction>();
            Rows = HeyCyanButtonDefaults.SupportedGestures
                .Select(g => new HeyCyanGestureRowViewModel(
                    store,
                    HeyCyanButtonDefaults.ProviderId,
                    HeyCyanButtonDefaults.ButtonId,
                    g))
                .ToList();
        }

        public IReadOnlyList<ButtonAction> AvailableActions { get; }
        public IReadOnlyList<HeyCyanGestureRowViewModel> Rows { get; }
    }
    ```

    Using a single `Rows` collection rather than three named properties
    keeps the XAML simple and lets future gestures (if firmware ever adds
    them) flow in via `SupportedGestures`.

3. **Add the section to `ButtonMappingsPage.xaml`.** Reuse the existing
   page rather than introducing a new one. Add a new `Border` /
   `CollectionView` block, gated on the optional view-model:

    ```xml
    <Border IsVisible="{Binding HeyCyan, Converter={StaticResource NotNullConverter}}"
            Style="{StaticResource SettingsSectionStyle}">
      <VerticalStackLayout>
        <Label Text="Glasses Button" Style="{StaticResource SettingsSectionHeader}" />
        <Label Text="Configure tap, double-tap and long-press for the HeyCyan glasses."
               Style="{StaticResource SettingsSectionDescription}" />
        <CollectionView ItemsSource="{Binding HeyCyan.Rows}">
          <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="vm:HeyCyanGestureRowViewModel">
              <Grid ColumnDefinitions="*,2*" Padding="0,6">
                <Label Grid.Column="0" Text="{Binding Label}" VerticalOptions="Center" />
                <Picker Grid.Column="1"
                        ItemsSource="{Binding Source={RelativeSource AncestorType={x:Type vm:ButtonMappingsViewModel}}, Path=HeyCyan.AvailableActions}"
                        SelectedItem="{Binding Action, Mode=TwoWay}" />
              </Grid>
            </DataTemplate>
          </CollectionView.ItemTemplate>
        </CollectionView>
      </VerticalStackLayout>
    </Border>
    ```

4. **Expose the section from the page view-model.** In
   `ButtonMappingsViewModel` (M14 Phase 3), add an optional property:

    ```csharp
    public HeyCyanButtonMappingsViewModel? HeyCyan { get; }

    public ButtonMappingsViewModel(
        IButtonMappingStore store,
        IHeyCyanGlassesSession? heyCyanSession = null)
    {
        // …existing wiring…
        if (heyCyanSession is not null)
            HeyCyan = new HeyCyanButtonMappingsViewModel(store);
    }
    ```

    Constructor injection of `IHeyCyanGlassesSession?` (nullable) means the
    section auto-hides on platforms where HeyCyan is not registered. Do
    **not** subscribe to session events here; the page is purely about
    persisted mappings.

5. **Live updates.** Because `HeyCyanGestureRowViewModel.Action` writes
   through `IButtonMappingStore` immediately on `SetProperty`, and
   `ActionMap` reads from the same store on every dispatch, no extra
   plumbing is required. The next gesture from the glasses uses the new
   action — no app restart, no reconnect.

6. **Reset-to-defaults button (optional, low effort).** Add a
   `Button Text="Reset glasses defaults"` bound to a `RelayCommand` on
   `HeyCyanButtonMappingsViewModel` that:
   - Clears the three HeyCyan entries from the store
   - Calls `HeyCyanButtonDefaults.SeedDefaults(actionMap)` again
   - Refreshes each row's `Action` from the store

   Skip this if M14 Phase 3 doesn't already have an analogous reset
   pattern; consistency matters more than features here.

## Verify

- [ ] On a platform without `IHeyCyanGlassesSession` registered, the
      Glasses Button section is invisible
- [ ] On a platform with HeyCyan, the section renders three rows labelled
      "Tap", "Double Tap", "Long Press"
- [ ] Each row's `Picker` lists every `ButtonAction` enum value
- [ ] Selecting a new action persists to `IButtonMappingStore` immediately
      (visible across app restarts)
- [ ] After a remap, the next physical button press from the glasses
      triggers the **new** action without restart or reconnect
- [ ] All bindings are MVVM-correct: no code-behind state, all property
      setters use `SetProperty(ref _field, value)`
- [ ] (If implemented) Reset-to-defaults button restores the Wave 2 defaults
