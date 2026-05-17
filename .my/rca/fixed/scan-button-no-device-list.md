# RCA: Scan button does not show device list

**Date**: 2026-05-16

## Symptom

After fixing BLE scan filters to include `M01` prefix, clicking the scan button
in the BodyCam app does not display a list of discovered devices, even though the
glasses are advertising and visible to raw BLE scans.

## Investigation Needed

1. Does `ScanAsync` actually run and find devices?
2. Is the device list bound to the UI correctly?
3. Does the scan complete silently without populating the collection?

## Status

Under investigation.
