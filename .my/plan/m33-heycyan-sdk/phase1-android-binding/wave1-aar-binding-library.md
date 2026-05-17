# Wave 1 — AAR Binding Library

**Parent:** [../phase1-android-binding.md](../phase1-android-binding.md)
**Next:** [wave2-heycyan-sdk-bridge.md](wave2-heycyan-sdk-bridge.md)

## Goal

Create `BodyCam.HeyCyan.Android.Bindings` — a .NET-for-Android **binding
library** project that wraps the vendor `glasses_sdk_20250723_v01.aar` and
produces a managed `BodyCam.HeyCyan.Android.Bindings.dll`. This is the
foundation every later wave (and every later phase) builds on. No bridge
logic, no session, no DI — just a clean managed surface over the AAR with
the obfuscated junk hidden and the public API surfaced.

## Steps

1. **Create the binding project** at
   `src/BodyCam.HeyCyan.Android.Bindings/BodyCam.HeyCyan.Android.Bindings.csproj`:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net9.0-android35.0</TargetFramework>
       <SupportedOSPlatformVersion>26</SupportedOSPlatformVersion>
       <IsBindingProject>true</IsBindingProject>
       <AndroidClassParser>class-parse</AndroidClassParser>
       <AndroidCodegenTarget>XAJavaInterop1</AndroidCodegenTarget>
       <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
       <Nullable>enable</Nullable>
     </PropertyGroup>

     <ItemGroup>
       <AndroidLibrary Include="Jars\glasses_sdk_20250723_v01.aar">
         <Bind>true</Bind>
       </AndroidLibrary>
       <TransformFile Include="Transforms\Metadata.xml" />
       <TransformFile Include="Transforms\EnumFields.xml" />
       <TransformFile Include="Transforms\EnumMethods.xml" />
     </ItemGroup>
   </Project>
   ```

2. **Drop the AAR** into `src/BodyCam.HeyCyan.Android.Bindings/Jars/`. Copy
   from
   `Alternative-HeyCyan-App-and-SDK/android/CyanBridge/app/libs/glasses_sdk_20250723_v01.aar`.
   Add to the solution but **do not** check the AAR into the binding
   project's NuGet output — `IsBindingProject=true` already wraps it.

   ```powershell
   $src = 'Alternative-HeyCyan-App-and-SDK/android/CyanBridge/app/libs/glasses_sdk_20250723_v01.aar'
   $dst = 'src/BodyCam.HeyCyan.Android.Bindings/Jars/glasses_sdk_20250723_v01.aar'
   New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
   Copy-Item $src $dst
   ```

3. **Add the binding project to the solution** and reference it from
   `src/BodyCam/BodyCam.csproj` guarded by the Android TFM only:

   ```xml
   <ItemGroup Condition="$(TargetFramework.Contains('-android'))">
     <ProjectReference Include="..\BodyCam.HeyCyan.Android.Bindings\BodyCam.HeyCyan.Android.Bindings.csproj" />
   </ItemGroup>
   ```

4. **First build & triage `BG8xxx` warnings.** Run
   `dotnet build src/BodyCam.HeyCyan.Android.Bindings -f net9.0-android35.0`.
   For each warning either fix the metadata or explicitly silence it via a
   `<remove-node>` / `<attr>` rule in `Transforms/Metadata.xml`. Never
   silence with `NoWarn`.

5. **Hide obfuscated noise.** The AAR contains a flat
   `com.iflytop.android.QCSDK` / `com.qiying.qcsdk.*` package full of
   single-letter inner classes (`a`, `b`, `c`…). Add a default-hide rule
   and re-expose only what we use:

   ```xml
   <!-- Transforms/Metadata.xml -->
   <metadata>
     <!-- Hide every $-suffixed inner class by default. -->
     <remove-node path="//class[contains(@name, '$')]" />

     <!-- Re-expose listener interfaces we need (cmdType, response). -->
     <attr path="/api/package[@name='com.qiying.qcsdk']/class[@name='LargeDataHandler.Response']"
           name="visibility">public</attr>

     <!-- Singleton return type. -->
     <attr path="/api/package[@name='com.qiying.qcsdk']/class[@name='LargeDataHandler']/method[@name='getInstance']"
           name="managedReturn">com.qiying.qcsdk.LargeDataHandler</attr>
   </metadata>
   ```

   > Adjust paths after running `class-parse` once and inspecting
   > `obj/Debug/net9.0-android35.0/api.xml`. The exact package and inner
   > class names are AAR-specific — do not invent them.

6. **Map functional interfaces to C# delegates** where `class-parse`
   produces awkward inner-interface output. The vendor SDK uses
   `(cmdType, resp) -> ...` lambdas:

   ```xml
   <attr path="/api/package[@name='com.qiying.qcsdk']/interface[@name='OnControlResponse']/method[@name='onResponse']"
         name="managedName">Invoke</attr>
   ```

   This lets C# consumers pass `Action<int, LargeDataHandler.Response>`.

7. **Keep this public API surface visible** (everything else may be hidden):

   | Java symbol | Why it must stay public |
   |---|---|
   | `LargeDataHandler.getInstance()` | bridge singleton entry |
   | `LargeDataHandler.glassesControl(byte[], OnControlResponse)` | only command-send method |
   | `LargeDataHandler.setNotifyListener(OnNotify)` | raw frame stream |
   | `QCCentralManager` (or vendor `BleManager`) | scan / connect / disconnect |
   | `QCDeviceInfo` / `QCBleDevice` | scan-result POCO |
   | `OnScan*` / `OnConnect*` / `OnButtonEvent*` listeners | event sources |

8. **Verify Android resources land in the consuming app.** The AAR ships
   AndroidManifest fragments and (potentially) native `.so` libs. Build
   `BodyCam` for `-android` and confirm the resulting APK contains the
   vendor `lib/<abi>/lib*.so` files under
   `obj/Debug/net9.0-android35.0/android/bin/packaged_resources`.

9. **Commit `Transforms/Metadata.xml`** with comments explaining each
   `<remove-node>` and `<attr>` rule — future agents need to know *why*
   each transform exists, since the AAR is a black box.

## Verify

- [ ] `dotnet build src/BodyCam.HeyCyan.Android.Bindings -f net9.0-android35.0` succeeds
- [ ] No `BG8xxx` warnings remain (each is either fixed or explicitly silenced via `Transforms/Metadata.xml`)
- [ ] `LargeDataHandler.Instance.GlassesControl(byte[], …)` resolves in IntelliSense from a referencing C# file
- [ ] `QCCentralManager` (or vendor equivalent) `StartScan` / `Connect` / `Disconnect` are reachable
- [ ] `obj/Debug/net9.0-android35.0/api.xml` no longer surfaces `class$a` / `class$b` inner classes
- [ ] Solution builds end-to-end on `-android` TFM without referencing the AAR from `BodyCam.csproj` directly
- [ ] Vendor native `.so` libs are packaged into the APK
- [ ] `Transforms/Metadata.xml` is committed with explanatory comments
