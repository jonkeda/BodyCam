# Phase 3 / Wave 5 — Tests

**Parent:** [../phase3-audio-providers.md](../phase3-audio-providers.md) ·
**Siblings:** [Wave 1](wave1-heycyan-audio-input-provider.md) ·
[Wave 2](wave2-heycyan-audio-output-provider.md) ·
[Wave 3](wave3-auto-routing-service.md) ·
[Wave 4](wave4-a2dp-codec-verification.md)

## Goal

Cover the routing logic and provider gating built in Waves 1–4 with unit
tests that run **without real BT hardware and without QCSDK**. All tests
live in `src/BodyCam.Tests/Services/Glasses/HeyCyan/` and use xUnit +
FluentAssertions, per the repo conventions.

Three fakes carry the suite:
1. `FakeHeyCyanSession` — drives `StateChanged` from tests.
2. `FakeBluetoothAudioInputProvider` / `FakeBluetoothAudioOutputProvider` —
   stand in for the M12/M13 generic BT providers; track MAC selection,
   start/stop calls, and replay chunks on demand.
3. `FakeAudioInputManager` / `FakeAudioOutputManager` — minimal stubs that
   record `SetActiveProviderAsync` calls so the router can be observed.

Codec probe tests use a `StubCodecProbe` since real `BluetoothA2dp` is
Android-only and unavailable to xUnit.

## Steps

1. Add the fakes under
   `src/BodyCam.Tests/Services/Glasses/HeyCyan/Fakes/`:

    ```csharp
    // FakeHeyCyanSession.cs
    public sealed class FakeHeyCyanSession : IHeyCyanGlassesSession
    {
        public HeyCyanState State { get; private set; } = HeyCyanState.Disconnected;
        public HeyCyanDeviceInfo? Device { get; private set; }
        public event EventHandler<HeyCyanState>? StateChanged;
        public event EventHandler<HeyCyanBattery>? BatteryUpdated;
        public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
        public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
        public event EventHandler<byte[]>? AiPhotoReceived;

        public void RaiseConnected(string mac)
        {
            Device = new HeyCyanDeviceInfo("Glasses", mac, -50);
            State  = HeyCyanState.Connected;
            StateChanged?.Invoke(this, State);
        }

        public void RaiseDisconnected()
        {
            State  = HeyCyanState.Disconnected;
            Device = null;
            StateChanged?.Invoke(this, State);
        }

        public void RaiseTransferMode()
        {
            State = HeyCyanState.TransferMode;
            StateChanged?.Invoke(this, State);
        }

        // Remaining members throw NotSupportedException — Phase 3 does not
        // exercise scan/connect/photo/etc.
        public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan t, CancellationToken ct) => throw new NotSupportedException();
        public Task ConnectAsync(HeyCyanDeviceInfo d, CancellationToken ct)               => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken ct)                                 => throw new NotSupportedException();
        public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)             => throw new NotSupportedException();
        public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)                 => throw new NotSupportedException();
        public Task SyncTimeAsync(CancellationToken ct)                                   => throw new NotSupportedException();
        public Task TakePhotoAsync(CancellationToken ct)                                  => throw new NotSupportedException();
        public Task StartVideoAsync(CancellationToken ct)                                 => throw new NotSupportedException();
        public Task StopVideoAsync(CancellationToken ct)                                  => throw new NotSupportedException();
        public Task StartAudioAsync(CancellationToken ct)                                 => throw new NotSupportedException();
        public Task StopAudioAsync(CancellationToken ct)                                  => throw new NotSupportedException();
        public Task TakeAiPhotoAsync(CancellationToken ct)                                => throw new NotSupportedException();
        public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)  => throw new NotSupportedException();
        public ValueTask DisposeAsync() => default;
    }
    ```

   The BT provider fakes track MAC selection and start/stop:

    ```csharp
    public sealed class FakeBluetoothAudioInputProvider : IBluetoothAudioInputProvider
    {
        private readonly HashSet<string> _macs;
        public FakeBluetoothAudioInputProvider(IEnumerable<string> macsAvailable)
            => _macs = new(macsAvailable, StringComparer.OrdinalIgnoreCase);

        public string ProviderId  => "bluetooth";
        public string DisplayName => "BT Mic (fake)";
        public bool IsAvailable  => true;
        public bool IsCapturing  { get; private set; }
        public string? SelectedMac { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount  { get; private set; }

        public event EventHandler<byte[]>? AudioChunkAvailable;
        public event EventHandler? Disconnected;

        public bool HasEndpointWithMac(string? mac) =>
            mac is not null && _macs.Contains(mac.Replace('-', ':'));

        public Task SelectEndpointByMacAsync(string mac, CancellationToken ct)
        { SelectedMac = mac; return Task.CompletedTask; }

        public Task StartAsync(CancellationToken ct = default)
        { StartCount++; IsCapturing = true; return Task.CompletedTask; }

        public Task StopAsync(CancellationToken ct = default)
        { StopCount++; IsCapturing = false; return Task.CompletedTask; }

        public void RaiseChunk(byte[] pcm)        => AudioChunkAvailable?.Invoke(this, pcm);
        public void RaiseDisconnected()           => Disconnected?.Invoke(this, EventArgs.Empty);
    }
    ```

   `FakeBluetoothAudioOutputProvider` is symmetric (`PlayChunkAsync`
   appends to a `List<byte[]>` so tests can assert payload pass-through).

2. Add `FakeAudioInputManager` / `FakeAudioOutputManager` (or use a real
   manager if M12/M13 already supply test doubles). They expose:
   - `string ActiveProviderId { get; }`
   - `Task SetActiveProviderAsync(string id)` — appends to a history list.

3. Write the test classes under
   `src/BodyCam.Tests/Services/Glasses/HeyCyan/`:

    ```csharp
    // HeyCyanAudioInputProviderTests.cs
    public class HeyCyanAudioInputProviderTests
    {
        private const string Mac = "AA:BB:CC:DD:EE:FF";

        [Fact]
        public void IsAvailable_RequiresConnectedAndMatchingMac()
        {
            var session = new FakeHeyCyanSession();
            var bt      = new FakeBluetoothAudioInputProvider(new[] { Mac });
            var sut     = new HeyCyanAudioInputProvider(session, bt, NullLogger<HeyCyanAudioInputProvider>.Instance);

            sut.IsAvailable.Should().BeFalse();
            session.RaiseConnected(Mac);
            sut.IsAvailable.Should().BeTrue();
            session.RaiseConnected("11:22:33:44:55:66");
            sut.IsAvailable.Should().BeFalse();
        }

        [Fact]
        public async Task StartAsync_SelectsMacBeforeStartingInner()
        {
            var session = new FakeHeyCyanSession();
            var bt      = new FakeBluetoothAudioInputProvider(new[] { Mac });
            var sut     = new HeyCyanAudioInputProvider(session, bt, NullLogger<HeyCyanAudioInputProvider>.Instance);
            session.RaiseConnected(Mac);

            await sut.StartAsync();

            bt.SelectedMac.Should().Be(Mac);
            bt.StartCount.Should().Be(1);
        }

        [Fact]
        public async Task StartAsync_Throws_WhenNoMatchingEndpoint()
        {
            var session = new FakeHeyCyanSession();
            var bt      = new FakeBluetoothAudioInputProvider(Array.Empty<string>());
            var sut     = new HeyCyanAudioInputProvider(session, bt, NullLogger<HeyCyanAudioInputProvider>.Instance);
            session.RaiseConnected(Mac);

            await sut.Awaiting(s => s.StartAsync()).Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public void StateChanged_ToDisconnected_StopsCapture()
        {
            var session = new FakeHeyCyanSession();
            var bt      = new FakeBluetoothAudioInputProvider(new[] { Mac });
            var sut     = new HeyCyanAudioInputProvider(session, bt, NullLogger<HeyCyanAudioInputProvider>.Instance);
            session.RaiseConnected(Mac);
            _ = sut.StartAsync();

            session.RaiseDisconnected();

            bt.StopCount.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Chunks_FromInner_ReEmitUnchanged()
        {
            var session = new FakeHeyCyanSession();
            var bt      = new FakeBluetoothAudioInputProvider(new[] { Mac });
            var sut     = new HeyCyanAudioInputProvider(session, bt, NullLogger<HeyCyanAudioInputProvider>.Instance);
            byte[]? received = null;
            sut.AudioChunkAvailable += (_, c) => received = c;

            bt.RaiseChunk(new byte[] { 1, 2, 3, 4 });

            received.Should().Equal(1, 2, 3, 4);
        }
    }
    ```

   Mirror the same shape in `HeyCyanAudioOutputProviderTests` (assert
   `PlayChunkAsync` forwards bytes verbatim; assert stop on disconnect).

4. Router tests:

    ```csharp
    public class HeyCyanAudioRouterTests
    {
        [Fact]
        public async Task Connected_RoutesBothManagers()
        {
            var session = new FakeHeyCyanSession();
            var input   = new FakeAudioInputManager(initialId: "platform");
            var output  = new FakeAudioOutputManager(initialId: "platform");
            await using var router = new HeyCyanAudioRouter(session, input, output, NullLogger<HeyCyanAudioRouter>.Instance);

            session.RaiseConnected("AA:BB:CC:DD:EE:FF");
            await Task.Delay(10);

            input.ActiveProviderId.Should().Be("heycyan-glasses");
            output.ActiveProviderId.Should().Be("heycyan-glasses");
        }

        [Fact]
        public async Task Disconnected_RestoresPreviousProviders()
        {
            var session = new FakeHeyCyanSession();
            var input   = new FakeAudioInputManager(initialId: "platform");
            var output  = new FakeAudioOutputManager(initialId: "platform");
            await using var router = new HeyCyanAudioRouter(session, input, output, NullLogger<HeyCyanAudioRouter>.Instance);

            session.RaiseConnected("AA:BB:CC:DD:EE:FF");
            await Task.Delay(10);
            session.RaiseDisconnected();
            await Task.Delay(10);

            input.ActiveProviderId.Should().Be("platform");
            output.ActiveProviderId.Should().Be("platform");
        }

        [Fact]
        public async Task TransferMode_DoesNotFlipRouting()
        {
            var session = new FakeHeyCyanSession();
            var input   = new FakeAudioInputManager("platform");
            var output  = new FakeAudioOutputManager("platform");
            await using var router = new HeyCyanAudioRouter(session, input, output, NullLogger<HeyCyanAudioRouter>.Instance);
            session.RaiseConnected("AA:BB:CC:DD:EE:FF"); await Task.Delay(10);

            session.RaiseTransferMode(); await Task.Delay(10);

            input.ActiveProviderId.Should().Be("heycyan-glasses");
            output.ActiveProviderId.Should().Be("heycyan-glasses");
        }

        [Fact]
        public async Task RapidToggle_DoesNotLeakSnapshotIntoHeyCyan()
        {
            var session = new FakeHeyCyanSession();
            var input   = new FakeAudioInputManager("platform");
            var output  = new FakeAudioOutputManager("platform");
            await using var router = new HeyCyanAudioRouter(session, input, output, NullLogger<HeyCyanAudioRouter>.Instance);

            for (var i = 0; i < 25; i++)
            {
                session.RaiseConnected("AA:BB:CC:DD:EE:FF"); await Task.Delay(2);
                session.RaiseDisconnected();                  await Task.Delay(2);
            }

            input.ActiveProviderId.Should().Be("platform");
            output.ActiveProviderId.Should().Be("platform");
        }
    }
    ```

5. Diagnostics tests use a `StubCodecProbe` returning a fixed
   `HeyCyanAudioRouteInfo` and assert:
   - `Current` is null until `Connected`.
   - `Updated` fires once per successful refresh.
   - A throwing probe sets `Current = null` and does not propagate.

6. CI: ensure these tests are listed under the default
   `dotnet test src/BodyCam.Tests` filter — no special trait is required
   because none of them touch real hardware.

## Verify

- [ ] All five test classes compile and pass under
      `dotnet test src/BodyCam.Tests`.
- [ ] No test references real BT or QCSDK assemblies.
- [ ] No test depends on `Thread.Sleep` longer than ~50 ms (use
      `await Task.Delay` to let async-void handlers run).
- [ ] Router tests cover: connect, disconnect, transfer-mode, rapid toggle.
- [ ] Provider tests cover: MAC gating, MAC-before-start ordering, missing
      endpoint throwing, stop-on-disconnect, chunk pass-through.
- [ ] Diagnostics tests cover: connected refresh, throwing probe, iOS
      `null` codec path.
- [ ] Tests live in `src/BodyCam.Tests/Services/Glasses/HeyCyan/` and
      follow the project's xUnit + FluentAssertions style.
- [ ] No handler leaks: a 100-cycle stress test asserts subscription
      counts on the fakes return to zero after `DisposeAsync`.
