# Phase 2 — Fix CS0618 obsolete MAUI API warnings

**Status:** Complete
**Depends on:** Phase 1 (SDK pin)
**Sibling phases:** [Phase 1 — Pin SDK](../phase-1-pin-sdk/overview.md), [Phase 3 — NuGet update](../phase-3-nuget-update/overview.md), [Phase 4 — Verify build](../phase-4-verify-build/overview.md)

---

## Summary

Resolve all four `[Obsolete]` CS0618 warnings in
[src/BodyCam/Pages/Main/MainPage.xaml.cs](../../../src/BodyCam/Pages/Main/MainPage.xaml.cs).
The deprecated MAUI view-extension overloads (`FadeTo`, `TranslateTo`) have
`*Async` equivalents with identical signatures and return types. The rename
is mechanical.

---

## What to do

Replace the deprecated calls with their `*Async` equivalents:

| Line | Old call | New call |
|---|---|---|
| ~93 | `element.FadeTo(1, 250, Easing.CubicOut)` | `element.FadeToAsync(1, 250, Easing.CubicOut)` |
| ~94 | `element.TranslateTo(0, 0, 250, Easing.CubicOut)` | `element.TranslateToAsync(0, 0, 250, Easing.CubicOut)` |
| ~115 | `await dots[i].FadeTo(1.0, 200)` | `await dots[i].FadeToAsync(1.0, 200)` |
| ~116 | `await dots[i].FadeTo(0.3, 200)` | `await dots[i].FadeToAsync(0.3, 200)` |

Both `FadeToAsync` and `TranslateToAsync` return `Task<bool>`, matching
the deprecated overloads exactly.

---

## Notes

- Do **not** add `#pragma warning disable CS0618` — fix the root cause.
- The CS0067 (unused event) warnings in provider classes are interface-contract
  stubs and are intentionally left unimplemented — suppress them with
  `#pragma warning disable CS0067` at field level, or leave as-is (they do not
  block the build in Release mode unless `TreatWarningsAsErrors` is enabled).

---

## Acceptance

- [ ] `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0`
      produces **0 CS0618 warnings**.
