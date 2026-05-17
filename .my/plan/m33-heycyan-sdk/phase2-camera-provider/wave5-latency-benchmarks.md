# M33 Phase 2 — Wave 5: Latency Benchmarks & Warm-Mode Verification

**Parent:** [`../phase2-camera-provider.md`](../phase2-camera-provider.md)
**Siblings:** [wave1](wave1-wifi-p2p-http-client.md) · [wave2](wave2-heycyan-media-transfer.md) · [wave3](wave3-heycyan-camera-provider.md) · [wave4](wave4-camera-manager-integration.md)

## Goal

Lock in the latency contract for the HeyCyan camera pipeline with both
unit tests (fake session + virtual clock) and real-hardware benchmarks
(opt-in, run from `BodyCam.RealTests`). Cold capture must complete in
≤ 6 s end-to-end; warm capture (within the 8 s idle window) must
complete in ≤ 2 s. Numbers get recorded in M11 docs alongside the
phone-camera baseline so callers (vision agent, dictation flow) have
honest expectations.

## Steps

1. **Add unit tests for warm-mode behavior** under
   [src/BodyCam.Tests/Services/Glasses/HeyCyan/HeyCyanMediaTransferTests.cs](../../../../src/BodyCam.Tests/Services/Glasses/HeyCyan/HeyCyanMediaTransferTests.cs)
   (extending the file from Wave 2). Use `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider`
   so idle expiry is deterministic:

   ```csharp
   [Fact]
   public async Task DownloadAsync_TwiceWithinWarmWindow_EntersTransferModeOnce()
   {
       var session = new FakeHeyCyanSession();
       var time = new FakeTimeProvider();
       var transfer = new HeyCyanMediaTransfer(session, _httpFactory, time,
           warmIdle: TimeSpan.FromSeconds(8));

       _ = await transfer.DownloadAsync("a.jpg", default);
       time.Advance(TimeSpan.FromSeconds(3));
       _ = await transfer.DownloadAsync("b.jpg", default);

       session.EnterCount.Should().Be(1);
   }

   [Fact]
   public async Task DownloadAsync_AfterWarmIdleElapsed_ReentersTransferMode()
   {
       // ... advance past 8s, expect EnterCount == 2 and ExitCount == 1.
   }

   [Fact]
   public async Task CancelMidDownload_DoesNotEagerlyExitTransferMode()
   {
       // ... cancellation must NOT trigger ScheduleIdleExit.
   }
   ```

2. **Add provider-level fake-clock tests** under
   [src/BodyCam.Tests/Services/Glasses/HeyCyan/HeyCyanCameraProviderTests.cs](../../../../src/BodyCam.Tests/Services/Glasses/HeyCyan/HeyCyanCameraProviderTests.cs):

   ```csharp
   [Fact]
   public async Task CaptureFrameAsync_TwiceWithinWarmWindow_EntersTransferModeOnce();

   [Fact]
   public async Task CaptureFrameAsync_AfterWarmIdleElapsed_ReentersTransferMode();
   ```

3. **Create the real-hardware benchmark project entry.** Add
   [src/BodyCam.RealTests/HeyCyanCameraLatencyTests.cs](../../../../src/BodyCam.RealTests/HeyCyanCameraLatencyTests.cs).
   Gate every test with `Trait("Category", "RealHardware")` and an
   environment-variable opt-in (`BODYCAM_REAL_HEYCYAN=1`) so CI
   does not run them by accident:

   ```csharp
   public class HeyCyanCameraLatencyTests
   {
       private static bool RealEnabled =>
           Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

       [SkippableFact, Trait("Category", "RealHardware")]
       public async Task CaptureFrameAsync_ColdLatency_IsUnderSixSeconds()
       {
           Skip.IfNot(RealEnabled);
           await using var fixture = await HeyCyanRealFixture.ConnectAsync();
           await fixture.Transfer.ExitAsync(default);   // force COLD path
           await Task.Delay(TimeSpan.FromSeconds(15));  // ensure group torn down

           var sw = Stopwatch.StartNew();
           var jpg = await fixture.Camera.CaptureFrameAsync(default);
           sw.Stop();

           jpg.Should().StartWith(new byte[] { 0xFF, 0xD8 });
           sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6));
           TestContext.Current.AddAttachment("cold-ms", sw.ElapsedMilliseconds.ToString());
       }

       [SkippableFact, Trait("Category", "RealHardware")]
       public async Task CaptureFrameAsync_WarmLatency_IsUnderTwoSeconds()
       {
           Skip.IfNot(RealEnabled);
           await using var fixture = await HeyCyanRealFixture.ConnectAsync();

           // Prime the warm session with one capture, then measure the next.
           _ = await fixture.Camera.CaptureFrameAsync(default);

           var sw = Stopwatch.StartNew();
           var jpg = await fixture.Camera.CaptureFrameAsync(default);
           sw.Stop();

           jpg.Should().StartWith(new byte[] { 0xFF, 0xD8 });
           sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
       }
   }
   ```

4. **Build the `HeyCyanRealFixture`.** Place at
   [src/BodyCam.RealTests/Fixtures/HeyCyanRealFixture.cs](../../../../src/BodyCam.RealTests/Fixtures/HeyCyanRealFixture.cs).
   It boots a minimal DI container (mirroring
   `MauiProgram.Android.cs`), scans + connects to the first paired
   glasses by `BODYCAM_REAL_HEYCYAN_MAC` env var, and yields the
   `HeyCyanCameraProvider` plus the `IHeyCyanMediaTransfer`.

5. **Capture an N-sample latency table.** Add a separate test that
   runs 10 cold + 10 warm captures and writes percentiles to a CSV
   under [TestResults/heycyan-latency.csv](../../../../TestResults/heycyan-latency.csv):

   ```csharp
   [SkippableFact, Trait("Category", "RealHardware")]
   public async Task CaptureFrameAsync_LatencyDistribution_RecordedToCsv();
   ```

   Columns: `iteration, mode (cold|warm), ms, jpg_bytes`. Compute
   `p50` / `p95` for each mode and assert
   `p95(cold) <= 6000 && p95(warm) <= 2000`.

6. **PowerShell harness for benchmark runs.** Add
   [src/BodyCam.RealTests/run-heycyan-latency.ps1](../../../../src/BodyCam.RealTests/run-heycyan-latency.ps1):

   ```powershell
   param([Parameter(Mandatory)][string]$Mac)
   $env:BODYCAM_REAL_HEYCYAN     = '1'
   $env:BODYCAM_REAL_HEYCYAN_MAC = $Mac
   dotnet test src\BodyCam.RealTests\BodyCam.RealTests.csproj `
       --filter "FullyQualifiedName~HeyCyanCameraLatencyTests" `
       --logger "console;verbosity=normal"
   ```

7. **Document results in M11 docs.** Append a "Camera latency
   measurements" subsection to the M11 docs page, paste the CSV
   percentiles plus the date and firmware version of the glasses
   under test (read via `IHeyCyanGlassesSession.GetVersionAsync`).
   Cross-link back to this wave for the methodology.

8. **CI gating.** In the `.github/workflows` file that runs the unit
   suite, ensure `BodyCam.RealTests` is **excluded** by default
   (filter on `Trait("Category") != "RealHardware"`). Add a separate
   manual-dispatch job for benchmarks that requires the
   `BODYCAM_REAL_HEYCYAN` secret to be set.

## Verify

- [ ] Unit tests for warm reuse, idle expiry, and cancellation pass
      with `FakeTimeProvider` (no real wall-clock waits).
- [ ] `BodyCam.RealTests` skip cleanly when
      `BODYCAM_REAL_HEYCYAN` is unset (no failures, no hangs).
- [ ] On real hardware, cold-capture `p95 <= 6000 ms` recorded in
      [TestResults/heycyan-latency.csv](../../../../TestResults/heycyan-latency.csv).
- [ ] On real hardware, warm-capture `p95 <= 2000 ms` recorded in
      the same CSV.
- [ ] All recorded JPGs start with `FF D8` (no truncated transfers).
- [ ] `IHeyCyanGlassesSession.EnterTransferModeAsync` invocation
      count over a 10-warm-capture run equals 1 (verified via test
      hook on the session counter).
- [ ] Latency table + glasses firmware/hardware versions documented
      in M11 docs, with date stamp.
- [ ] CI does not run real-hardware tests by default; manual
      dispatch job exists and is documented in
      [docs/testing.md](../../../../docs/testing.md).
