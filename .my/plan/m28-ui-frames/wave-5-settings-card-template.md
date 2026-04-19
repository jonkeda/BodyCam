# Wave 5 — Settings Card Template

**Prerequisite:** Wave 1 (cards exist as Buttons)  
**Solves:** Visual polish — replaces plain Buttons with styled card ContentViews  
**Target:** Build green, all tests green  

---

## Problem

Wave 1 replaces Frame+TapGestureRecognizer with plain `Button` elements for each settings card. This works for automation but looks flat — no icon layout, no description line, no visual card structure.

---

## Changes

### 1. Create `SettingsCardView` reusable ContentView

**New file:** `src/BodyCam/Views/SettingsCardView.xaml`

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="BodyCam.Views.SettingsCardView">

    <Border StrokeShape="RoundRectangle 12"
            Stroke="{AppThemeBinding Light=#E0E0E0, Dark=#333333}"
            BackgroundColor="{AppThemeBinding Light=#FFFFFF, Dark=#2A2A2A}"
            Padding="0"
            HeightRequest="72">
        <!-- Button overlay for UIA Invoke support -->
        <Button x:Name="CardButton"
                AutomationId="{Binding Source={RelativeSource AncestorType={x:Type ContentView}}, Path=CardAutomationId}"
                Clicked="OnCardClicked"
                BackgroundColor="Transparent"
                Padding="0">
            <Button.Content>
                <Grid ColumnDefinitions="48,*" Padding="16,0">
                    <Label Text="{Binding Source={RelativeSource AncestorType={x:Type ContentView}}, Path=Icon}"
                           FontSize="24"
                           VerticalOptions="Center"
                           HorizontalOptions="Center" />
                    <VerticalStackLayout Grid.Column="1" VerticalOptions="Center" Spacing="2">
                        <Label Text="{Binding Source={RelativeSource AncestorType={x:Type ContentView}}, Path=CardTitle}"
                               FontSize="16" FontAttributes="Bold"
                               TextColor="{AppThemeBinding Light=#333333, Dark=#E0E0E0}" />
                        <Label Text="{Binding Source={RelativeSource AncestorType={x:Type ContentView}}, Path=Description}"
                               FontSize="12"
                               TextColor="{AppThemeBinding Light=#666666, Dark=#999999}" />
                    </VerticalStackLayout>
                </Grid>
            </Button.Content>
        </Button>
    </Border>
</ContentView>
```

**New file:** `src/BodyCam/Views/SettingsCardView.xaml.cs`

```csharp
namespace BodyCam.Views;

public partial class SettingsCardView : ContentView
{
    public static readonly BindableProperty CardAutomationIdProperty =
        BindableProperty.Create(nameof(CardAutomationId), typeof(string), typeof(SettingsCardView));

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(SettingsCardView));

    public static readonly BindableProperty CardTitleProperty =
        BindableProperty.Create(nameof(CardTitle), typeof(string), typeof(SettingsCardView));

    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(SettingsCardView));

    public string CardAutomationId
    {
        get => (string)GetValue(CardAutomationIdProperty);
        set => SetValue(CardAutomationIdProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string CardTitle
    {
        get => (string)GetValue(CardTitleProperty);
        set => SetValue(CardTitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public event EventHandler? CardClicked;

    public SettingsCardView()
    {
        InitializeComponent();
    }

    private void OnCardClicked(object? sender, EventArgs e)
    {
        CardClicked?.Invoke(this, e);
    }
}
```

### 2. Update `SettingsPage.xaml` — Use SettingsCardView

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:BodyCam.ViewModels"
             xmlns:views="clr-namespace:BodyCam.Views"
             x:Class="BodyCam.Pages.SettingsPage"
             x:DataType="vm:SettingsViewModel"
             Title="{Binding Title}">

    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="12">

            <Label Text="Changes take effect on next session start."
                   FontSize="12" TextColor="Gray" FontAttributes="Italic" />

            <views:SettingsCardView CardAutomationId="ConnectionSettingsCard"
                                    Icon="🔗" CardTitle="Connection"
                                    Description="Provider, API key, models"
                                    CardClicked="OnConnectionTapped" />

            <views:SettingsCardView CardAutomationId="VoiceSettingsCard"
                                    Icon="🎙" CardTitle="Voice &amp; AI"
                                    Description="Voice, turn detection, instructions"
                                    CardClicked="OnVoiceTapped" />

            <views:SettingsCardView CardAutomationId="DeviceSettingsCard"
                                    Icon="📱" CardTitle="Devices"
                                    Description="Camera, microphone, speaker"
                                    CardClicked="OnDevicesTapped" />

            <views:SettingsCardView CardAutomationId="AdvancedSettingsCard"
                                    Icon="⚙" CardTitle="Advanced"
                                    Description="Debug, diagnostics, tools"
                                    CardClicked="OnAdvancedTapped" />

        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

### 3. Update `SettingsPage.xaml.cs` — Change event signature

```csharp
// Event handlers change from (object?, EventArgs) to (object?, EventArgs)
// — same signature. No code change needed.
```

---

## Files Changed

| File | Action |
|---|---|
| `src/BodyCam/Views/SettingsCardView.xaml(.cs)` | **Create** |
| `src/BodyCam/Pages/SettingsPage.xaml` | Replace 4 plain Buttons with `SettingsCardView` |

## UI Test Impact

- `CardAutomationId` propagates to the inner `Button` → FlaUI finds the same AutomationId
- `Button<SettingsPage>` PageObject type still works (inner element is a Button)
- **Zero test changes expected**

## Design Note

If `Button.Content` binding via `RelativeSource` doesn't work on all platforms, simplify by putting the AutomationId on the `Border` instead and using a `TapGestureRecognizer` on the `Border` combined with an invisible overlay `Button`:

```xml
<Grid>
    <Border ...> <!-- visual card --> </Border>
    <Button AutomationId="ConnectionSettingsCard"
            BackgroundColor="Transparent"
            Opacity="0.01"
            Clicked="OnConnectionTapped" />
</Grid>
```

This ensures FlaUI has an invokable target regardless of visual structure.

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
Get-Process -Name "BodyCam*" -EA SilentlyContinue | Stop-Process -Force
dotnet test src/BodyCam.UITests --no-build --filter "FullyQualifiedName~TabNavigationTests"
Get-Process -Name "BodyCam*" -EA SilentlyContinue | Stop-Process -Force
dotnet test src/BodyCam.UITests --no-build --filter "FullyQualifiedName~ProviderTests"
```
