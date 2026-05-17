# M33 Phase 6 — Wave 4: Info.plist & Entitlements

## Goal

Add the iOS permissions and capabilities required for the glasses session
to actually run on a real device. Without these, BLE scanning silently
returns no results and `NEHotspotConfigurationManager.ApplyConfiguration`
returns error code 8 (`unauthorized`). The keys here are the minimum
working set — the [iOS demo's](../../../../Alternative-HeyCyan-App-and-SDK/ios/QCSDKDemo/Info.plist)
plist confirms each one.

**Parent phase:** [`../phase6-ios-binding.md`](../phase6-ios-binding.md)
**Prev:** [`wave3-hotspot-http-client.md`](wave3-hotspot-http-client.md)
**Next:** [`wave5-di-and-parity-tests.md`](wave5-di-and-parity-tests.md)

## Steps

1. **Add the Hotspot Configuration entitlement.** Edit (or create)
   `src/BodyCam/Platforms/iOS/Entitlements.plist` and add the developer
   capability key. The provisioning profile must also enable
   **Hotspot Configuration** in the Apple Developer portal — flipping the
   plist alone is not enough.

   ```xml
   <?xml version="1.0" encoding="UTF-8"?>
   <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
       "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
   <plist version="1.0">
   <dict>
       <key>com.apple.developer.networking.HotspotConfiguration</key>
       <true/>
   </dict>
   </plist>
   ```

2. **Reference the entitlements file from the iOS head.** In
   `src/BodyCam/BodyCam.csproj`, ensure the iOS-only `PropertyGroup` sets
   `<CodesignEntitlements>Platforms/iOS/Entitlements.plist</CodesignEntitlements>`
   for `Debug` and `Release`. Without this property, Xamarin signs the
   bundle with no entitlements and the runtime denies the hotspot call.

3. **Add usage descriptions to `Info.plist`.** Edit
   `src/BodyCam/Platforms/iOS/Info.plist`. iOS rejects the binary at App
   Store submission and shows a permission-prompt crash on first launch
   without these strings — even though they are technically optional at
   build time:

   ```xml
   <key>NSBluetoothAlwaysUsageDescription</key>
   <string>BodyCam connects to HeyCyan smart glasses over Bluetooth Low Energy
   to control capture and read battery, firmware, and media counts.</string>

   <key>NSLocalNetworkUsageDescription</key>
   <string>BodyCam joins the glasses' Wi-Fi hotspot to download captured
   photos, videos, and audio recordings.</string>

   <key>NSCameraUsageDescription</key>
   <string>BodyCam captures photos with the phone camera when no glasses
   are connected.</string>

   <key>NSMicrophoneUsageDescription</key>
   <string>BodyCam records audio for live conversations and dictation.</string>
   ```

4. **Add the SSID-readback location key.** Since iOS 13, reading the
   currently-joined SSID via `CNCopyCurrentNetworkInfo` (used by the
   `WiFiTransferManager` demo to confirm the join succeeded) requires
   *When-In-Use* location authorisation. Without it the SSID API returns
   `nil` and the discovery probe in
   [`wave3`](wave3-hotspot-http-client.md) cannot distinguish "joined the
   right network" from "joined the wrong network":

   ```xml
   <key>NSLocationWhenInUseUsageDescription</key>
   <string>BodyCam reads the active Wi-Fi SSID to confirm the glasses
   hotspot is currently joined before downloading media.</string>
   ```

5. **Pre-declare background modes.** BLE scanning during a long capture
   session needs `bluetooth-central`. Without it, scans pause when the app
   suspends and the glasses appear to "drop":

   ```xml
   <key>UIBackgroundModes</key>
   <array>
       <string>bluetooth-central</string>
       <string>audio</string>
   </array>
   ```

6. **Provisioning profile.** Regenerate the profile (Apple Developer
   portal → Identifiers → BodyCam → Capabilities) so the embedded
   profile matches the new entitlement. Old profiles silently strip
   unknown entitlements at install time and you only see the failure as
   `applyConfiguration error 8` at runtime.

7. **Document the permission flow.** Update
   `docs/deployment/ios.md` (creating it if absent) with:

   - First-launch prompts the user will see (BLE → Local Network →
     Microphone → Camera → Location-When-In-Use), in that order.
   - The OS error code 8 troubleshooting note pointing back here.
   - A reminder that the **Hotspot Configuration** capability is paid-tier
     only — the free Apple ID profile cannot sign it.

## Verify

- [ ] `Platforms/iOS/Entitlements.plist` contains
      `com.apple.developer.networking.HotspotConfiguration = <true/>`
- [ ] `BodyCam.csproj` sets `<CodesignEntitlements>` for both Debug and
      Release iOS configurations
- [ ] `NSBluetoothAlwaysUsageDescription`, `NSLocalNetworkUsageDescription`,
      `NSCameraUsageDescription`, `NSMicrophoneUsageDescription`,
      `NSLocationWhenInUseUsageDescription` all present in `Info.plist`
      with non-empty, App-Store-acceptable strings
- [ ] `UIBackgroundModes` includes `bluetooth-central` and `audio`
- [ ] Provisioning profile re-issued with Hotspot Configuration enabled
      and embedded in the signed `.ipa`
- [ ] First device launch shows the BLE permission prompt **before** the
      first scan attempt (otherwise scan returns silently empty)
- [ ] `NEHotspotConfigurationManager.ApplyConfiguration` returns `nil`
      error on a paired build (not error code 8 / `unauthorized`)
- [ ] `docs/deployment/ios.md` updated with the new prompt sequence and
      error-code-8 remediation pointer
