---
applyTo: "**/*.xaml"
---

# XAML Instructions — Accessibility & Cross-Platform

## Accessibility

Every interactive element MUST have:
1. `AutomationId` — for UI tests (PascalCase, descriptive: `SaveButton`, `NameEntry`)
2. `SemanticProperties.Description` — what the element is ("Save settings", "User name")
3. `SemanticProperties.Hint` — what happens when activated ("Saves current settings and returns")

```xml
<!-- Correct -->
<Button AutomationId="SaveButton"
        Text="Save"
        SemanticProperties.Description="Save settings"
        SemanticProperties.Hint="Saves current settings and returns to the main page" />

<!-- Wrong — missing accessibility properties -->
<Button Text="Save" />
```

### Headings

Use `SemanticProperties.HeadingLevel` on section labels for screen reader navigation:

```xml
<Label Text="Provider" FontSize="Title" FontAttributes="Bold"
       SemanticProperties.HeadingLevel="Level1" />
```

### Images and Icons

Decorative images: `SemanticProperties.Description=""` (empty string silences screen reader).
Meaningful images: describe what they convey, not what they look like.

```xml
<!-- Decorative -->
<Image Source="divider.png" SemanticProperties.Description="" />

<!-- Meaningful -->
<Image Source="warning.png" SemanticProperties.Description="Warning" />
```

### Emoji as Icons

When using emoji as button text (e.g. `Text="🎤"`), always add `SemanticProperties.Description` — screen readers read emoji inconsistently across platforms.

---

## Cross-Platform Pitfalls

### Do NOT use `TabIndex`

`TabIndex` is not supported on Android in .NET MAUI and causes `XamlParseException` at runtime. The app crashes on launch.

```xml
<!-- WRONG — crashes on Android -->
<Button Text="Save" TabIndex="10" />

<!-- Correct — rely on visual order for tab sequence -->
<Button Text="Save" />
```

If you need custom focus order, use `SemanticOrderView` or arrange elements in the correct visual order in XAML.

### `IsVisible="False"` removes from accessibility tree

Setting `IsVisible="False"` removes the element from both the visual tree AND the UIA/accessibility tree. If you need to hide an element visually but keep it accessible (e.g. a sentinel for automation), use:

```xml
<Label Text="sentinel" HeightRequest="1" Opacity="0.01" />
```

Note: `Opacity="0"` or `HeightRequest="0"` also removes from the accessibility tree on some platforms.

### Color and Contrast

Use `AppThemeBinding` for all text colors to ensure contrast in both light and dark mode:

```xml
<Label TextColor="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray300}}" />
```

Do NOT hardcode colors that only work in one theme.

### Platform-Specific XAML

Use `OnPlatform` only when behavior genuinely differs. Prefer cross-platform properties first.

```xml
<!-- Prefer this -->
<Entry Keyboard="Plain" />

<!-- Only when needed -->
<Entry>
    <Entry.Keyboard>
        <OnPlatform x:TypeArguments="Keyboard">
            <On Platform="Android" Value="Plain" />
            <On Platform="WinUI" Value="Default" />
        </OnPlatform>
    </Entry.Keyboard>
</Entry>
```
