# RCA: "Debugging type 'dotnet' is currently unsupported"

## Symptom

When launching the BodyCam MAUI app from Visual Studio 2026 Insiders (18.7.0) using F5, a dialog appears:

> **Debugging type 'dotnet' is currently unsupported.**

The app does not start.

## Environment

- Visual Studio Professional 2026 (18.7.0-insiders)
- .NET MAUI project targeting `net10.0-windows10.0.19041.0`
- Launch profile: `Windows Machine` with `commandName: "Project"`

## Investigation

### What was tried

1. Changed `commandName` from `"Project"` to `"MsixPackage"` — same error.
2. Added `"debugType": "managed"` — same error.

### What was NOT yet investigated

- [ ] Whether the .NET MAUI workload for VS 2026 is fully installed (`dotnet workload list`)
- [ ] Whether the correct Windows App SDK / WinUI debug engine is present
- [ ] Whether `launchSettings.json` is even the file VS 2026 reads (could be `.launchSettings.json` or a VS-internal profile)
- [ ] Whether there is a required VS component missing (e.g., ".NET MAUI development" workload in VS Installer)
- [ ] Whether the project needs a `<WindowsPackageType>None</WindowsPackageType>` property for unpackaged debugging
- [ ] Whether the net10.0 TFM requires a newer Windows SDK target

## Likely Root Causes (ranked)

1. **Missing MAUI workload or debug component** — VS 2026 Insiders may not have the MAUI debugging engine installed. The string `'dotnet'` refers to the new .NET debug adapter type which may require an optional VS component.
2. **net10.0 preview incompatibility** — The project targets `net10.0` which is in preview; the debug engine in VS 2026 Insiders may not yet support this TFM for MAUI Windows.
3. **Packaged vs unpackaged mismatch** — The debug launch may expect an MSIX-packaged app but the project configuration doesn't match.

## Recommended Next Steps

1. Run `dotnet workload list` to confirm `maui-windows` is installed.
2. Open **VS Installer → Modify → Individual Components** and verify ".NET Multi-platform App UI development" is checked.
3. Check **Tools → Options → Preview Features** for any MAUI/debugger-related flags.
4. Try adding to the csproj:
   ```xml
   <WindowsPackageType>None</WindowsPackageType>
   ```
   to switch to unpackaged mode and see if the debugger accepts that.
5. Check VS release notes / known issues for 18.7.0-insiders for MAUI debugging support status.

## Status

🔴 Unresolved — needs workload and component verification.
