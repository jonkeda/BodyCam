# M49 Phase 1 - Plugin Integration And Platform Setup

**Status:** Proposed
**Goal:** Add `Plugin.Maui.OCR` to the MAUI app, initialize it correctly, and
make the platform manifests ready for native OCR.

## Implementation

### Package

Add the package to `src/BodyCam/BodyCam.csproj`.

```xml
<PackageReference Include="Plugin.Maui.OCR" Version="1.1.1" />
```

Pin the version that is validated during implementation. The NuGet page showed
`1.1.1` as the current package when this plan was written on 2026-06-03.

Because BodyCam currently targets `net10.0-*`, verify the package restores and
builds against:

- `net10.0-windows10.0.19041.0`
- `net10.0-android`
- `net10.0-ios`
- `net10.0-maccatalyst`

If a target fails, keep the BodyCam-owned OCR abstraction and gate only the
plugin implementation while the package compatibility issue is resolved.

### MAUI Builder

In `src/BodyCam/MauiProgram.cs`, add the plugin namespace and chain `.UseOcr()`
near the other MAUI builder extensions:

```csharp
using Plugin.Maui.OCR;

builder
    .UseMauiApp<App>()
    .UseMauiCommunityToolkitCamera()
    .UseOcr()
    .ConfigureFonts(...);
```

Keep the existing builder shape readable. It is fine if `.UseOcr()` is placed
after `.ConfigureFonts(...)`; the important point is that the plugin runs before
`builder.Build()`.

### Android Manifest

Add the ML Kit dependency metadata inside the existing `<application>` element
in `src/BodyCam/Platforms/Android/AndroidManifest.xml`:

```xml
<application ...>
  <meta-data
      android:name="com.google.mlkit.vision.DEPENDENCIES"
      android:value="ocr" />
</application>
```

The app already declares camera permission. This metadata asks Google Play
services / ML Kit to install the OCR model with the app, which avoids a slow or
failed first OCR attempt on fresh installs.

### Platform Permissions

No new camera permission should be required beyond the existing camera capture
flow. Still verify:

- Android camera permission request path still runs before capture.
- iOS `Info.plist` camera usage strings remain valid.
- Windows camera access remains tied to the existing capture service.

## Acceptance

- `dotnet restore BodyCam.sln` succeeds with the OCR package.
- `MauiProgram.cs` initializes the plugin using `.UseOcr()`.
- Android manifest contains ML Kit OCR dependency metadata in `<application>`.
- The app still builds for at least Windows after the package is added.
- Any target-framework incompatibility is documented before moving to Phase 2.

## Risks

- The plugin may not yet be tested by its maintainers against BodyCam's .NET 10
  target frameworks.
- Android ML Kit model availability can behave differently between emulator,
  sideloaded APK, and Play-installed builds.
- Windows OCR language support depends on installed OS OCR languages.
