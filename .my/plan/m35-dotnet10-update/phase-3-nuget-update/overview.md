# Phase 3 — NuGet package update

**Status:** Complete
**Depends on:** Phase 1 (SDK pin)
**Sibling phases:** [Phase 1 — Pin SDK](../phase-1-pin-sdk/overview.md), [Phase 2 — Fix CS0618 warnings](../phase-2-fix-cs0618/overview.md), [Phase 4 — Verify build](../phase-4-verify-build/overview.md)

---

## Summary

Bring the one outdated NuGet package — `Azure.Monitor.OpenTelemetry.Exporter`
(1.7.0 → 1.8.0) — to its latest stable release. All other packages use
wildcards that already resolve to current latest.

---

## What to do

The `Version="1.*"` wildcard in
[src/BodyCam/BodyCam.csproj](../../../src/BodyCam/BodyCam.csproj) already
resolves to `1.8.0` at restore time once the NuGet cache is refreshed. No
`.csproj` edit is strictly required. To force the update:

```powershell
dotnet add src/BodyCam/BodyCam.csproj package Azure.Monitor.OpenTelemetry.Exporter
```

This will update the packages cache to `1.8.0`.

> If you want to pin the exact version instead of using a wildcard, change
> `Version="1.*"` to `Version="1.8.0"`.

---

## Notes

- All other packages use wildcards (`10.0.*`, `2.*`, `3.*`, `6.*`) that are
  already resolving to current latest. No further action needed.

---

## Acceptance

- [ ] `dotnet list src/BodyCam/BodyCam.csproj package --outdated` returns no
      outdated rows.
