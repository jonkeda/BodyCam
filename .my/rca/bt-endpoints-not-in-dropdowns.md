# RCA: Bluetooth Endpoints Not Appearing in App Dropdowns

**Date:** 2025-05-17
**Status:** Root cause confirmed
**Severity:** High — glasses audio unusable on Windows

---

## Symptom

After successfully pairing the M01 Pro glasses via Classic BT (confirmed by
the test app: both "Headset (M01 Pro_E6C9)" capture and "Headphones (M01
Pro_E6C9)" render endpoints appear as Active), the BodyCam app's audio
input/output dropdown menus do not show the glasses.

---

## Root Cause

The M01 Pro audio endpoints are routed through the **Intel Smart Sound
Technology** audio subsystem, NOT the traditional Bluetooth audio driver
stack. The property store shows:

```
[Capture] Headset (M01 Pro_E6C9)
  DEVPKEY_Device_EnumeratorName (#24) = INTELAUDIO     ← NOT "BTHENUM"!
  Instance path (#2) = {1}.INTELAUDIO\CTLR_DEV_7A50&...
                        ↑ No Bluetooth MAC anywhere

[Render] Headphones (M01 Pro_E6C9)
  DEVPKEY_Device_EnumeratorName (#24) = INTELAUDIO     ← NOT "BTHENUM"!
  Instance path (#2) = {1}.INTELAUDIO\CTLR_DEV_7A50&...
```

**Both** `IsBluetoothDevice` and `ExtractMacFromDevice` fail silently:
- `IsBluetoothDevice` checks `EnumeratorName == "BTHENUM"` → returns `false`
- `ExtractMacFromDevice` regex `&([0-9A-Fa-f]{12})_C` → no match

This happens on PCs with Intel Bluetooth controllers that use the SST
(Smart Sound Technology) audio pipeline. Intel routes BT audio through its
own audio controller rather than the traditional `BthA2dp.sys` /
`BthHfpAudio.sys` stack. Other BT headsets on the same PC (1MORE HQ51,
1MORE SonoFlow) show `BTHENUM` — they were likely paired before a driver
update changed the routing.

---

## Fix

### Detection: Cross-reference MMDevice with paired BluetoothDevices

Since the property store doesn't identify these as BT devices, the
enumerators need a fallback path. Cross-reference MMDevice `FriendlyName`
against paired `BluetoothDevice` names to identify BT audio devices that
are routed through non-BTHENUM driver stacks.

### MAC extraction: Use BluetoothDevice.BluetoothAddress

When `ExtractMacFromDevice` fails (no BTHENUM instance path), look up the
paired `BluetoothDevice` by name match and use its `BluetoothAddress`
property for the MAC.
