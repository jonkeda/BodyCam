# M33 Phase 5 — Recorded Media Pipeline

Post-hoc, **optional** pipeline that pulls recorded media off the glasses
(JPG photos, MP4 videos, raw-OPUS voice notes), normalizes it, and writes it
to the platform photo/audio library. This is **independent** of the live
Realtime conversation audio (Phase 3). The `.opus` voice notes can optionally
feed into the M16 dictation pipeline as a source.

**Depends on:** M33 Phase 2 (`HeyCyanMediaTransfer`, transfer-mode HTTP client),
M33 Phase 1 (`IHeyCyanGlassesSession`). **Optional:** M16 Phase 1 (dictation
source registry).

**Reference:**
- `Alternative-HeyCyan-App-and-SDK/android/AGENTS.md` — OPUS framing rules,
  `media.config` shape, `DCIM/CyanBridge` save layout.
- `Alternative-HeyCyan-App-and-SDK/ios/QCSDKDemo/MediaGalleryViewController.{h,m}`
  — feature-set mirror for the MAUI gallery page.

---

## Why a Separate Phase

Phase 2 implemented `HeyCyanMediaTransfer` for **single-shot** snapshot
retrieval (one JPG → camera provider). Real users will accumulate dozens of
recordings on the glasses across a day. Those need:

1. **Bulk enumeration** of `/files/media.config` (not just newest-N).
2. **Format normalization** — raw 40-byte OPUS frames are not playable; they
   must be wrapped in an Ogg container before the OS audio layer will touch
   them.
3. **Platform media-library integration** — `MediaStore` (Android) /
   `PHPhotoLibrary` (iOS) so files appear in Photos / Files apps.
4. **A user-facing gallery** to browse, filter, and play imports.

None of this belongs in the camera provider hot path.

---

## Wave 1: `OpusOggWrapper`

Heuristic raw-packet → Ogg/Opus wrapper. Mirrors the official app's
`OpusManager hasHead=false, packetSize=40` behavior plus the length-prefix
fallbacks documented in `android/AGENTS.md`.

```csharp
namespace BodyCam.Services.Glasses.HeyCyan.Media;

public enum OpusFraming
{
    /// <summary>Already a valid Ogg/Opus stream (starts with "OggS"). Pass-through.</summary>
    OggContainer,
    /// <summary>Fixed 40-byte raw packets (official app default).</summary>
    FixedPacket40,
    /// <summary>Each packet prefixed by u16 little-endian length.</summary>
    LengthPrefixedU16Le,
    /// <summary>Each packet prefixed by u16 big-endian length.</summary>
    LengthPrefixedU16Be,
    /// <summary>Each packet prefixed by u8 length.</summary>
    LengthPrefixedU8,
    /// <summary>Could not classify — caller should fall back to FixedPacket40.</summary>
    Unknown,
}

public static class OpusOggWrapper
{
    private const int DefaultSampleRate = 16000;
    private const int DefaultChannels = 1;
    private const int DefaultPacketSize = 40;

    /// <summary>
    /// Sniff the stream framing. Returns OggContainer when the first 4 bytes
    /// are "OggS", otherwise tries length-prefix heuristics, otherwise
    /// FixedPacket40.
    /// </summary>
    public static OpusFraming Detect(ReadOnlySpan<byte> raw);

    /// <summary>
    /// Convert raw glasses bytes into a playable Ogg/Opus byte array.
    /// If <paramref name="framing"/> is OggContainer the input is returned as-is.
    /// </summary>
    public static byte[] WrapToOgg(
        ReadOnlySpan<byte> raw,
        OpusFraming framing,
        int sampleRate = DefaultSampleRate,
        int channels = DefaultChannels,
        int packetSize = DefaultPacketSize);

    /// <summary>One-shot helper: detect + wrap.</summary>
    public static byte[] AutoWrap(ReadOnlySpan<byte> raw) =>
        WrapToOgg(raw, Detect(raw));
}
```

The Ogg writer emits an `OpusHead` page (id-header), an empty `OpusTags` page,
and then one Ogg page per ~20 ms of audio with proper granule positions.
Granule = sample count at 48 kHz (Opus convention) regardless of source rate.

### Tests (`BodyCam.Tests/Services/Glasses/HeyCyan/Media/OpusOggWrapperTests.cs`)

- Fixture `fixed-packet-40.bin` (synthetic stream of N×40-byte packets) →
  wrapped output starts with `"OggS"`, contains exactly one `OpusHead`,
  packet count matches input.
- Fixture `already-ogg.bin` (real Ogg/Opus capture) → `Detect` returns
  `OggContainer`, `WrapToOgg` returns input unchanged.
- Fixture `len-prefix-u16le.bin` → detected and wrapped without truncation.
- Garbage input (random 1 KiB) → falls back to `FixedPacket40`, still produces
  a structurally-valid Ogg stream (validated by parsing back the page headers).

---

## Wave 2: `HeyCyanRecordedMediaService` + `IMediaStore`

Bulk-import service. Enumerates `/files/media.config`, classifies by extension,
saves via the platform media-store abstraction.

```csharp
namespace BodyCam.Services.Glasses.HeyCyan.Media;

public enum RecordedMediaKind { Photo, Video, Audio, Unknown }

public sealed record RecordedMediaItem(
    string FileName,
    RecordedMediaKind Kind,
    long? SizeBytes,
    DateTimeOffset? GlassesTimestamp);

public sealed record ImportedMediaItem(
    RecordedMediaItem Source,
    string LocalUri,        // content:// on Android, ph:// or file:// on iOS
    long BytesWritten,
    TimeSpan TransferTime);

public interface IHeyCyanRecordedMediaService
{
    /// <summary>Enumerate files currently on the glasses (requires transfer mode).</summary>
    IAsyncEnumerable<RecordedMediaItem> EnumerateAsync(CancellationToken ct);

    /// <summary>Import every item, reporting progress. Skips items already imported.</summary>
    IAsyncEnumerable<ImportedMediaItem> ImportAllAsync(
        IProgress<RecordedMediaImportProgress>? progress,
        CancellationToken ct);

    /// <summary>Import a single item.</summary>
    Task<ImportedMediaItem> ImportAsync(RecordedMediaItem item, CancellationToken ct);

    /// <summary>Delete from glasses after successful import (optional, opt-in).</summary>
    Task<bool> DeleteRemoteAsync(string fileName, CancellationToken ct);
}

public sealed record RecordedMediaImportProgress(
    int Completed, int Total, string CurrentFile, long BytesSoFar);
```

Classification (case-insensitive extension match, must agree with the iOS
demo's `MediaGalleryViewController` filter):

| Extension | Kind | Save target |
|-----------|------|-------------|
| `.jpg`, `.jpeg`, `.png` | Photo | `MediaStore.Images` / `PHPhotoLibrary` |
| `.mp4`, `.mov` | Video | `MediaStore.Video` / `PHPhotoLibrary` |
| `.opus`, `.ogg` | Audio | `MediaStore.Audio` (mime `audio/ogg`) / app-Documents on iOS |
| anything else | Unknown | skipped + warning |

Audio is run through `OpusOggWrapper.AutoWrap` before being handed to the
media store. Photos and videos pass through byte-for-byte. Longer HTTP read
timeouts (60 s) are used for video to handle 30-100 MB MP4 transfers over
WiFi-Direct.

### Platform `IMediaStore`

```csharp
public interface IMediaStore
{
    Task<string> SaveImageAsync(string fileName, Stream content, CancellationToken ct);
    Task<string> SaveVideoAsync(string fileName, Stream content, CancellationToken ct);
    Task<string> SaveAudioAsync(string fileName, string mimeType, Stream content, CancellationToken ct);
    Task<bool> ExistsAsync(string fileName, RecordedMediaKind kind, CancellationToken ct);
}
```

- **Android (`AndroidMediaStore`)** — `ContentResolver.Insert` against
  `MediaStore.Images.Media.ExternalContentUri` etc. with
  `RELATIVE_PATH=DCIM/BodyCam` (matching the official app's `DCIM/CyanBridge`
  pattern). Returns the inserted `content://` URI.
- **iOS (`IosMediaStore`)** — `PHPhotoLibrary.SharedPhotoLibrary.PerformChanges`
  with `PHAssetCreationRequest` for image/video. Audio (`.ogg`) is not a
  Photos asset type → write to `NSFileManager` Documents/`BodyCam/Audio/`
  and surface a `file://` URI.
- **Windows / unsupported** — `NoopMediaStore` writes to
  `FileSystem.Current.AppDataDirectory/RecordedMedia/`.

### Verify
- [ ] `EnumerateAsync` parses `media.config` (one filename per line, trims CR/LF).
- [ ] `ImportAsync` round-trips a known JPG bytes-for-bytes.
- [ ] `ImportAsync` for `.opus` produces output that opens in `MediaPlayer`/
      `AVPlayer` without error.
- [ ] `ExistsAsync` short-circuits re-import (idempotent bulk import).
- [ ] `DeleteRemoteAsync` is opt-in and never runs in tests by default.

---

## Wave 3: MP4 Sidecar Metadata

The glasses MP4s carry no useful metadata. Write a `.bodycam.json` sidecar
next to each imported video (and optionally a small JSON blob in the audio
ID3v2-equivalent for OPUS) so later analytics/M16 ingestion has provenance.

```csharp
public sealed record RecordedMediaSidecar(
    string SourceFileName,
    string GlassesMacAddress,
    DateTimeOffset ImportedAt,
    DateTimeOffset? GlassesTimestamp,
    TimeSpan? Duration,        // probed via MediaMetadataRetriever / AVAsset
    long SizeBytes,
    string Sha256);
```

Probe duration with `MediaMetadataRetriever` (Android) and `AVAsset.Duration`
(iOS); skip silently on Windows. SHA-256 lets the M16 hook detect duplicates.

### Verify
- [ ] Sidecar JSON schema is stable (versioned, `"schema": 1`).
- [ ] Sidecar is written atomically (temp file + rename).
- [ ] MAC address matches `IHeyCyanGlassesSession.Device.Address`.

---

## Wave 4: `MediaGalleryPage` (MAUI)

Mirrors the iOS demo's `MediaGalleryViewController`: thumbnail grid, filter
chips for Photos / Videos / Audio, tap-to-play. Backed by
`MediaGalleryViewModel` over `IHeyCyanRecordedMediaService` + the OS media
store enumerator (so previously-imported items survive across launches).

```xml
<!-- Views/MediaGalleryPage.xaml -->
<ContentPage x:Class="BodyCam.Views.MediaGalleryPage"
             xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:vm="clr-namespace:BodyCam.ViewModels"
             Title="Glasses Media">
  <Grid RowDefinitions="Auto,Auto,*">
    <HorizontalStackLayout Grid.Row="0" Spacing="8" Padding="12">
      <Button Text="Photos"  Command="{Binding FilterCommand}" CommandParameter="Photo"/>
      <Button Text="Videos"  Command="{Binding FilterCommand}" CommandParameter="Video"/>
      <Button Text="Audio"   Command="{Binding FilterCommand}" CommandParameter="Audio"/>
      <Button Text="All"     Command="{Binding FilterCommand}" CommandParameter="All"/>
    </HorizontalStackLayout>

    <ProgressBar Grid.Row="1" Progress="{Binding ImportProgress}"
                 IsVisible="{Binding IsImporting}"/>

    <CollectionView Grid.Row="2"
                    ItemsSource="{Binding FilteredItems}"
                    SelectionMode="Single"
                    SelectionChangedCommand="{Binding OpenItemCommand}"
                    SelectionChangedCommandParameter="{Binding Source={RelativeSource Self}, Path=SelectedItem}">
      <CollectionView.ItemsLayout>
        <GridItemsLayout Orientation="Vertical" Span="3" HorizontalItemSpacing="4" VerticalItemSpacing="4"/>
      </CollectionView.ItemsLayout>
      <CollectionView.ItemTemplate>
        <DataTemplate>
          <Grid>
            <Image Source="{Binding ThumbnailSource}" Aspect="AspectFill"/>
            <Label Text="▶" IsVisible="{Binding IsVideo}" FontSize="28"
                   HorizontalOptions="End" VerticalOptions="End" Margin="4"/>
            <Label Text="🎙" IsVisible="{Binding IsAudio}" FontSize="32"
                   HorizontalOptions="Center" VerticalOptions="Center"/>
          </Grid>
        </DataTemplate>
      </CollectionView.ItemTemplate>
    </CollectionView>
  </Grid>
</ContentPage>
```

`MediaGalleryViewModel : ViewModelBase`:

```csharp
public sealed class MediaGalleryViewModel : ViewModelBase
{
    private readonly IHeyCyanRecordedMediaService _media;
    public ObservableCollection<MediaTileViewModel> FilteredItems { get; } = new();
    public IAsyncRelayCommand<string> FilterCommand { get; }
    public IAsyncRelayCommand<MediaTileViewModel?> OpenItemCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    // ImportProgress, IsImporting via SetProperty
}
```

Tap behavior:
- Photo → push `ImageViewerPage`.
- Video → `Launcher.OpenAsync(localUri)` so the OS player handles MP4.
- Audio → inline mini-player (`MediaElement`) bound to the wrapped Ogg URI.

Audio placeholder thumbnails are generated on the fly (waveform-icon
drawable). Video thumbnails come from `MediaMetadataRetriever.GetFrameAtTime`
(Android) / `AVAssetImageGenerator` (iOS) on first display, cached on disk.

### Verify
- [ ] Filter chips toggle visible items without re-enumerating the store.
- [ ] Tap-to-play works for all three kinds on Android and iOS.
- [ ] Progress bar reflects `RecordedMediaImportProgress` during a refresh.
- [ ] Empty-state message shown when no media has been imported yet.

---

## Wave 5: Optional M16 Dictation Hook

When M16 is wired, register imported `.opus` files as a dictation source so
they appear in the dictation pipeline alongside live mic input.

```csharp
public sealed class HeyCyanDictationSource : IDictationSource // M16 contract
{
    public string SourceId => "heycyan-voicenote";
    public Task<Stream> OpenAsync(string localUri, CancellationToken ct);
    public string MimeType => "audio/ogg";
}
```

Registration is gated behind `BodyCamOptions.HeyCyan.FeedVoiceNotesToDictation`
(default `false`) so the hook is purely opt-in. When enabled, the recorded
media service raises `OnAudioImported` and the M16 registry picks it up,
dedup'ing on the SHA-256 from the Wave 3 sidecar.

### Verify
- [ ] When the option is `false`, no M16 reference is touched (no hard
      dependency on M16 assembly load).
- [ ] When `true`, an imported `.opus` triggers exactly one
      `IDictationRegistry.Register` call.
- [ ] Re-importing the same file (same SHA-256) does not re-register.

---

## Wave 6: Tests

| Test project | Focus |
|--------------|-------|
| `BodyCam.Tests` | `OpusOggWrapperTests` (5 fixtures), `RecordedMediaClassifierTests`, `SidecarSerializerTests`, `MediaGalleryViewModelTests` (filter/refresh/open). |
| `BodyCam.IntegrationTests` | `HeyCyanRecordedMediaServiceTests` against a `FakeMediaTransfer` serving in-memory `media.config` + fixture bytes. Asserts `IMediaStore` receives the right calls in order. |
| `BodyCam.RealTests` | `[Trait("RequiresGlasses","true")]` end-to-end: connect → record 5 s audio → enter transfer → import → verify file plays. |

Fixture files live under `BodyCam.Tests/Fixtures/HeyCyan/Media/`:
`fixed-packet-40.bin`, `already-ogg.opus`, `len-prefix-u16le.bin`,
`sample.jpg`, `sample.mp4`, `media.config`.

---

## Verify (Phase 5 overall)

- [ ] `OpusOggWrapper` round-trips all four framing modes; output validated
      by an independent Ogg page parser.
- [ ] `HeyCyanRecordedMediaService` enumerates and classifies every entry in
      a real `media.config` capture without throwing on unknown extensions.
- [ ] Audio imports open in the OS player (Android `MediaPlayer`, iOS
      `AVPlayer`) without "unsupported format" errors.
- [ ] Photos and videos appear in the system Photos app under `BodyCam/`.
- [ ] Sidecar JSON exists for every imported video and audio file.
- [ ] `MediaGalleryPage` filter chips, thumbnails, and tap-to-play work on
      both Android and iOS.
- [ ] M16 dictation hook is **off by default**; toggling it on does not
      regress existing dictation behavior.
- [ ] All Wave 6 tests green; `BodyCam.RealTests` glasses E2E passes on a
      physical device.
- [ ] Phase is independent of Phase 3 — disabling Realtime audio still
      leaves the recorded-media pipeline fully functional.
