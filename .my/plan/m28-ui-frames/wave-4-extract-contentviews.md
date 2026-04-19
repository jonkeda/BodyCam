# Wave 4 — Extract MainPage ContentViews

**Prerequisite:** Wave 3 (pages in folders)  
**Solves:** P5 (monolithic MainPage.xaml — 345 lines)  
**Target:** Build green, all tests green  

---

## Problem

`MainPage.xaml` is 345 lines containing 4 visual sections inline:
- **Row 0** (lines 31–81): Status bar — state dot, segmented control (Sleep/Listen/Active), debug toggle, clear
- **Row 1** (lines 83–180): Content area — transcript `CollectionView`, camera `CameraView`, snapshot overlay, debug overlay
- **Row 2** (lines 182–197): Tab selector — Transcript/Camera buttons
- **Row 3** (lines 199–238): Quick actions — Look, Read, Find, Ask, Photo buttons

Adding features (map view, history panel) means this file keeps growing.

---

## Extraction Plan

One ContentView per visual section. Each extraction is one commit.

### Step 4a — Extract `StatusBarView`

**New file:** `src/BodyCam/Pages/Main/Views/StatusBarView.xaml`

Extract Row 0 (status bar) into a ContentView. BindingContext inherits from MainPage → MainViewModel.

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:BodyCam.ViewModels"
             x:Class="BodyCam.Pages.Main.Views.StatusBarView"
             x:DataType="vm:MainViewModel">

    <Grid ColumnDefinitions="Auto,*,Auto,Auto" Padding="12,8"
          BackgroundColor="{AppThemeBinding Light=#FAFAFA, Dark=#1A1A1A}">

        <Ellipse AutomationId="StatusDot"
                 WidthRequest="12" HeightRequest="12"
                 Fill="{Binding StateColor}"
                 VerticalOptions="Center" />

        <Border Grid.Column="1" StrokeShape="RoundRectangle 16"
                Stroke="{AppThemeBinding Light=#E0E0E0, Dark=#333333}"
                Padding="2" Margin="8,0"
                HorizontalOptions="Start"
                HeightRequest="36">
            <Grid ColumnDefinitions="Auto,Auto,Auto" ColumnSpacing="0">
                <Button AutomationId="SleepButton"
                        Text="😴"
                        Command="{Binding SetStateCommand}"
                        CommandParameter="Sleep"
                        BackgroundColor="{Binding SleepSegmentColor}"
                        TextColor="{Binding SleepSegmentTextColor}"
                        CornerRadius="14"
                        HeightRequest="32" MinimumWidthRequest="56"
                        FontSize="14" Padding="8,0" />
                <Button Grid.Column="1" AutomationId="ListenButton"
                        Text="👂"
                        Command="{Binding SetStateCommand}"
                        CommandParameter="Listen"
                        BackgroundColor="{Binding ListenSegmentColor}"
                        TextColor="{Binding ListenSegmentTextColor}"
                        CornerRadius="14"
                        HeightRequest="32" MinimumWidthRequest="56"
                        FontSize="14" Padding="8,0" />
                <Button Grid.Column="2" AutomationId="ActiveButton"
                        Text="💬"
                        Command="{Binding SetStateCommand}"
                        CommandParameter="Active"
                        BackgroundColor="{Binding ActiveSegmentColor}"
                        TextColor="{Binding ActiveSegmentTextColor}"
                        CornerRadius="14"
                        HeightRequest="32" MinimumWidthRequest="56"
                        FontSize="14" Padding="8,0" />
            </Grid>
        </Border>

        <Button Grid.Column="2" AutomationId="DebugToggleButton"
                Text="🐛"
                Command="{Binding ToggleDebugCommand}"
                WidthRequest="40" HeightRequest="32"
                FontSize="16" Padding="0"
                BackgroundColor="Transparent" />

        <Button Grid.Column="3" AutomationId="ClearButton"
                Text="Clear"
                Command="{Binding ClearCommand}"
                WidthRequest="60" HeightRequest="32"
                FontSize="12"
                BackgroundColor="{AppThemeBinding Light=#E0E0E0, Dark=#333}"
                TextColor="{AppThemeBinding Light=#333, Dark=#E0E0E0}" />
    </Grid>
</ContentView>
```

**New file:** `src/BodyCam/Pages/Main/Views/StatusBarView.xaml.cs`
```csharp
namespace BodyCam.Pages.Main.Views;

public partial class StatusBarView : ContentView
{
    public StatusBarView()
    {
        InitializeComponent();
    }
}
```

### Step 4b — Extract `QuickActionsView`

**New file:** `src/BodyCam/Pages/Main/Views/QuickActionsView.xaml`

Extract Row 3 (quick action bar):

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:BodyCam.ViewModels"
             x:Class="BodyCam.Pages.Main.Views.QuickActionsView"
             x:DataType="vm:MainViewModel">

    <ContentView.Resources>
        <Style x:Key="ActionButton" TargetType="Button">
            <Setter Property="HeightRequest" Value="48" />
            <Setter Property="FontSize" Value="14" />
            <Setter Property="CornerRadius" Value="8" />
            <Setter Property="BackgroundColor"
                    Value="{AppThemeBinding Light=#EEEEEE, Dark=#2A2A2A}" />
            <Setter Property="TextColor"
                    Value="{AppThemeBinding Light=#333333, Dark=#E0E0E0}" />
        </Style>
    </ContentView.Resources>

    <Grid ColumnDefinitions="*,*,*" RowDefinitions="Auto,Auto"
          ColumnSpacing="8" RowSpacing="8" Padding="12">
        <Button AutomationId="LookButton"
                Text="👁 Look"
                Command="{Binding LookCommand}"
                IsEnabled="{Binding CanAct}"
                Style="{StaticResource ActionButton}" />
        <Button Grid.Column="1" AutomationId="ReadButton"
                Text="📖 Read"
                Command="{Binding ReadCommand}"
                IsEnabled="{Binding CanAct}"
                Style="{StaticResource ActionButton}" />
        <Button Grid.Column="2" AutomationId="FindButton"
                Text="🔍 Find"
                Command="{Binding FindCommand}"
                IsEnabled="{Binding CanAct}"
                Style="{StaticResource ActionButton}" />
        <Button Grid.Row="1" AutomationId="AskButton"
                Text="💬 Ask"
                Command="{Binding AskCommand}"
                Style="{StaticResource ActionButton}" />
        <Button Grid.Row="1" Grid.Column="1" AutomationId="PhotoButton"
                Text="📸 Photo"
                Command="{Binding PhotoCommand}"
                IsEnabled="{Binding CanAct}"
                Style="{StaticResource ActionButton}" />
    </Grid>
</ContentView>
```

### Step 4c — Extract `DebugOverlayView`

**New file:** `src/BodyCam/Pages/Main/Views/DebugOverlayView.xaml`

Extract the debug overlay from Row 1:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:BodyCam.ViewModels"
             x:Class="BodyCam.Pages.Main.Views.DebugOverlayView"
             x:DataType="vm:MainViewModel">

    <Grid IsVisible="{Binding DebugVisible}"
          VerticalOptions="End"
          MaximumHeightRequest="250"
          BackgroundColor="{AppThemeBinding Light=#F5F5F5CC, Dark=#1A1A1ACC}">
        <Frame Padding="8" CornerRadius="8">
            <ScrollView x:Name="DebugScroll" AutomationId="DebugScroll">
                <Label AutomationId="DebugLabel"
                       Text="{Binding DebugLog}"
                       FontSize="11"
                       FontFamily="OpenSansRegular"
                       TextColor="Gray" />
            </ScrollView>
        </Frame>
    </Grid>
</ContentView>
```

### Step 4d — Resulting MainPage.xaml

After all extractions, `MainPage.xaml` becomes a composition shell:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:BodyCam.ViewModels"
             xmlns:models="clr-namespace:BodyCam.Models"
             xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit"
             xmlns:converters="clr-namespace:BodyCam.Converters"
             xmlns:views="clr-namespace:BodyCam.Pages.Main.Views"
             x:Class="BodyCam.Pages.MainPage"
             x:DataType="vm:MainViewModel"
             Title="{Binding Title}">

    <ContentPage.Resources>
        <converters:InvertBoolConverter x:Key="InvertBool" />
    </ContentPage.Resources>

    <Grid RowDefinitions="Auto,*,Auto,Auto">

        <!-- Row 0: Status bar -->
        <views:StatusBarView Grid.Row="0" />

        <!-- Row 1: Content area (transcript OR camera) -->
        <Grid Grid.Row="1">

            <!-- Transcript tab -->
            <Frame IsVisible="{Binding ShowTranscriptTab}"
                   BorderColor="{AppThemeBinding Light=#E0E0E0, Dark=#333}" Padding="8" CornerRadius="4">
                <CollectionView x:Name="TranscriptList"
                                AutomationId="TranscriptList"
                                ItemsSource="{Binding Entries}">
                    <CollectionView.ItemTemplate>
                        <DataTemplate x:DataType="models:TranscriptEntry">
                            <VerticalStackLayout Padding="4,2">
                                <Label Text="{Binding DisplayText}"
                                       FontSize="14"
                                       FontFamily="OpenSansRegular"
                                       TextColor="{Binding RoleColor}" />
                                <Image Source="{Binding Image}"
                                       IsVisible="{Binding HasImage}"
                                       HeightRequest="200"
                                       Aspect="AspectFit"
                                       Margin="0,4" />
                                <Label Text="{Binding ImageCaption}"
                                       IsVisible="{Binding HasImage}"
                                       FontSize="12"
                                       TextColor="Gray"
                                       FontAttributes="Italic" />
                            </VerticalStackLayout>
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
            </Frame>

            <!-- Camera tab -->
            <Grid IsVisible="{Binding ShowCameraTab}">
                <Label AutomationId="CameraPlaceholder"
                       Text="Camera initializing..."
                       HorizontalOptions="Center" VerticalOptions="Center"
                       TextColor="Gray" />
                <toolkit:CameraView x:Name="CameraPreview" AutomationId="CameraPreview" />

                <!-- Snapshot overlay -->
                <Grid IsVisible="{Binding ShowSnapshot}"
                      BackgroundColor="#80000000"
                      Padding="16">
                    <Border StrokeShape="RoundRectangle 8"
                            BackgroundColor="{AppThemeBinding Light=White, Dark=#2A2A2A}"
                            Padding="8"
                            VerticalOptions="Center"
                            HorizontalOptions="Center"
                            MaximumWidthRequest="400">
                        <VerticalStackLayout Spacing="8">
                            <Image AutomationId="SnapshotImage"
                                   Source="{Binding SnapshotImage}"
                                   HeightRequest="300"
                                   Aspect="AspectFit" />
                            <Label AutomationId="SnapshotCaption"
                                   Text="{Binding SnapshotCaption}"
                                   FontSize="14"
                                   HorizontalTextAlignment="Center" />
                            <Button AutomationId="DismissSnapshotButton"
                                    Text="Dismiss"
                                    Command="{Binding DismissSnapshotCommand}"
                                    HorizontalOptions="Center"
                                    BackgroundColor="{AppThemeBinding Light=#E0E0E0, Dark=#333}" />
                        </VerticalStackLayout>
                    </Border>
                </Grid>
            </Grid>

            <!-- Debug overlay -->
            <views:DebugOverlayView />
        </Grid>

        <!-- Row 2: Tab selector -->
        <Grid Grid.Row="2" ColumnDefinitions="*,*" HeightRequest="40"
              BackgroundColor="{AppThemeBinding Light=#F0F0F0, Dark=#222222}">
            <Button AutomationId="TranscriptTabButton"
                    Text="📝 Transcript"
                    Command="{Binding SwitchToTranscriptCommand}"
                    BackgroundColor="Transparent"
                    TextColor="{AppThemeBinding Light=#333, Dark=#E0E0E0}"
                    FontSize="14" />
            <Button Grid.Column="1" AutomationId="CameraTabButton"
                    Text="📷 Camera"
                    Command="{Binding SwitchToCameraCommand}"
                    BackgroundColor="Transparent"
                    TextColor="{AppThemeBinding Light=#333, Dark=#E0E0E0}"
                    FontSize="14" />
        </Grid>

        <!-- Row 3: Quick action bar -->
        <views:QuickActionsView Grid.Row="3" />
    </Grid>
</ContentPage>
```

**MainPage drops from 345 → ~120 lines.** The transcript/camera section stays inline because `TranscriptList` is referenced by name in code-behind for scroll-to-bottom logic.

### `MainPage.xaml.cs` — No changes

The code-behind references `TranscriptList` and `CameraPreview` by `x:Name`, which remain in MainPage.xaml. The `DismissSnapshotButton` reference also stays. No code-behind changes needed.

---

## Files Changed

| File | Action |
|---|---|
| `src/BodyCam/Pages/Main/Views/StatusBarView.xaml(.cs)` | **Create** |
| `src/BodyCam/Pages/Main/Views/QuickActionsView.xaml(.cs)` | **Create** |
| `src/BodyCam/Pages/Main/Views/DebugOverlayView.xaml(.cs)` | **Create** |
| `src/BodyCam/Pages/MainPage.xaml` | **Simplify** — replace inline sections with ContentView references |

## UI Test Impact

- All AutomationIds preserved on the same elements
- ContentViews inherit BindingContext from parent
- FlaUI doesn't care about XAML nesting → **zero test changes**

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
# Spot-check MainPage tests
Get-Process -Name "BodyCam*" -EA SilentlyContinue | Stop-Process -Force
dotnet test src/BodyCam.UITests --no-build --filter "FullyQualifiedName~StatusBarTests"
Get-Process -Name "BodyCam*" -EA SilentlyContinue | Stop-Process -Force
dotnet test src/BodyCam.UITests --no-build --filter "FullyQualifiedName~QuickActionTests"
```
