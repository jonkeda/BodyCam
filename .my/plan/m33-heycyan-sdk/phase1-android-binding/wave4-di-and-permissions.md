# Wave 4 — DI Registration & Manifest Permissions

**Parent:** [../phase1-android-binding.md](../phase1-android-binding.md)
**Previous:** [wave3-android-glasses-session.md](wave3-android-glasses-session.md)
**Next:** [wave5-fake-bridge-tests.md](wave5-fake-bridge-tests.md)

## Goal

Make `IHeyCyanGlassesSession` resolvable from the MAUI DI container on
Android, declare every Bluetooth / Wi-Fi / location permission the SDK
needs (including those Phase 2 will require, so the user is not prompted
twice), and add a typed `HeyCyanPermissions.RequestAsync()` helper that
the session calls before the first scan. No UI yet — Phase 7 owns the
connection screen.

## Steps

1. **Register services in `MauiProgram.cs`.** `IHeyCyanGlassesSession` is
   singleton — there is exactly one physical glasses connection per app.
   Phase 7 will wrap it in `HeyCyanGlassesDeviceManager`:

   ```csharp
   // src/BodyCam/MauiProgram.cs
   #if ANDROID
   builder.Services.AddSingleton<IHeyCyanSdkBridge, BodyCam.Platforms.Android.HeyCyan.HeyCyanSdkBridge>();
   builder.Services.AddSingleton<IHeyCyanGlassesSession,
       BodyCam.Services.Glasses.HeyCyan.AndroidHeyCyanGlassesSession>();
   #endif
   ```

2. **Declare permissions** in
   `src/BodyCam/Platforms/Android/AndroidManifest.xml`. Add Phase 2
   permissions now (`NEARBY_WIFI_DEVICES`, `ACCESS_FINE_LOCATION`,
   `CHANGE_WIFI_STATE`) so the user is not re-prompted when the camera
   provider lands:

   ```xml
   <!-- BLE scan + connect (Phase 1). -->
   <uses-permission android:name="android.permission.BLUETOOTH_SCAN"
                    android:usesPermissionFlags="neverForLocation" />
   <uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />

   <!-- Wi-Fi-Direct transfer mode (Phase 2 — declared early to avoid re-prompts). -->
   <uses-permission android:name="android.permission.NEARBY_WIFI_DEVICES"
                    android:usesPermissionFlags="neverForLocation" />
   <uses-permission android:name="android.permission.CHANGE_WIFI_STATE" />
   <uses-permission android:name="android.permission.ACCESS_WIFI_STATE" />

   <!-- Required on Android < 12 for BLE scan results. -->
   <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
   <uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />

   <!-- HTTP to the glasses' Wi-Fi-Direct group (Phase 2). The SDK opens a
        plain-HTTP server, so we need a network-security-config exception. -->
   <uses-permission android:name="android.permission.INTERNET" />

   <uses-feature android:name="android.hardware.bluetooth_le" android:required="true" />
   ```

3. **Add `network_security_config.xml`** at
   `src/BodyCam/Platforms/Android/Resources/xml/network_security_config.xml`
   to permit cleartext to the glasses' Wi-Fi-Direct subnet (Phase 2 will
   actually use it; declare now to avoid touching the manifest twice):

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <network-security-config>
     <domain-config cleartextTrafficPermitted="true">
       <!-- Default Wi-Fi-Direct group owner per QCSDK. -->
       <domain includeSubdomains="true">192.168.49.1</domain>
     </domain-config>
   </network-security-config>
   ```

   Reference it from `<application android:networkSecurityConfig="@xml/network_security_config" …>`.

4. **Implement `HeyCyanPermissions`** at
   `src/BodyCam/Platforms/Android/HeyCyan/HeyCyanPermissions.cs` (Android-
   only). Use MAUI's `Permissions` API — do not hand-roll the Android
   request flow:

   ```csharp
   #if ANDROID
   namespace BodyCam.Platforms.Android.HeyCyan;

   internal static class HeyCyanPermissions
   {
       public static async Task RequestAsync()
       {
           var bt = await Permissions.RequestAsync<Permissions.Bluetooth>();
           if (bt != PermissionStatus.Granted)
               throw new HeyCyanPermissionException(nameof(Permissions.Bluetooth));

           if (OperatingSystem.IsAndroidVersionAtLeast(31) is false)
           {
               var loc = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
               if (loc != PermissionStatus.Granted)
                   throw new HeyCyanPermissionException(nameof(Permissions.LocationWhenInUse));
           }
       }
   }

   public sealed class HeyCyanPermissionException(string permission)
       : Exception($"User denied required permission: {permission}");
   #endif
   ```

5. **Wire the helper into `AndroidHeyCyanGlassesSession.ScanAsync`** (Wave
   3 already has the call site). The first `ScanAsync` triggers the
   prompt; subsequent calls return immediately because MAUI caches grants.

6. **Verify the foreground service requirement does not apply yet.**
   Phase 1 only does BLE scan/connect with the app in the foreground.
   Phase 5 may need a `bluetoothConnected` foreground service for
   long-running media transfer; do not add it now.

7. **Confirm targetSdk alignment.** `BodyCam.csproj` should already target
   `android35.0` (Android 15); confirm `<TargetFrameworks>` contains
   `net9.0-android35.0` and `<SupportedOSPlatformVersion>26`. The new
   permissions assume `BLUETOOTH_SCAN`/`BLUETOOTH_CONNECT` (Android 12+);
   the legacy `ACCESS_FINE_LOCATION` requirement only kicks in below 31.

8. **Smoke test the resolution chain**:

   ```powershell
   dotnet build src/BodyCam -f net9.0-android35.0
   ```

   Then add a temporary log line in `App.xaml.cs` (`OnStart`) that
   resolves `IHeyCyanGlassesSession` from `MauiContext.Services` and logs
   `State`. Remove before commit.

## Verify

- [ ] `dotnet build src/BodyCam -f net9.0-android35.0` succeeds
- [ ] `IHeyCyanGlassesSession` resolves via DI as a singleton
- [ ] `IHeyCyanSdkBridge` and `IHeyCyanGlassesSession` share the same lifetime (no double-instantiation)
- [ ] First `ScanAsync` prompts for `BLUETOOTH_SCAN` + `BLUETOOTH_CONNECT` on a real device
- [ ] On Android < 12, `ACCESS_FINE_LOCATION` is also requested
- [ ] Subsequent scans do **not** re-prompt
- [ ] `HeyCyanPermissionException` is thrown (not swallowed) when the user denies
- [ ] `AndroidManifest.xml` includes `NEARBY_WIFI_DEVICES` + `CHANGE_WIFI_STATE` so Phase 2 needs no manifest churn
- [ ] `network_security_config.xml` is referenced from `<application>`
- [ ] `<uses-feature android:name="android.hardware.bluetooth_le" android:required="true" />` is present
