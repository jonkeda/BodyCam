# M35 — Update to Latest Stable .NET 10

**Status:** In progress
**Depends on:** M33 (HeyCyan SDK, landed)

## Context

The BodyCam solution currently builds with the **preview SDK
`10.0.300-preview.0.26177.108`** (installed via Visual Studio 18.6). A stable
.NET 10 patch is available (`10.0.107`) and the workload manifests are pinned
to `10.0.100`. There is no `global.json` in the repo root, so SDK selection
drifts with whatever Visual Studio installs.

All projects already target `net10.0-*` TFMs — no TFM changes are needed.

### Current state (verified)

| Area | State |
|---|---|
| SDK in use | `10.0.300-preview.0.26177.108` (VS-managed, no `global.json`) |
| Latest stable SDK | `10.0.107` (`dotnet sdk check` output) |
| Workload manifests | `10.0.100` (android 36.1.43, ios 26.2.10233, maui-windows 10.0.20) |
| NuGet packages | `Azure.Monitor.OpenTelemetry.Exporter` 1.7.0 → 1.8.0 available; all others current |
| CS0618 warnings | 4 obsolete MAUI view-extension calls in `MainPage.xaml.cs` |
| CS0067 warnings | ~10 unused event fields across provider classes (interface contract) |

---

## Goals

1. **Reproducible builds** — pin SDK via `global.json` so CI and all
   developer machines use the same SDK version.
2. **Stable runtime** — stop relying on preview/RC SDK bits; use the latest
   stable `.NET 10` patch.
3. **Zero build warnings** — resolve all CS0618 (obsolete MAUI API) warnings.
4. **Updated packages** — bump the one genuinely outdated NuGet package.
5. **Workload alignment** — document how to bring workloads in sync with the
   pinned SDK (VS-managed workloads vs `dotnet workload update`).

---

## Phases

- [Phase 1 — Pin SDK with global.json](phase-1-pin-sdk/overview.md)
- [Phase 2 — Fix CS0618 obsolete MAUI API warnings](phase-2-fix-cs0618/overview.md)
- [Phase 3 — NuGet package update](phase-3-nuget-update/overview.md)
- [Phase 4 — Verify build & workload notes](phase-4-verify-build/overview.md)
- [Phase 5 — Fix iOS TFM build errors](phase-5-fix-ios-build/overview.md)

---

## Phase 1 — Pin SDK with `global.json`

**Goal:** Make SDK selection explicit and reproducible.

### What to do

- Create `e:\repos\Private\BodyCam\global.json` with:

  ```json
  {
    "sdk": {
      "version": "10.0.107",
      "rollForward": "latestPatch"
    }
  }
  ```

  `latestPatch` allows automatic uptake of future `10.0.1xx` patches while
  rejecting feature-band upgrades (`10.0.2xx`) until we explicitly bump it.

- `10.0.107` must be installed on the machine. If not present, install it via
  the [.NET download page](https://dot.net) or `winget install
  Microsoft.DotNet.SDK.10`.

### Notes

- Brinell (the sub-repo at `Brinell/`) has its own `global.json` pinned to
  `10.0.100` with `latestFeature` — leave it untouched.
- The BodyCam `global.json` sits at the solution root and covers
  `src/BodyCam/`, all test projects, and `BodyCam.sln`.
- CI (if added later) should install the exact SDK version from `global.json`
  via `uses: actions/setup-dotnet` or `dotnet-install.sh`.

### Acceptance

- `dotnet --version` in the repo root resolves to `10.0.107` (or a later
  `10.0.1xx` patch).
- `dotnet build BodyCam.sln` succeeds with no errors.

---

## Phase 2 — Fix CS0618 obsolete MAUI API warnings

**Goal:** Resolve all four `[Obsolete]` warnings in
[src/BodyCam/Pages/Main/MainPage.xaml.cs](../../src/BodyCam/Pages/Main/MainPage.xaml.cs).

### What to do

Replace the deprecated view-extension overloads with their `*Async` equivalents:

| Line | Old call | New call |
|---|---|---|
| ~93 | `element.FadeTo(1, 250, Easing.CubicOut)` | `element.FadeToAsync(1, 250, Easing.CubicOut)` |
| ~94 | `element.TranslateTo(0, 0, 250, Easing.CubicOut)` | `element.TranslateToAsync(0, 0, 250, Easing.CubicOut)` |
| ~115 | `await dots[i].FadeTo(1.0, 200)` | `await dots[i].FadeToAsync(1.0, 200)` |
| ~116 | `await dots[i].FadeTo(0.3, 200)` | `await dots[i].FadeToAsync(0.3, 200)` |

The return types and signatures are identical — `FadeToAsync` returns `Task<bool>`,
`TranslateToAsync` returns `Task<bool>`. The rename is mechanical.

### Notes

- Do **not** add `#pragma warning disable CS0618` — fix the root cause.
- The CS0067 (unused event) warnings in provider classes are interface-contract
  stubs and are intentionally left unimplemented — suppress them with
  `#pragma warning disable CS0067` at field level, or leave as-is (they do not
  block the build in Release mode unless `TreatWarningsAsErrors` is enabled).

### Acceptance

- `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0`
  produces **0 CS0618 warnings**.

---

## Phase 3 — NuGet package update

**Goal:** Bring the one outdated package to its latest stable release.

### What to do

In [src/BodyCam/BodyCam.csproj](../../src/BodyCam/BodyCam.csproj), change:

```xml
<!-- Before -->
<PackageReference Include="Azure.Monitor.OpenTelemetry.Exporter" Version="1.*" />

<!-- After — no change needed; wildcard already resolves to latest 1.x -->
```

The `Version="1.*"` wildcard already resolves to `1.8.0` at restore time once
NuGet cache is cleared. No `.csproj` edit is required unless you want to pin
the exact patch version. To force the update:

```powershell
dotnet add src/BodyCam/BodyCam.csproj package Azure.Monitor.OpenTelemetry.Exporter
```

This will update the lock file / packages cache to `1.8.0`.

### Notes

- All other packages use wildcards (`10.0.*`, `2.*`, `3.*`, `6.*`) that are
  already resolving to current latest. No further action needed.

### Acceptance

- `dotnet list src/BodyCam/BodyCam.csproj package --outdated` returns no rows.

---

## Phase 4 — Verify build & workload notes

**Goal:** Confirm clean build across all Windows-buildable targets; document
workload upgrade path.

### What to do

1. Build all projects on Windows:

   ```powershell
   dotnet build BodyCam.sln -c Release -f net10.0-windows10.0.19041.0
   dotnet build src/BodyCam.Tests/BodyCam.Tests.csproj
   dotnet build src/BodyCam.IntegrationTests/BodyCam.IntegrationTests.csproj
   dotnet build src/BodyCam.UITests/BodyCam.UITests.csproj
   ```

2. **Workload upgrade note:** The installed workloads (`android 36.1.43`,
   `ios 26.2.10233`, `maui-windows 10.0.20`) are VS-managed and tied to the
   VS 18.6 release. To upgrade workloads independently of VS:

   ```powershell
   dotnet workload update
   ```

   This may require admin privileges and a stable SDK installed (not a
   preview). Workload updates are **optional** for this milestone — the
   current versions build and run correctly. Track as a follow-up if a
   specific workload bug requires a newer manifest.

3. Confirm the app launches on Windows after the SDK pin:

   ```powershell
   dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -c Debug
   # then run the app manually from the bin output
   ```

### Acceptance

- Zero build errors across all Windows-targeted projects.
- Zero CS0618 warnings in `BodyCam.csproj` build output.
- App launches on Windows without crash.
