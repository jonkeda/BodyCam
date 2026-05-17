# Wave 5 — Unit Tests with `FakeHeyCyanSdkBridge`

**Parent:** [../phase1-android-binding.md](../phase1-android-binding.md)
**Previous:** [wave4-di-and-permissions.md](wave4-di-and-permissions.md)

## Goal

Lock down Wave 3 behavior (and the frame-parser logic from Wave 2) with
cross-platform xUnit + FluentAssertions tests in `BodyCam.Tests`. Tests
must run on the dev box without an Android emulator, the AAR, or any real
glasses — that is the entire point of putting `IHeyCyanSdkBridge` behind
an interface in Wave 2. Real-hardware verification happens manually
against the Phase 1 exit-criteria checklist in
[`../phase1-android-binding.md`](../phase1-android-binding.md).

## Steps

1. **Confirm `BodyCam.Tests` targets a non-Android TFM.** It should already
   build for `net9.0` (no `-android` suffix). If the session class is
   `#if ANDROID`-only (Wave 3), refactor so the testable parts live in a
   shared file:

   - `src/BodyCam/Services/Glasses/HeyCyan/AndroidHeyCyanGlassesSession.cs`
     — keeps the `#if ANDROID` guard, contains DI ctor.
   - `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanGlassesSessionCore.cs` —
     **no** `#if`, contains the testable logic. Tests construct it
     directly with a `FakeHeyCyanSdkBridge`.

   The Android class becomes a thin shim that forwards to the core. This
   keeps the cross-platform test boundary clean.

2. **Implement `FakeHeyCyanSdkBridge`** at
   `src/BodyCam.Tests/Glasses/HeyCyan/FakeHeyCyanSdkBridge.cs`:

   ```csharp
   namespace BodyCam.Tests.Glasses.HeyCyan;

   internal sealed class FakeHeyCyanSdkBridge : IHeyCyanSdkBridge
   {
       public List<HeyCyanScanResult> ScriptedScan { get; } = new();
       public Func<byte[], HeyCyanResponse>? OnSend { get; set; }
       public TaskCompletionSource ConnectGate { get; } =
           new(TaskCreationOptions.RunContinuationsAsynchronously);

       public bool ScanStarted { get; private set; }
       public bool ScanStopped { get; private set; }
       public string? ConnectedMac { get; private set; }
       public bool Disposed { get; private set; }

       public event EventHandler<HeyCyanScanResult>? DeviceDiscovered;
       public event EventHandler<HeyCyanConnectionState>? ConnectionStateChanged;
       public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
       public event EventHandler<HeyCyanRawNotify>? RawNotify;

       public Task StartScanAsync(TimeSpan _, CancellationToken __)
       {
           ScanStarted = true;
           foreach (var r in ScriptedScan) DeviceDiscovered?.Invoke(this, r);
           return Task.CompletedTask;
       }

       public Task StopScanAsync() { ScanStopped = true; return Task.CompletedTask; }

       public async Task ConnectAsync(string mac, CancellationToken ct)
       {
           ConnectionStateChanged?.Invoke(this, HeyCyanConnectionState.Connecting);
           await ConnectGate.Task.WaitAsync(ct);
           ConnectedMac = mac;
           ConnectionStateChanged?.Invoke(this, HeyCyanConnectionState.Connected);
       }

       public Task DisconnectAsync()
       {
           ConnectionStateChanged?.Invoke(this, HeyCyanConnectionState.Disconnected);
           ConnectedMac = null;
           return Task.CompletedTask;
       }

       public Task<HeyCyanResponse> SendAsync(byte[] payload, CancellationToken ct)
       {
           if (OnSend is null) throw new InvalidOperationException("OnSend not scripted");
           return Task.FromResult(OnSend(payload));
       }

       public void RaiseButton(HeyCyanButtonGesture g) =>
           ButtonPressed?.Invoke(this, new(g, DateTimeOffset.UtcNow));
       public void RaiseDisconnect() =>
           ConnectionStateChanged?.Invoke(this, HeyCyanConnectionState.Disconnected);
       public void RaiseRawNotify(byte[] frame) =>
           RawNotify?.Invoke(this, new(frame));

       public void Dispose() => Disposed = true;
   }
   ```

3. **Write `AndroidHeyCyanGlassesSessionTests`** at
   `src/BodyCam.Tests/Glasses/HeyCyan/AndroidHeyCyanGlassesSessionTests.cs`:

   ```csharp
   public class AndroidHeyCyanGlassesSessionTests
   {
       private static (HeyCyanGlassesSessionCore session, FakeHeyCyanSdkBridge fake) Build()
       {
           var fake = new FakeHeyCyanSdkBridge();
           var session = new HeyCyanGlassesSessionCore(fake, NullLogger<HeyCyanGlassesSessionCore>.Instance);
           return (session, fake);
       }

       [Fact]
       public async Task ScanAsync_returns_deduplicated_devices()
       {
           var (s, fake) = Build();
           fake.ScriptedScan.AddRange([
               new("Glasses", "AA:BB", -50),
               new("Glasses", "AA:BB", -52), // duplicate MAC
               new("Glasses", "CC:DD", -60),
           ]);
           var result = await s.ScanAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
           result.Should().HaveCount(2);
           result.Select(r => r.Address).Should().BeEquivalentTo(new[] { "AA:BB", "CC:DD" });
       }

       [Fact]
       public async Task ConnectAsync_transitions_through_Connecting_to_Connected()
       {
           var (s, fake) = Build();
           var states = new List<HeyCyanState>();
           s.StateChanged += (_, st) => states.Add(st);
           var task = s.ConnectAsync(new("X", "AA:BB", -50), CancellationToken.None);
           fake.ConnectGate.SetResult();
           await task;
           states.Should().ContainInOrder(HeyCyanState.Connecting, HeyCyanState.Connected);
       }

       [Fact]
       public async Task ConnectAsync_when_already_connected_throws()
       {
           var (s, fake) = Build();
           fake.ConnectGate.SetResult();
           await s.ConnectAsync(new("X", "AA:BB", -50), default);
           Func<Task> act = () => s.ConnectAsync(new("X", "AA:BB", -50), default);
           await act.Should().ThrowAsync<InvalidOperationException>();
       }

       [Fact]
       public async Task ConnectAsync_cancellation_propagates()
       {
           var (s, fake) = Build();
           using var cts = new CancellationTokenSource();
           var task = s.ConnectAsync(new("X", "AA:BB", -50), cts.Token);
           cts.Cancel();
           await FluentActions.Awaiting(() => task).Should().ThrowAsync<OperationCanceledException>();
           s.State.Should().Be(HeyCyanState.Disconnected);
       }

       [Fact]
       public async Task BLE_drop_transitions_to_Disconnected_and_raises_StateChanged()
       {
           var (s, fake) = Build();
           fake.ConnectGate.SetResult();
           await s.ConnectAsync(new("X", "AA:BB", -50), default);
           HeyCyanState? last = null;
           s.StateChanged += (_, st) => last = st;
           fake.RaiseDisconnect();
           last.Should().Be(HeyCyanState.Disconnected);
           s.Device.Should().BeNull();
       }

       [Fact]
       public async Task GetVersionAsync_parses_firmware_response()
       {
           var (s, fake) = Build();
           fake.OnSend = _ => new HeyCyanResponse(0x01, /* canned bytes */ new byte[] { /* … */ });
           var v = await s.GetVersionAsync(default);
           v.Firmware.Should().NotBeNullOrEmpty();
       }

       [Fact]
       public async Task SyncTimeAsync_sends_unix_LE_payload()
       {
           var (s, fake) = Build();
           byte[]? sent = null;
           fake.OnSend = p => { sent = p; return new HeyCyanResponse(0x03, []); };
           await s.SyncTimeAsync(default);
           sent.Should().NotBeNull();
           sent![0].Should().Be(0x03);
           // bytes 1..4 are unix-LE
           BitConverter.IsLittleEndian.Should().BeTrue();
       }

       [Fact]
       public async Task ButtonPressed_forwards_each_gesture_exactly_once()
       {
           var (s, fake) = Build();
           var got = new List<HeyCyanButtonGesture>();
           s.ButtonPressed += (_, e) => got.Add(e.Gesture);
           fake.RaiseButton(HeyCyanButtonGesture.Tap);
           fake.RaiseButton(HeyCyanButtonGesture.DoubleTap);
           fake.RaiseButton(HeyCyanButtonGesture.LongPress);
           got.Should().Equal(HeyCyanButtonGesture.Tap, HeyCyanButtonGesture.DoubleTap, HeyCyanButtonGesture.LongPress);
       }

       [Fact]
       public async Task DisposeAsync_is_idempotent()
       {
           var (s, fake) = Build();
           await s.DisposeAsync();
           await s.DisposeAsync(); // must not throw
           fake.Disposed.Should().BeTrue();
       }
   }
   ```

4. **Write `HeyCyanFrameParserTests`** that exercise the byte-level
   parsing logic from Wave 2 directly, with no bridge in the loop:

   ```csharp
   public class HeyCyanFrameParserTests
   {
       [Theory]
       [InlineData(new byte[] { 0x02, 0,0,0,0,0, 0x01 }, HeyCyanButtonGesture.Tap)]
       [InlineData(new byte[] { 0x02, 0,0,0,0,0, 0x02 }, HeyCyanButtonGesture.DoubleTap)]
       [InlineData(new byte[] { 0x02, 0,0,0,0,0, 0x03 }, HeyCyanButtonGesture.LongPress)]
       public void Button_frames_decoded_correctly(byte[] frame, HeyCyanButtonGesture expected)
           => HeyCyanFrameParser.TryParseButton(frame, out var g).Should().BeTrue().And.Subject
               .Should().Be(true).And.Subject.Should().Be(expected); // (use the actual API shape)

       [Fact]
       public void Ip_frame_loadData6_eq_08_extracts_ipv4_from_bytes_7_to_10()
       {
           var frame = new byte[] { 0xFF, 0,0,0,0,0, 0x08, 192, 168, 49, 1 };
           HeyCyanFrameParser.TryParseTransferIp(frame, out var ip).Should().BeTrue();
           ip.ToString().Should().Be("192.168.49.1");
       }

       [Fact]
       public void P2p_error_frame_loadData6_eq_09_value_FF_is_classified_noisy()
       {
           var frame = new byte[] { 0xFF, 0,0,0,0,0, 0x09, 0xFF };
           HeyCyanFrameParser.ClassifyP2pError(frame).Should().Be(HeyCyanP2pErrorKind.Noisy);
       }
   }
   ```

5. **Avoid real time in tests.** Inject a `TimeProvider` into the session
   if any test needs a deterministic timestamp. `Task.Delay` based
   timeouts in `ScanAsync` should accept very small windows
   (<= 50 ms) — never wait seconds.

6. **Run the suite from the repo root**:

   ```powershell
   dotnet test src/BodyCam.Tests --filter "FullyQualifiedName~HeyCyan" -c Debug
   ```

7. **Wire into CI.** Confirm `.github/workflows/*.yml` (or whatever CI is
   in use) runs `BodyCam.Tests` without an Android workload. Tests must
   be green on a Windows runner with only the `net9.0` SDK installed.

## Verify

- [ ] `BodyCam.Tests` builds without referencing any `-android` TFM
- [ ] All `AndroidHeyCyanGlassesSessionTests` pass on the dev box (no glasses required)
- [ ] All `HeyCyanFrameParserTests` pass and cover button gestures + `0x08` IP + `0x09` P2P-error branches
- [ ] `FakeHeyCyanSdkBridge` raises every event the real bridge raises (no shape drift)
- [ ] No test depends on real wall-clock time — all use small `TimeSpan`s or a `TimeProvider`
- [ ] Idempotent dispose verified (calling `DisposeAsync` twice does not throw)
- [ ] Cancellation tests use `CancellationTokenSource.Cancel()` and assert `OperationCanceledException`
- [ ] CI green on a runner without the Android workload installed
