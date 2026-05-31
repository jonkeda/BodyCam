# M42 Phase 4 - Verification

Goal: update tests from the old tabbed model to the m42 one-screen model.

Scope:
- Remove or rewrite tab switching tests.
- Update camera tests to assert inline camera visibility after Look, Read, or Scan rather than after Camera tab selection.
- Update quick-action tests to cover Look, Read, Scan, and the Actions drawer.
- Update status bar tests to expect chip-style mode controls and no transcript/camera tabs.

Acceptance:
- `BodyCam.UITests` compiles.
- Main page tests reflect the new first-page contract.
- Build passes for `src/BodyCam/BodyCam.csproj` on Windows target.
