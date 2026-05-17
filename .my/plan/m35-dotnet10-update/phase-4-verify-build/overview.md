# Phase 4 — Verify build & workload notes

**Status:** Complete
**Depends on:** Phase 1, Phase 2, Phase 3
**Sibling phases:** [Phase 1 — Pin SDK](../phase-1-pin-sdk/overview.md), [Phase 2 — Fix CS0618 warnings](../phase-2-fix-cs0618/overview.md), [Phase 3 — NuGet update](../phase-3-nuget-update/overview.md)

---

## Summary

Confirm clean build across all Windows-buildable targets and document the
workload upgrade path. This is the final validation phase before closing M35.

---

## What to do

### 4.1 — Build all projects on Windows

```powershell
dotnet build BodyCam.sln -c Release -f net10.0-windows10.0.19041.0
dotnet build src/BodyCam.Tests/BodyCam.Tests.csproj
dotnet build src/BodyCam.IntegrationTests/BodyCam.IntegrationTests.csproj
dotnet build src/BodyCam.UITests/BodyCam.UITests.csproj
```

### 4.2 — Workload upgrade note

The installed workloads (`android 36.1.43`, `ios 26.2.10233`,
`maui-windows 10.0.20`) are VS-managed and tied to the VS 18.6 release. To
upgrade workloads independently of VS:

```powershell
dotnet workload update
```

This may require admin privileges and a stable SDK installed (not a preview).
Workload updates are **optional** for this milestone — the current versions
build and run correctly. Track as a follow-up if a specific workload bug
requires a newer manifest.

### 4.3 — Smoke-test app launch

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -c Debug
# then run the app manually from the bin output
```

Confirm the app launches on Windows without crash after the SDK pin.

---

## Acceptance

- [ ] Zero build errors across all Windows-targeted projects.
- [ ] Zero CS0618 warnings in `BodyCam.csproj` build output.
- [ ] App launches on Windows without crash.
