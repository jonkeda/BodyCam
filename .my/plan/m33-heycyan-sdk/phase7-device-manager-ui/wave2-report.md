# Wave 2: `GlassesPage` (scan / list / connect / status) — Implemented

## Files created
- [src/BodyCam/Converters/BoolToColorConverter.cs](src/BodyCam/Converters/BoolToColorConverter.cs) — Converts bool to color (Green/Red) for status dot
- [src/BodyCam/Converters/PercentConverter.cs](src/BodyCam/Converters/PercentConverter.cs) — Converts int (0-100) to double (0.0-1.0) for ProgressBar
- [src/BodyCam/ViewModels/GlassesViewModel.cs](src/BodyCam/ViewModels/GlassesViewModel.cs) — Glasses connection UI ViewModel with scan/connect/disconnect commands
- [src/BodyCam/Pages/GlassesPage.xaml](src/BodyCam/Pages/GlassesPage.xaml) — XAML page with status header, device list, and status panel
- [src/BodyCam/Pages/GlassesPage.xaml.cs](src/BodyCam/Pages/GlassesPage.xaml.cs) — Code-behind resolving ViewModel from DI
- [src/BodyCam.Tests/ViewModels/GlassesViewModelTests.cs](src/BodyCam.Tests/ViewModels/GlassesViewModelTests.cs) — 33 unit tests covering ViewModel behavior

## Files changed
- [src/BodyCam/App.xaml](src/BodyCam/App.xaml) — Registered BoolToColorConverter and PercentConverter
- [src/BodyCam/ServiceExtensions.cs](src/BodyCam/ServiceExtensions.cs) — Registered GlassesViewModel, GlassesPage, and route `//glasses`

## Build/Test results
- `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android --no-restore` — PASS (with 101 analyzer warnings, no errors)
- `dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj --filter "FullyQualifiedName~GlassesViewModel" --verbosity minimal` — PASS (33/33 tests succeeded)
- `dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj --filter "FullyQualifiedName~HeyCyan|FullyQualifiedName~GlassesViewModel"` — PASS (256/256 tests succeeded)

## Verify checklist
- [ ] Scan populates the device list with HeyCyan glasses (name, MAC, RSSI) — MANUAL: requires real HeyCyan hardware
- [ ] Connect transitions UI to the status panel within ~3 s — MANUAL: requires real HeyCyan hardware
- [ ] Battery %, MAC, firmware, hardware all render correctly — MANUAL: requires real HeyCyan hardware
- [ ] Charging bolt appears when `IsCharging` is true — MANUAL: requires real HeyCyan hardware
- [ ] Photo / video / audio counts update live as captures occur — MANUAL: requires real HeyCyan hardware
- [ ] Disconnect returns the UI to scan + list — MANUAL: requires real HeyCyan hardware
- [x] `GlassesViewModelTests` pass (xUnit + FluentAssertions) — **VERIFIED** (33/33 tests pass)
- [x] No `CommunityToolkit.Mvvm` references introduced — **VERIFIED** (uses BodyCam.Mvvm only)

## Notes / deviations
- The wave spec showed `CancellationToken` parameters on command methods, but the existing `AsyncRelayCommand` infrastructure doesn't support them. Methods pass `CancellationToken.None` to the manager's async methods instead.
- Unit tests use `FakeHeyCyanSessionWithVersion` and real `HeyCyanGlassesDeviceManager` instances (following the pattern in `HeyCyanGlassesDeviceManagerTests`) rather than mocking the sealed manager class.
- All real hardware verifications are marked as MANUAL — genuine BLE scan, connect, battery, media counts, and live updates require physical HeyCyan glasses per M33 Phase 7 Wave 5 manual checklist.

## Next wave hint
Wave 3: [wave3-shell-battery-widget.md](wave3-shell-battery-widget.md) — battery indicator in AppShell title bar
