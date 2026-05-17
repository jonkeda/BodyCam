# Phase 1 — Pin SDK with `global.json`

**Status:** Complete
**Depends on:** None
**Sibling phases:** [Phase 2 — Fix CS0618 warnings](../phase-2-fix-cs0618/overview.md), [Phase 3 — NuGet update](../phase-3-nuget-update/overview.md), [Phase 4 — Verify build](../phase-4-verify-build/overview.md)

---

## Summary

Make SDK selection explicit and reproducible by adding a `global.json` at
the repo root. Today the solution has no `global.json`, so SDK selection
drifts with whatever Visual Studio installs — currently the preview SDK
`10.0.300-preview.0.26177.108`. Pinning to `10.0.107` with `latestPatch`
roll-forward ensures all developer machines and future CI use the same
stable SDK.

---

## What to do

Create `e:\repos\Private\BodyCam\global.json`:

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

If `10.0.107` is not installed, install it via the
[.NET download page](https://dot.net) or:

```powershell
winget install Microsoft.DotNet.SDK.10
```

---

## Notes

- Brinell (the sub-repo at `Brinell/`) has its own `global.json` pinned to
  `10.0.100` with `latestFeature` — leave it untouched.
- The BodyCam `global.json` sits at the solution root and covers
  `src/BodyCam/`, all test projects, and `BodyCam.sln`.
- CI (if added later) should install the exact SDK version from `global.json`
  via `uses: actions/setup-dotnet` or `dotnet-install.sh`.

---

## Acceptance

- [ ] `dotnet --version` in the repo root resolves to `10.0.107` (or a later
      `10.0.1xx` patch).
- [ ] `dotnet build BodyCam.sln` succeeds with no errors.
