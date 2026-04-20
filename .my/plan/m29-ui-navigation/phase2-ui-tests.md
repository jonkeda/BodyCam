# M29 Phase 2 — UI Test Navigation Updates

**Status:** NOT STARTED  
**Depends on:** Phase 1 (Shell navigation refactor)

---

## Goal

Update the UI test project to work with push-based navigation instead of
TabBar switching. The fixture's `NavigateToHome` / `NavigateToSettings` helpers
and the `TabNavigationTests` class need to change. Settings sub-page tests
should remain largely untouched since they already use pushed routes.

---

## Impact Assessment

### Tests That Break

| File | Reason | Action |
|------|--------|--------|
| `TabNavigationTests.cs` | Tests TabBar round-trip switching | **Rewrite** to test push/pop navigation |
| `BodyCamFixture.cs` | `NavigateToHome()` / `NavigateToSettings()` use NavIcon toggle | **Update** navigation helpers |

### Tests That Keep Working

| File | Reason |
|------|--------|
| `TabSwitchingTests.cs` | Tests MainPage's internal Transcript/Camera tab buttons — **not Shell tabs**. No change needed. |
| `SettingsHubTests.cs` | Uses `NavigateToSettings()` helper — works once fixture is updated |
| `VoiceSettingsTests.cs` etc. | Uses `NavigateToSettingsSubPage()` — works once fixture is updated |
| All `MainPage/` tests | Use `NavigateToHome()` — works once fixture is updated |

---

## Wave 1: Update BodyCamFixture Navigation Helpers

### 1.1 NavigateToSettings()

Currently uses `ClickNavIcon()` toggle. After Phase 1, the ⚙ button only
pushes Settings (no toggle). The back button returns to MainPage.

```csharp
public void NavigateToSettings()
{
    if (_settingsPage.IsLoaded(2000)) return;

    // Ensure we're on MainPage first (⚙ button only visible there)
    NavigateToHome();
    _mainPage.NavIcon.Click();
    _settingsPage.WaitReady(5000);
}
```

### 1.2 NavigateToHome()

With push-based navigation, if we're on a pushed page we need to go back.
The NavIcon is hidden on pushed pages; use the Shell back button instead.

```csharp
public void NavigateToHome()
{
    if (_mainPage.IsLoaded(2000)) return;

    // On a pushed page — click back button to return
    ClickBackButton();
    if (_mainPage.IsLoaded(3000)) return;

    // May need multiple backs (e.g. sub-page → settings → home)
    ClickBackButton();
    _mainPage.IsLoaded(5000);
}

private void ClickBackButton()
{
    // Shell back button — platform-specific AutomationId
    // Windows: "NavigationViewBackButton" (WinUI NavigationView)
    // Alternatively, use keyboard Back or find the back element
    var backButton = Context.FindElement("NavigationViewBackButton", 3000);
    backButton?.Click();
}
```

> **Note:** The exact AutomationId of Shell's back button on Windows needs
> verification during implementation. It may be `"NavigationViewBackButton"`,
> `"BackButton"`, or accessible via `Context.GoBack()` if Brinell provides it.
> Test empirically.

### 1.3 NavigateToSettingsSubPage()

No changes expected — it calls `NavigateToSettings()` then clicks the card.
The sub-page push/pop was already working with Shell routes.

### 1.4 Remove ClickNavIcon Toggle Logic

The old `ClickNavIcon()` method tried both `_mainPage.NavIcon` and
`_settingsPage.NavIcon` because the toggle could fire from either page. Since
NavIcon is now only on MainPage, simplify:

```csharp
private void ClickNavIcon()
{
    _mainPage.NavIcon.WaitExists(true, 5000);
    _mainPage.NavIcon.Click();
}
```

---

## Wave 2: Rewrite TabNavigationTests

Rename to `NavigationTests.cs` and rewrite to test push/pop semantics.

```
Tests/Navigation/NavigationTests.cs  (replaces TabNavigationTests.cs)
```

```csharp
[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Feature", "Navigation")]
public class NavigationTests
{
    private readonly BodyCamFixture _fixture;

    public NavigationTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void NavigateToSettings_ShowsSettingsPage()
    {
        _fixture.NavigateToSettings();
        Assert.True(_fixture.SettingsPage.IsLoaded());
    }

    [Fact]
    public void NavigateBackFromSettings_ShowsMainPage()
    {
        _fixture.NavigateToSettings();
        _fixture.NavigateToHome();
        Assert.True(_fixture.MainPage.IsLoaded());
    }

    [Fact]
    public void Navigation_RoundTrip_AllPagesLoad()
    {
        _fixture.NavigateToHome();
        Assert.True(_fixture.MainPage.IsLoaded());

        _fixture.NavigateToSettings();
        Assert.True(_fixture.SettingsPage.IsLoaded());

        _fixture.NavigateToHome();
        Assert.True(_fixture.MainPage.IsLoaded());
    }

    [Fact]
    public void SettingsSubPage_BackTwice_ReturnsToMainPage()
    {
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.ConnectionSettingsCard.Click(),
            _fixture.ConnectionSettingsPage);

        // Back to Settings
        _fixture.NavigateToHome(); // Will click back twice internally
        Assert.True(_fixture.MainPage.IsLoaded());
    }
}
```

---

## Wave 3: Remove NavIcon from SettingsPage Page Object

After Phase 1, the ⚙ button is hidden on pushed pages. Remove the `NavIcon`
property from `SettingsPage.cs` since it's no longer used there:

```csharp
// Remove this line from SettingsPage.cs:
// public Button<SettingsPage> NavIcon => Button("NavIcon");
```

The `NavIcon` remains only on `MainPage.cs`.

---

## Wave 4: DismissSetupIfShown Updates

The fixture's `DismissSetupIfShown()` works by clicking "Next" buttons on
SetupPage until MainPage loads. With push-based navigation, SetupPage is pushed
on top of MainPage. After the last "Next" click, SetupPage pops and reveals
MainPage — same end result.

**No changes expected**, but verify that:
- The setup page's completion handler calls `GoToAsync("..")` or `PopAsync()`
- The fixture's loop still terminates correctly

---

## Verification

- [ ] `NavigateToHome()` returns to MainPage from any pushed page
- [ ] `NavigateToSettings()` pushes SettingsPage from MainPage
- [ ] `NavigateToSettingsSubPage()` still reaches sub-pages correctly
- [ ] `TabSwitchingTests` (Transcript/Camera tabs) still pass — not affected
- [ ] `NavigationTests` (renamed) all pass
- [ ] All `SettingsPage/` tests pass with updated fixture
- [ ] All `MainPage/` tests pass with updated fixture
- [ ] `DismissSetupIfShown()` still works for first-launch flow
