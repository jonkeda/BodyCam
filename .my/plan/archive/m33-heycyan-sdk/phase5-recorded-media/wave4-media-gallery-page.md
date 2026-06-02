# M33 Phase 5 — Wave 4: `MediaGalleryPage` (MAUI)

Parent: [../phase5-recorded-media.md](../phase5-recorded-media.md)
Siblings: [wave1-opus-ogg-wrapper.md](wave1-opus-ogg-wrapper.md)
· [wave2-recorded-media-service.md](wave2-recorded-media-service.md)
· [wave3-mp4-sidecar-metadata.md](wave3-mp4-sidecar-metadata.md)
· [wave5-m16-dictation-hook.md](wave5-m16-dictation-hook.md)
· [wave6-tests.md](wave6-tests.md)

## Goal

User-facing browser for everything imported by Wave 2: thumbnail grid,
filter chips for Photos / Videos / Audio / All, tap-to-play. Mirrors the
iOS reference demo's `MediaGalleryViewController` feature-set so anyone
familiar with the vendor app finds the same workflow.

Backed by `MediaGalleryViewModel` over `IHeyCyanRecordedMediaService` plus
the OS media store so previously-imported items survive across launches
(we don't keep our own DB; the OS is the source of truth).

## Steps

1. **ViewModel** — `src/BodyCam/ViewModels/MediaGalleryViewModel.cs`,
   inheriting `ViewModelBase`. Fields backed by `SetProperty(ref _x, value)`:

    ```csharp
    public sealed class MediaGalleryViewModel : ViewModelBase
    {
        private readonly IHeyCyanRecordedMediaService _media;
        private readonly IMediaStore _store;

        public ObservableCollection<MediaTileViewModel> AllItems     { get; } = new();
        public ObservableCollection<MediaTileViewModel> FilteredItems { get; } = new();

        private string _filter = "All";
        public string Filter
        {
            get => _filter;
            set { if (SetProperty(ref _filter, value)) ApplyFilter(); }
        }

        private double _importProgress;
        public double ImportProgress
        {
            get => _importProgress;
            set => SetProperty(ref _importProgress, value);
        }

        private bool _isImporting;
        public bool IsImporting
        {
            get => _isImporting;
            set => SetProperty(ref _isImporting, value);
        }

        public IAsyncRelayCommand<string>            FilterCommand   { get; }
        public IAsyncRelayCommand<MediaTileViewModel?> OpenItemCommand { get; }
        public IAsyncRelayCommand                    RefreshCommand  { get; }
    }
    ```

   Use `RelayCommand` / `AsyncRelayCommand` from `BodyCam.Mvvm` — never
   `CommunityToolkit.Mvvm` (project rule).

2. **Tile VM** — `MediaTileViewModel : ViewModelBase` with: `LocalUri`,
   `FileName`, `Kind` (`RecordedMediaKind`), `IsPhoto`, `IsVideo`,
   `IsAudio` (computed bools), `ThumbnailSource` (`ImageSource?`),
   `Duration` (from sidecar, `string?` formatted "0:23"). Thumbnails are
   lazy — first access triggers `LoadThumbnailAsync`.

3. **Refresh flow** — `RefreshCommand` does:

    1. `IsImporting = true; ImportProgress = 0;`
    2. `await foreach (var item in _media.ImportAllAsync(progress, ct))` —
       progress callback marshals to UI thread via
       `MainThread.BeginInvokeOnMainThread` and updates
       `ImportProgress = (double)p.Completed / p.Total`.
    3. After import, `await ReloadFromStoreAsync(ct)` enumerates the
       platform media store (`MediaStore` / `PHPhotoLibrary` / app dir)
       under our `BodyCam` folder so previously-imported items appear too.
    4. `ApplyFilter();` then `IsImporting = false;`.

4. **`ApplyFilter`** is a synchronous in-memory rebuild of `FilteredItems`
   from `AllItems`. Filter chips do **not** re-enumerate the platform
   store — they just toggle visibility, so they feel instant.

5. **Open behavior** — `OpenItemCommand` handler dispatches by `Kind`:

    - `Photo` → `Shell.Current.GoToAsync(nameof(ImageViewerPage), …)` with
      the `LocalUri` as a query parameter. (`ImageViewerPage` is a tiny
      page with a single full-screen `Image`; create alongside the
      gallery if it does not yet exist.)
    - `Video` → `await Launcher.Default.OpenAsync(new OpenFileRequest
      { File = new ReadOnlyFile(localPath) })` so the OS player handles
      MP4. **Standard direct save** means the OS already has the right
      handlers; no embedded player needed.
    - `Audio` → push `AudioPlayerPage` with a `MediaElement` bound to the
      Ogg URI. Works because Wave 1 wrapped the raw 40-byte packets into
      a real Ogg container.

6. **XAML** — `src/BodyCam/Views/MediaGalleryPage.xaml`:

    ```xml
    <ContentPage x:Class="BodyCam.Views.MediaGalleryPage"
                 xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                 xmlns:vm="clr-namespace:BodyCam.ViewModels"
                 Title="Glasses Media">
      <Grid RowDefinitions="Auto,Auto,*">

        <HorizontalStackLayout Grid.Row="0" Spacing="8" Padding="12">
          <Button Text="All"    Command="{Binding FilterCommand}" CommandParameter="All"/>
          <Button Text="Photos" Command="{Binding FilterCommand}" CommandParameter="Photo"/>
          <Button Text="Videos" Command="{Binding FilterCommand}" CommandParameter="Video"/>
          <Button Text="Audio"  Command="{Binding FilterCommand}" CommandParameter="Audio"/>
          <Button Text="Refresh" Command="{Binding RefreshCommand}"/>
        </HorizontalStackLayout>

        <ProgressBar Grid.Row="1" Progress="{Binding ImportProgress}"
                     IsVisible="{Binding IsImporting}"/>

        <CollectionView Grid.Row="2"
                        ItemsSource="{Binding FilteredItems}"
                        SelectionMode="Single"
                        SelectionChangedCommand="{Binding OpenItemCommand}"
                        SelectionChangedCommandParameter="{Binding Source={RelativeSource Self}, Path=SelectedItem}">
          <CollectionView.ItemsLayout>
            <GridItemsLayout Orientation="Vertical" Span="3"
                             HorizontalItemSpacing="4" VerticalItemSpacing="4"/>
          </CollectionView.ItemsLayout>
          <CollectionView.ItemTemplate>
            <DataTemplate>
              <Grid HeightRequest="120">
                <Image Source="{Binding ThumbnailSource}" Aspect="AspectFill"/>
                <Label Text="▶" IsVisible="{Binding IsVideo}" FontSize="28"
                       HorizontalOptions="End" VerticalOptions="End" Margin="4"/>
                <Label Text="🎙" IsVisible="{Binding IsAudio}" FontSize="32"
                       HorizontalOptions="Center" VerticalOptions="Center"/>
                <Label Text="{Binding Duration}" FontSize="10"
                       BackgroundColor="#80000000" TextColor="White"
                       HorizontalOptions="Start" VerticalOptions="End"
                       Padding="4,1"
                       IsVisible="{Binding Duration, Converter={StaticResource NotNullToBoolConverter}}"/>
              </Grid>
            </DataTemplate>
          </CollectionView.ItemTemplate>

          <CollectionView.EmptyView>
            <Label Text="No glasses media imported yet. Tap Refresh."
                   HorizontalOptions="Center" VerticalOptions="Center"
                   FontSize="16"/>
          </CollectionView.EmptyView>
        </CollectionView>
      </Grid>
    </ContentPage>
    ```

7. **Code-behind** — minimal: `BindingContext = vm;` injected via
   constructor DI. Page registered in `AppShell.xaml` under a `glasses`
   tab, route name `media-gallery`.

8. **Thumbnails**:

    - **Photo** — `ImageSource.FromUri/FromFile` directly on the local URI;
      MAUI handles down-sampling.
    - **Video** — first display calls
      `Android.Media.MediaMetadataRetriever.GetFrameAtTime(0)` /
      `AVAssetImageGenerator.CopyCGImageAtTime(.zero)`, encodes JPEG to
      `Cache/Thumbs/<sha256>.jpg`, sets `ThumbnailSource` to that file.
      Subsequent displays read the cached file directly.
    - **Audio** — bundled waveform-icon drawable
      (`Resources/Images/audio_placeholder.png`). No per-file generation.

9. **DI registration** in `MauiProgram.cs`:

    ```csharp
    builder.Services.AddTransient<MediaGalleryViewModel>();
    builder.Services.AddTransient<MediaGalleryPage>();
    Routing.RegisterRoute("media-gallery", typeof(MediaGalleryPage));
    ```

## Verify

- [ ] Filter chips toggle `FilteredItems` without re-enumerating the
      platform media store (verified by spying on
      `IHeyCyanRecordedMediaService.EnumerateAsync` call count).
- [ ] Tapping a photo tile pushes `ImageViewerPage` with the correct URI.
- [ ] Tapping a video tile invokes `Launcher.OpenAsync` and the OS player
      starts (manual on device, smoke-asserted in tests).
- [ ] Tapping an audio tile pushes `AudioPlayerPage` and `MediaElement`
      reaches `Playing` state on the wrapped Ogg URI.
- [ ] `ProgressBar` is hidden when `IsImporting` is false and shows
      monotonic progress while a refresh runs.
- [ ] `EmptyView` label is visible when `FilteredItems.Count == 0`.
- [ ] Video thumbnails are cached after first display (no re-extraction
      on scroll-back).
- [ ] All bindings use `SetProperty` chain in the VM — no manual
      `OnPropertyChanged` calls (project MVVM rule).
- [ ] Page works offline (no live glasses connection) — old imports
      load from the OS media store alone.
