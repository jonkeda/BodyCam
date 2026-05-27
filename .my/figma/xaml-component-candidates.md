# XAML Component Candidates for Figma

**Generated:** 2026-05-21

---

## Selection Criteria

- Appears on multiple screens, OR
- Has distinct visual states, OR
- Is complex enough to warrant a reusable Figma component.

**Not componentized:** plain Labels, VerticalStackLayout containers, ScrollView wrappers, Grid layout scaffolds. These are layout primitives handled with Figma auto-layout.

---

## Primitive Controls

These mirror styled MAUI controls used across the app.

| Component | Based on | Variants | Notes |
|-----------|----------|----------|-------|
| **Button** | MAUI Button | Primary, Outline, Danger, Disabled, Settings size, Full size | Implicit style + 3 named variants |
| **IconButton** | MAUI Button | Icon-only, icon+text | AppShell nav, clear, dismiss, filter buttons |
| **TextField** | MAUI Entry | Default, Focused, Error, Disabled, Masked (API key) | ConnectionSettings, AdvancedSettings |
| **MultilineTextField** | MAUI Editor | Default, Focused | VoiceSettings system instructions |
| **SelectField** | MAUI Picker | Default, Focused | Models, voice, devices, source profile |
| **RadioGroup** | MAUI RadioButton | Horizontal (provider selection) | ConnectionSettings provider |
| **ToggleRow** | Label + Switch | On, Off, Disabled | AdvancedSettings switches |
| **DisclosureSection** | VerticalStackLayout + tap | Collapsed, Expanded | DeviceSettings glasses panel |
| **SegmentedTabs** | Grid + 2 Buttons | Tab 1 active, Tab 2 active | MainPage Transcript/Camera tabs |
| **ProgressIndicator** | MAUI ProgressBar | Indeterminate, Percent | Import progress, battery |

---

## Product Components

BodyCam-specific components built from primitives above.

| Component | Screen(s) | States | Source XAML |
|-----------|-----------|--------|------------|
| **StatusBar** | Home | Off, On, Listening (+ segment colors from binding) | StatusBarView.xaml |
| **StateSegmentButton** | Home (StatusBar) | Selected, Unselected, each of 3 states | StatusBarView.xaml |
| **QuickActionButton** | Home | Default, Pressed, Disabled | QuickActionsView.xaml |
| **TranscriptEntry** | Home | User text, AI text, Thinking, Scan/tool result, Image+caption, Inline actions, Error | TranscriptView.xaml DataTemplate |
| **CameraPreviewSurface** | Home | Initializing, Active, Hidden | CameraTabView.xaml |
| **SnapshotOverlay** | Home | Visible (image+caption+dismiss), Hidden | CameraTabView.xaml |
| **ScanResultOverlay** | Home | Visible (content+actions), Hidden | ScanResultOverlay.xaml |
| **SettingsCard** | Settings Hub | Default (with icon, title, description) | SettingsCardView.xaml |
| **ConnectionTestPanel** | Connection Settings | Idle, Testing, Connected, Failed | ConnectionSettingsPage.xaml (button + status) |
| **ModelStatusRow** | Connection Settings | Valid, Invalid, Loading | ConnectionSettingsPage.xaml (picker + status label) |
| **DeviceStatusHeader** | Devices | Connected (green dot), Disconnected (gray dot) | GlassesPage.xaml status frame |
| **ExpandableDeviceSection** | Devices | Collapsed, Expanded (with details grid) | DeviceSettingsPage.xaml glasses panel |
| **DeviceListItem** | Glasses | Name, Address, RSSI | GlassesPage.xaml CollectionView template |
| **InfoBanner** | Devices | Blue info frame (auto-switch notification) | DeviceSettingsPage.xaml |
| **SetupStepCard** | Setup | Current step with icon/title/desc, status (granted/denied/skipped) | SetupPage.xaml |
| **MediaTile** | Media Gallery | Photo, Video (with ▶), Audio (with 🎙️), with duration | MediaGalleryPage.xaml DataTemplate |
| **FilterBar** | Media Gallery | All/Photos/Videos/Audio + Refresh, selected state | MediaGalleryPage.xaml button row |

---

## Components NOT Recommended

These are too simple or too page-specific to justify a Figma component:

| Element | Reason |
|---------|--------|
| Section header label | Just a text style (`SettingsSectionHeader`) |
| Detail label/value pair | Text style pair, not a component |
| Page ScrollView wrapper | Layout primitive |
| Debug overlay | Dev-only, not designed for users |
| Audio player page | Too simple (3 elements) |
| Image viewer page | Single image element |

---

## Pattern Notes

### Repeated Layout Patterns
1. **Settings page pattern:** `ScrollView → VStack (MaxWidth 500) → [SectionHeader + controls] × N` — used by Connection, Voice, Devices, Advanced. Not a component, but a layout convention.
2. **Labeled picker row:** Section header + Picker — repeated ~10 times across settings pages. Could be a row component but might over-componentize.
3. **Button row:** HorizontalStackLayout of buttons — appears in Connection (key management), Devices (actions), Glasses (scan controls). Too varied to standardize.

### State Patterns
- **Connection state:** dots/colors/labels repeated in Glasses, Devices, StatusBar — should share a color token set for connected/disconnected/error.
- **Light/Dark theming:** Every hardcoded color appears as `AppThemeBinding Light=X, Dark=Y` pairs — Figma should model both modes.
- **Conditional sections:** Many sections use `IsVisible` binding — Figma should show both visible and hidden states.
