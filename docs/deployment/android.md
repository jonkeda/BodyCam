# Deploying BodyCam to an Android Phone

## Prerequisites

| Requirement | Version |
|-------------|---------|
| .NET SDK | 10.0+ |
| .NET MAUI workload | `dotnet workload install maui` |
| Android SDK | API 21+ (installed via VS or `dotnet workload`) |
| Java JDK | 17 (bundled with the MAUI workload) |
| USB cable | USB-C / Micro-USB data cable (not charge-only) |

## 1. Enable Developer Options on Your Phone

1. Open **Settings > About phone**
2. Tap **Build number** 7 times until it says "You are now a developer"
3. Go back to **Settings > Developer options**
4. Enable **USB debugging**
5. *(Optional)* Enable **Install via USB** if your phone has this setting

## 2. Connect the Phone

1. Plug the phone into your PC via USB
2. On the phone, accept the **"Allow USB debugging?"** prompt
3. Check the **"Always allow from this computer"** box
4. Verify the connection:

```powershell
# Should list your device
dotnet build -t:GetAndroidDevices -f net10.0-android src/BodyCam/BodyCam.csproj
```

Or if you have `adb` on PATH:

```powershell
adb devices
# Should show something like:
# XXXXXXXXX    device
```

## 3. Build and Deploy (Debug)

From the repo root:

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android -t:Install
```

This builds the APK in Debug configuration and installs it on the connected device. The app will appear as **BodyCam** in your app drawer.

To build and immediately launch:

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android -t:Run
```

## 4. Build a Signed Release APK

### Generate a keystore (one-time)

```powershell
keytool -genkeypair -v -keystore bodycam.keystore -alias bodycam -keyalg RSA -keysize 2048 -validity 10000
```

Keep `bodycam.keystore` safe — you need the same key to update the app.

### Build the release APK

```powershell
dotnet publish src/BodyCam/BodyCam.csproj -f net10.0-android -c Release `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore=bodycam.keystore `
    -p:AndroidSigningKeyAlias=bodycam `
    -p:AndroidSigningKeyPass=YOUR_PASSWORD `
    -p:AndroidSigningStorePass=YOUR_PASSWORD
```

The signed APK will be at:

```
src/BodyCam/bin/Release/net10.0-android/publish/com.companyname.bodycam-Signed.apk
```

### Install the release APK

```powershell
adb install src/BodyCam/bin/Release/net10.0-android/publish/com.companyname.bodycam-Signed.apk
```

Or transfer the APK to the phone and install via the file manager.

## 5. Wireless Debugging (no USB cable)

After the initial USB setup, you can switch to Wi-Fi:

```powershell
# Phone and PC must be on the same network
adb tcpip 5555
adb connect <PHONE_IP>:5555
# Unplug USB — subsequent deploys go over Wi-Fi
```

Find the phone IP at **Settings > Wi-Fi > (your network) > IP address**.

To switch back to USB:

```powershell
adb usb
```

## 6. Required Permissions

The app requests these permissions at runtime (declared in `AndroidManifest.xml`):

| Permission | Why |
|------------|-----|
| `INTERNET` | OpenAI / Azure API calls |
| `ACCESS_NETWORK_STATE` | Check connectivity |
| `RECORD_AUDIO` | Microphone for voice input |
| `CAMERA` | Camera feed for vision features |
| `BLUETOOTH` / `BLUETOOTH_CONNECT` | Bluetooth audio devices |
| `MODIFY_AUDIO_SETTINGS` | Audio output routing |

Grant all permissions when prompted on first launch.

## 7. Troubleshooting

### "No Android devices found"

- Check USB cable is a **data** cable (charge-only cables won't work)
- Ensure **USB debugging** is enabled
- Try a different USB port
- Run `adb kill-server && adb start-server`

### Build fails with SDK errors

```powershell
# Install/update the Android workload
dotnet workload install maui-android
# Or update all workloads
dotnet workload update
```

### App crashes on launch

```powershell
# Stream the device log filtered to BodyCam
adb logcat -s mono-rt:* dotnet:* BodyCam:*
```

### Deploy succeeds but app doesn't appear

```powershell
# Force reinstall
adb uninstall com.companyname.bodycam
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android -t:Install
```

### Hot Restart (faster iteration)

Use `dotnet build -t:Run` with the `-p:EmbedAssembliesIntoApk=false` flag during development. This avoids a full APK rebuild for code-only changes.
