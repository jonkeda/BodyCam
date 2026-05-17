# Phase 5 — Fix iOS TFM build errors

**Status:** Complete
**Depends on:** Phase 1 (SDK pin)
**Sibling phases:** [Phase 1 — Pin SDK](../phase-1-pin-sdk/overview.md), [Phase 2 — Fix CS0618 warnings](../phase-2-fix-cs0618/overview.md), [Phase 3 — NuGet update](../phase-3-nuget-update/overview.md), [Phase 4 — Verify build](../phase-4-verify-build/overview.md)

---

## Summary

The `net10.0-ios` target fails to build due to API changes between the .NET 9
and .NET 10 iOS bindings. Two files have errors:

| File | Line(s) | Error | Root cause |
|---|---|---|---|
| [PlatformMicProvider.cs](../../../src/BodyCam/Platforms/iOS/PlatformMicProvider.cs#L148) | 148 | CS0246: `AudioBuffer` not found | Missing `using AudioToolbox;` |
| [IosMediaStore.cs](../../../src/BodyCam/Platforms/iOS/HeyCyan/IosMediaStore.cs#L40) | 40 | `CreationRequestForAssetFromImage` not found | Convenience method removed in .NET 10 iOS bindings |
| [IosMediaStore.cs](../../../src/BodyCam/Platforms/iOS/HeyCyan/IosMediaStore.cs#L80) | 80 | `CreationRequestForAssetFromVideo` not found | Convenience method removed in .NET 10 iOS bindings |

---

## What to do

### 5.1 — Fix `PlatformMicProvider.cs`

Add the missing `using AudioToolbox;` directive. The `AudioBuffer` struct used
in `ConvertToMono16(AudioBuffer buffer, int frameCount)` at line 148 lives in
the `AudioToolbox` namespace — it was previously resolved implicitly but
requires an explicit import in .NET 10.

### 5.2 — Fix `IosMediaStore.cs`

The convenience factory methods on `PHAssetCreationRequest` were removed in the
.NET 10 iOS bindings:

- `PHAssetCreationRequest.CreationRequestForAssetFromImage(NSUrl)` — **gone**
- `PHAssetCreationRequest.CreationRequestForAssetFromVideo(NSUrl)` — **gone**

Replace with the modern `CreationRequestForAsset()` + `AddResource()` pattern:

```csharp
// Before (.NET 9)
var request = PHAssetCreationRequest.CreationRequestForAssetFromImage(
    NSUrl.FromFilename(tempPath));

// After (.NET 10)
var request = PHAssetCreationRequest.CreationRequestForAsset();
request.AddResource(PHAssetResourceType.Photo,
    NSUrl.FromFilename(tempPath), null);
```

```csharp
// Before (.NET 9)
var request = PHAssetCreationRequest.CreationRequestForAssetFromVideo(
    NSUrl.FromFilename(tempPath));

// After (.NET 10)
var request = PHAssetCreationRequest.CreationRequestForAsset();
request.AddResource(PHAssetResourceType.Video,
    NSUrl.FromFilename(tempPath), null);
```

The `AddResource` overload takes `(PHAssetResourceType, NSUrl,
PHAssetResourceCreationOptions?)` — pass `null` for default options.

### 5.3 — Add missing `SupportedOSPlatformVersion` to iOS bindings project

[BodyCam.HeyCyan.iOS.Bindings.csproj](../../../src/BodyCam.HeyCyan.iOS.Bindings/BodyCam.HeyCyan.iOS.Bindings.csproj)
is missing an explicit `SupportedOSPlatformVersion`. Add it to match the main
app:

```xml
<SupportedOSPlatformVersion>15.0</SupportedOSPlatformVersion>
```

---

## Notes

- These errors are **pre-existing** — they were present before M35 and are
  caused by API changes in the .NET 10 iOS workload, not by the SDK pin.
- The `IosMediaStore.cs` fix may require adding `using Photos;` if not
  already present (it is already imported).
- Windows and Android targets are unaffected — all iOS code is platform-guarded.

---

## Acceptance

- [ ] `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-ios` completes with
      **0 errors**.
- [ ] `dotnet build src/BodyCam.HeyCyan.iOS.Bindings/BodyCam.HeyCyan.iOS.Bindings.csproj`
      completes with **0 errors**.
