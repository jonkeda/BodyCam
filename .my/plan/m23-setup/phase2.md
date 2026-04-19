# M23 Phase 2 — App Routing & Permission Guards

**Goal:** Wire SetupPage into the app lifecycle and guard existing code against missing permissions.

---

## Files to Modify

### `App.xaml.cs`

Route to `SetupPage` or `AppShell` based on `SetupCompleted`:

```csharp
protected override Window CreateWindow(IActivationState? activationState)
{
    var settings = Handler?.MauiContext?.Services.GetRequiredService<ISettingsService>();
    
    // On Windows, skip setup (no runtime permissions needed)
    // On Android/iOS, show setup if not completed
#if ANDROID || IOS
    if (settings is not null && !settings.SetupCompleted)
        return new Window(new SetupPage(/* DI */));
#endif
    
    return new Window(new AppShell());
}
```

**Problem:** `SetupPage` needs DI-injected ViewModel. Solution: resolve from `IServiceProvider` via `Handler.MauiContext.Services`.

**Alternative (simpler):** Use `AppShell` always, but register SetupPage as a route. On startup navigate to `//SetupPage` if needed. On completion navigate to `//MainPage`.

**Chosen approach:** Add SetupPage as a ShellContent route. In `AppShell.OnNavigated` or `App.OnStart`, redirect to setup if `SetupCompleted == false`. This keeps DI injection working through Shell's `ContentTemplate`.

---

### `AppShell.xaml`

Add SetupPage route:

```xml
<Tab>
    <ShellContent
        Title="Setup"
        ContentTemplate="{DataTemplate local:SetupPage}"
        Route="SetupPage" />
</Tab>
```

Hide the tab bar on SetupPage: `Shell.TabBarIsVisible="False"`.

On setup completion, navigate to `//MainPage` and hide the setup tab.

---

### `AppShell.xaml.cs`

Add startup redirect logic:

```csharp
protected override async void OnNavigated(ShellNavigatedEventArgs args)
{
    base.OnNavigated(args);
    // On first navigation, check if setup is needed
    if (!_checkedSetup)
    {
        _checkedSetup = true;
        var settings = Handler?.MauiContext?.Services.GetRequiredService<ISettingsService>();
        if (settings is not null && !settings.SetupCompleted)
            await GoToAsync("//SetupPage");
    }
}
```

---

### `MainPage.xaml.cs` — Permission Guards

The `Loaded` handler currently requests BT permission inline. With the setup flow, permissions are already granted (or denied). Change the guard to **check only, don't request**:

```csharp
#elif ANDROID
    var btStatus = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
    if (btStatus == PermissionStatus.Granted)
    {
        // BT code...
    }
#endif
```

Remove the `RequestAsync` call — that's now handled by `SetupPage`.

---

### `ServiceExtensions.cs`

Register `SetupViewModel` and `SetupPage`:

```csharp
services.AddTransient<SetupViewModel>();
services.AddTransient<SetupPage>();
```

---

## Files Already Modified (Phase 0 — done)

| File | Change | Status |
|---|---|---|
| `ISettingsService.cs` | Added `SetupCompleted` property | ✅ Done |
| `SettingsService.cs` | Implemented `SetupCompleted` via `Preferences` | ✅ Done |

---

## SetupPage → MainPage Transition

When `SetupViewModel` finishes:
1. Sets `ISettingsService.SetupCompleted = true`
2. Calls `Shell.Current.GoToAsync("//MainPage")`

---

## Reset Setup (Settings)

Add a button to `SettingsPage.xaml`:

```xml
<Button Text="Re-run Setup" Command="{Binding ResetSetupCommand}" />
```

`SettingsViewModel.ResetSetupCommand`:
```csharp
_settings.SetupCompleted = false;
Shell.Current.GoToAsync("//SetupPage");
```

---

## Platform Behavior

| Platform | Setup Flow |
|---|---|
| **Android** | Full flow: Mic → Camera → BT → API Key |
| **Windows** | API Key step only (permissions not needed) |
| **iOS** | Full flow (future — same as Android) |
