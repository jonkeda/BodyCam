# M29 Phase 3 — Settings Page Header Icon

**Status:** NOT STARTED  
**Depends on:** Phase 1 (push-based navigation)

---

## Goal

Add a gear icon (⚙) to the Settings page navigation bar title so users have a
clear visual indicator of which page they're on. Use `Shell.TitleView` on the
SettingsPage to render a styled header with icon + title text, matching the
app's title bar aesthetic.

---

## Wave 1: Shell.TitleView on SettingsPage

Override the Shell navigation bar title on `SettingsPage.xaml` with a
`Shell.TitleView` that shows a gear icon alongside the "Settings" text.

```xml
<Shell.TitleView>
    <HorizontalStackLayout VerticalOptions="Center" Spacing="6" Padding="4,0">
        <Label Text="⚙" FontSize="20" VerticalOptions="Center" />
        <Label Text="Settings" FontSize="20" FontAttributes="Bold"
               VerticalOptions="Center" />
    </HorizontalStackLayout>
</Shell.TitleView>
```

This overrides the global `Shell.TitleView` from AppShell for only the
SettingsPage, while the Shell back arrow remains visible.

---

## Verification

- [ ] Settings page shows ⚙ icon next to "Settings" in the nav bar
- [ ] Back arrow is still visible and functional
- [ ] MainPage still shows the global TitleView (BodyCam + ⚙ button)
- [ ] Settings sub-pages are unaffected
- [ ] `dotnet build` — 0 errors
