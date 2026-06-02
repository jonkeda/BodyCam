# M33 Phase 6 — Wave 5: DI Registration & Parity Tests

## Goal

Wire `IosHeyCyanGlassesSession` and `HotspotHttpClient` into the MAUI DI
container under an iOS-only conditional, then prove behavioural parity with
`AndroidHeyCyanGlassesSession` (M33 Phase 1) using a single shared test
harness. The Phase 2-5 cross-platform providers
(`HeyCyanCameraProvider`, `HeyCyanAudioInputProvider`,
`HeyCyanAudioOutputProvider`, `HeyCyanButtonProvider`,
`HeyCyanMediaTransfer`) consume `IHeyCyanGlassesSession` and must run
unmodified on iOS — this wave verifies that contract.

**Parent phase:** [`../phase6-ios-binding.md`](../phase6-ios-binding.md)
**Prev:** [`wave4-infoplist-entitlements.md`](wave4-infoplist-entitlements.md)

## Steps

1. **Register the iOS services in `MauiProgram.cs`.** Wrap registration in
   `#if IOS` so the binding assembly never loads on Android / Windows /
   MacCatalyst:

   ```csharp
   #if IOS
   using BodyCam.Platforms.iOS.HeyCyan;
   using BodyCam.Services.Glasses.HeyCyan;

   builder.Services.AddSingleton<HotspotHttpClient>();
   builder.Services.AddSingleton<IHeyCyanGlassesSession, IosHeyCyanGlassesSession>();
   #endif
   ```

   Singleton lifetime is correct because `QCSDKManager.SharedInstance` and
   `QCCentralManager.SharedInstance` are themselves Obj-C singletons —
   stacking transient managed wrappers on top would produce conflicting
   delegates.

2. **Confirm the cross-platform providers are not duplicated.** The Phase
   2-5 providers (`HeyCyanCameraProvider`, `HeyCyanAudioInputProvider`,
   `HeyCyanAudioOutputProvider`, `HeyCyanButtonProvider`,
   `HeyCyanMediaTransfer`) are registered **outside** the `#if IOS` block
   and resolve `IHeyCyanGlassesSession` by interface. Verify with
   `grep_search` for `HeyCyanCameraProvider` that there is no iOS fork.

3. **Add a contract conformance test.** This catches missing interface
   members at compile time on every platform:

   ```csharp
   // src/BodyCam.Tests/Glasses/HeyCyanSessionContractTests.cs
   public sealed class HeyCyanSessionContractTests
   {
       public static IEnumerable<object[]> Sessions => new[]
       {
   #if ANDROID
           new object[] { typeof(AndroidHeyCyanGlassesSession) },
   #elif IOS
           new object[] { typeof(IosHeyCyanGlassesSession) },
   #endif
       };

       [Theory, MemberData(nameof(Sessions))]
       public void Implements_full_contract(Type t) =>
           typeof(IHeyCyanGlassesSession).IsAssignableFrom(t).Should().BeTrue();
   }
   ```

4. **Add a fake-bridge parity test.** Drive both sessions with the same
   recorded notify-frame fixtures so behavioural parity (state ordering,
   gesture mapping, battery deltas) is asserted in `BodyCam.Tests` without
   real hardware:

   ```csharp
   public sealed class HeyCyanSessionParityTests
   {
       [Fact]
       public async Task Button_frames_emit_same_gestures_as_android()
       {
           // Replay fixtures from src/BodyCam.Tests/Glasses/Fixtures/notify-frames/
           // through both NotifyFrameParser and assert the same
           // HeyCyanButtonGesture sequence the Android Phase 1 tests assert.
       }

       [Fact]
       public void Connect_state_ordering_matches_android()
       {
           var actual = SimulateConnectFlow();
           actual.Should().Equal(
               HeyCyanState.Disconnected,
               HeyCyanState.Scanning,
               HeyCyanState.Connecting,
               HeyCyanState.Connected);
       }
   }
   ```

5. **Add a real-device parity test gate.** Lives in `BodyCam.RealTests`
   (per `.github/copilot-instructions.md`) and is gated on the
   `BODYCAM_HEYCYAN_REAL_DEVICE=1` environment variable so CI does not
   require glasses:

   ```csharp
   // src/BodyCam.RealTests/Glasses/IosHeyCyanRealDeviceTests.cs
   [Trait("Category", "RealDevice")]
   public sealed class IosHeyCyanRealDeviceTests
   {
       [SkippableFact]
       public async Task Scan_then_capture_then_transfer_round_trip()
       {
           Skip.If(Environment.GetEnvironmentVariable("BODYCAM_HEYCYAN_REAL_DEVICE") != "1");

           await using var session = ResolveSession();
           var devices = await session.ScanAsync(TimeSpan.FromSeconds(15), default);
           devices.Should().NotBeEmpty();

           await session.ConnectAsync(devices[0], default);
           session.State.Should().Be(HeyCyanState.Connected);

           var v = await session.GetVersionAsync(default);
           v.Firmware.Should().NotBeNullOrEmpty();

           await session.TakePhotoAsync(default);
           await using var transfer = await session.EnterTransferModeAsync(default);
           transfer.BaseUrl.Should().StartWith("http://");
           transfer.FileNames.Should().NotBeEmpty();
       }
   }
   ```

6. **Wire the iOS test target.** Add `net9.0-ios` to the
   `BodyCam.RealTests` target frameworks and reference
   `BodyCam.HeyCyan.iOS.Bindings` only under that TFM. The test class file
   is gated `#if IOS` so other targets continue to compile.

7. **Run the build matrix.**

   ```pwsh
   dotnet build BodyCam.sln -c Debug
   dotnet test src/BodyCam.Tests        # contract + fixture parity
   dotnet test src/BodyCam.RealTests -- --filter "Category!=RealDevice"
   # Hardware run (paired iPhone, paired glasses):
   $env:BODYCAM_HEYCYAN_REAL_DEVICE = "1"
   dotnet test src/BodyCam.RealTests -f net9.0-ios -- --filter "Category=RealDevice"
   ```

8. **Update overview status.** Mark Phase 6 entry in
   [`../overview.md`](../overview.md) as ✅ once all checkboxes below pass
   on real hardware.

## Verify

- [ ] `MauiProgram.cs` registers `HotspotHttpClient` and
      `IosHeyCyanGlassesSession` only inside an `#if IOS` block
- [ ] No iOS-specific replacements of `HeyCyanCameraProvider`,
      `HeyCyanAudioInputProvider`, `HeyCyanAudioOutputProvider`,
      `HeyCyanButtonProvider`, or `HeyCyanMediaTransfer` exist (one
      cross-platform registration each)
- [ ] `HeyCyanSessionContractTests` passes on Android and iOS
- [ ] `HeyCyanSessionParityTests` consumes the same notify-frame fixture
      corpus as Android Phase 1 and asserts identical gesture / state
      sequences
- [ ] `IosHeyCyanRealDeviceTests` passes against real hardware when
      `BODYCAM_HEYCYAN_REAL_DEVICE=1` is set
- [ ] `dotnet build -f net9.0-ios` is clean — no binding warnings,
      no missing `[Export]` on protocol members
- [ ] On real iPhone hardware: scan → connect → version/battery/time →
      photo → AI photo → transfer-mode round-trip all succeed end-to-end
- [ ] On disconnect, the iPhone returns to its previous Wi-Fi network
      (verified by `NEHotspotConfigurationManager.GetConfiguredSsids`
      returning empty)
- [ ] [`../overview.md`](../overview.md) Phase 6 status updated to ✅
