# Step 4: Camera Preview UI

Add a small camera preview thumbnail and a vision status indicator to MainPage. The preview updates periodically while the session is active.

## Files Modified

### 1. `src/BodyCam/ViewModels/MainViewModel.cs`

**Add** camera preview properties and a periodic refresh timer:

```csharp
// ADD fields:
    private ImageSource? _cameraPreview;
    private string? _visionStatus;
    private CancellationTokenSource? _previewCts;

// ADD properties:
    public ImageSource? CameraPreview
    {
        get => _cameraPreview;
        set => SetProperty(ref _cameraPreview, value);
    }

    public string? VisionStatus
    {
        get => _visionStatus;
        set => SetProperty(ref _visionStatus, value);
    }
```

**Update** `StartAsync` (called by ToggleCommand) — start a preview refresh loop:

```csharp
// After await _orchestrator.StartAsync();
_previewCts = new CancellationTokenSource();
_ = RefreshPreviewLoopAsync(_previewCts.Token);
```

**Update** `StopAsync` — cancel the preview loop:

```csharp
// Before await _orchestrator.StopAsync();
_previewCts?.Cancel();
_previewCts?.Dispose();
_previewCts = null;
CameraPreview = null;
VisionStatus = null;
```

**Add** the preview refresh method:

```csharp
    private async Task RefreshPreviewLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var frame = await _orchestrator.CapturePreviewFrameAsync(ct);
                if (frame is not null)
                {
                    CameraPreview = ImageSource.FromStream(() => new MemoryStream(frame));
                }

                // Update vision status from cached description
                var desc = _orchestrator.Session.LastVisionDescription;
                VisionStatus = desc is not null ? "Vision: active" : "Vision: no frame";
            }
            catch (OperationCanceledException) { break; }
            catch { /* non-fatal */ }

            await Task.Delay(500, ct); // 2fps preview is sufficient
        }
    }
```

### 2. `src/BodyCam/Orchestration/AgentOrchestrator.cs`

**Add** a method to capture a raw frame for UI preview (no vision API call):

```csharp
    /// <summary>
    /// Captures a camera frame for UI preview. Does not call the vision API.
    /// </summary>
    public Task<byte[]?> CapturePreviewFrameAsync(CancellationToken ct = default)
        => _vision.Camera.CaptureFrameAsync(ct);
```

### 3. `src/BodyCam/MainPage.xaml`

**Add** camera preview overlay and vision status to the header row. Insert after the existing `HorizontalStackLayout` in Row 0:

```xml
<!-- BEFORE (existing header, unchanged): -->
<HorizontalStackLayout Grid.Row="0" Spacing="12" VerticalOptions="Center">
    <!-- existing buttons + status label -->
</HorizontalStackLayout>

<!-- ADD camera preview overlay in Row 1, positioned top-right: -->
<Grid Grid.Row="1">
    <!-- Existing transcript Frame moves inside this Grid -->
    <Frame BorderColor="{AppThemeBinding Light=#E0E0E0, Dark=#333}" Padding="8" CornerRadius="4">
        <CollectionView ... />
    </Frame>

    <!-- Camera preview overlay (top-right corner) -->
    <Border StrokeShape="RoundRectangle 4"
            Stroke="{AppThemeBinding Light=#E0E0E0, Dark=#555}"
            BackgroundColor="Black"
            WidthRequest="160" HeightRequest="120"
            HorizontalOptions="End" VerticalOptions="Start"
            Margin="8"
            IsVisible="{Binding IsRunning}">
        <Image Source="{Binding CameraPreview}"
               Aspect="AspectFill" />
    </Border>
</Grid>
```

**Restructure Row 1** to be a `Grid` containing both the transcript and the camera overlay:

```xml
<!-- Full Row 1 replacement: -->
<Grid Grid.Row="1">
    <Frame BorderColor="{AppThemeBinding Light=#E0E0E0, Dark=#333}" Padding="8" CornerRadius="4">
        <CollectionView
            x:Name="TranscriptList"
            ItemsSource="{Binding Entries}">
            <CollectionView.ItemTemplate>
                <DataTemplate x:DataType="models:TranscriptEntry">
                    <VerticalStackLayout Padding="4,2">
                        <Label Text="{Binding DisplayText}"
                               FontSize="14"
                               FontFamily="OpenSansRegular" />
                    </VerticalStackLayout>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Frame>

    <Border StrokeShape="RoundRectangle 4"
            Stroke="{AppThemeBinding Light=#E0E0E0, Dark=#555}"
            BackgroundColor="Black"
            WidthRequest="160" HeightRequest="120"
            HorizontalOptions="End" VerticalOptions="Start"
            Margin="8"
            IsVisible="{Binding IsRunning}">
        <Image Source="{Binding CameraPreview}"
               Aspect="AspectFill" />
    </Border>
</Grid>
```

**Add** vision status text to the header (after the existing StatusText label):

```xml
<Label
    Text="{Binding VisionStatus}"
    FontSize="12"
    VerticalOptions="Center"
    TextColor="{AppThemeBinding Light=#999, Dark=#666}"
    IsVisible="{Binding IsRunning}" />
```

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -v q
```

Manual: Run app → Start → verify camera preview appears in top-right corner of transcript area. Vision status should show "Vision: active" or "Vision: no frame". Preview should disappear when stopped.
