# M33 Phase 4 — Wave 4: Tests

**Parent:** [`../phase4-button-provider.md`](../phase4-button-provider.md)
**Siblings:** [wave1](wave1-heycyan-button-provider.md) · [wave2](wave2-default-gesture-mapping.md) · [wave3](wave3-settings-ui.md)
**Depends on:** Waves 1–3.

## Goal

Lock in Wave 1–3 behavior with deterministic unit tests under
`BodyCam.Tests/Services/Glasses/HeyCyan/`. All tests use a hand-rolled
`FakeHeyCyanSession` — no real BLE, no platform code, no async timing
dependencies. Use xUnit + FluentAssertions per repo convention.

## Steps

1. **Create the fake session** at
   `src/BodyCam.Tests/Services/Glasses/HeyCyan/FakeHeyCyanSession.cs`:

    ```csharp
    namespace BodyCam.Tests.Services.Glasses.HeyCyan;

    internal sealed class FakeHeyCyanSession : IHeyCyanGlassesSession
    {
        private HeyCyanState _state = HeyCyanState.Connected;

        public HeyCyanState State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                StateChanged?.Invoke(this, value);
            }
        }

        public HeyCyanDeviceInfo? Device => null;

        public event EventHandler<HeyCyanState>?       StateChanged;
        public event EventHandler<HeyCyanBattery>?     BatteryUpdated;
        public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
        public event EventHandler<HeyCyanMediaCount>?  MediaCountUpdated;
        public event EventHandler<byte[]>?             AiPhotoReceived;

        public void RaiseButton(HeyCyanButtonGesture g)
            => ButtonPressed?.Invoke(this, new HeyCyanButtonEvent(g, DateTimeOffset.UtcNow));

        // Members not exercised by Phase 4 tests:
        public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan t, CancellationToken ct) => throw new NotImplementedException();
        public Task ConnectAsync(HeyCyanDeviceInfo d, CancellationToken ct)                       => throw new NotImplementedException();
        public Task DisconnectAsync(CancellationToken ct)                                          => throw new NotImplementedException();
        public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)                      => throw new NotImplementedException();
        public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)                          => throw new NotImplementedException();
        public Task SyncTimeAsync(CancellationToken ct)                                            => throw new NotImplementedException();
        public Task TakePhotoAsync(CancellationToken ct)                                           => throw new NotImplementedException();
        public Task StartVideoAsync(CancellationToken ct)                                          => throw new NotImplementedException();
        public Task StopVideoAsync(CancellationToken ct)                                           => throw new NotImplementedException();
        public Task StartAudioAsync(CancellationToken ct)                                          => throw new NotImplementedException();
        public Task StopAudioAsync(CancellationToken ct)                                           => throw new NotImplementedException();
        public Task TakeAiPhotoAsync(CancellationToken ct)                                         => throw new NotImplementedException();
        public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)           => throw new NotImplementedException();
        public ValueTask DisposeAsync() => default;
    }
    ```

2. **Provider forwarding tests** at `HeyCyanButtonProviderTests.cs`:

    ```csharp
    public class HeyCyanButtonProviderTests
    {
        private static HeyCyanButtonProvider Make(out FakeHeyCyanSession session)
        {
            session = new FakeHeyCyanSession();
            return new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
        }

        [Theory]
        [InlineData(HeyCyanButtonGesture.Tap,       ButtonGesture.Tap)]
        [InlineData(HeyCyanButtonGesture.DoubleTap, ButtonGesture.DoubleTap)]
        [InlineData(HeyCyanButtonGesture.LongPress, ButtonGesture.LongPress)]
        public async Task ForwardsGesture_AsPreRecognized(
            HeyCyanButtonGesture input, ButtonGesture expected)
        {
            var provider = Make(out var session);
            await provider.StartAsync();

            ButtonGestureEvent? captured = null;
            provider.PreRecognizedGesture += (_, e) => captured = e;
            session.RaiseButton(input);

            captured.Should().NotBeNull();
            captured!.Gesture.Should().Be(expected);
            captured.ProviderId.Should().Be("heycyan-glasses");
            captured.ButtonId.Should().Be("glasses-button");
        }

        [Fact]
        public async Task RawButtonEvent_IsNeverRaised()
        {
            var provider = Make(out var session);
            await provider.StartAsync();
            var rawFired = false;
            provider.RawButtonEvent += (_, _) => rawFired = true;

            session.RaiseButton(HeyCyanButtonGesture.Tap);
            session.RaiseButton(HeyCyanButtonGesture.DoubleTap);
            session.RaiseButton(HeyCyanButtonGesture.LongPress);

            rawFired.Should().BeFalse();
        }

        [Fact]
        public async Task StopAsync_Unsubscribes_AndIsIdempotent()
        {
            var provider = Make(out var session);
            await provider.StartAsync();
            await provider.StopAsync();
            await provider.StopAsync(); // second call must not throw

            var fired = false;
            provider.PreRecognizedGesture += (_, _) => fired = true;
            session.RaiseButton(HeyCyanButtonGesture.Tap);

            fired.Should().BeFalse();
        }

        [Fact]
        public async Task IsAvailable_TracksSessionState()
        {
            var provider = Make(out var session);
            await provider.StartAsync();

            session.State = HeyCyanState.Connected;     provider.IsAvailable.Should().BeTrue();
            session.State = HeyCyanState.TransferMode;  provider.IsAvailable.Should().BeTrue();
            session.State = HeyCyanState.Disconnected;  provider.IsAvailable.Should().BeFalse();
        }
    }
    ```

3. **Defaults tests** at `HeyCyanButtonDefaultsTests.cs`:

    ```csharp
    public class HeyCyanButtonDefaultsTests
    {
        [Fact]
        public void SeedDefaults_PopulatesAllThreeGestures_OnEmptyStore()
        {
            var store = new InMemoryButtonMappingStore();
            var map   = new ActionMap(store);

            HeyCyanButtonDefaults.SeedDefaults(map);

            store.Get("heycyan-glasses", "glasses-button", ButtonGesture.Tap)
                 .Should().Be(ButtonAction.ToggleConversation);
            store.Get("heycyan-glasses", "glasses-button", ButtonGesture.DoubleTap)
                 .Should().Be(ButtonAction.CapturePhoto);
            store.Get("heycyan-glasses", "glasses-button", ButtonGesture.LongPress)
                 .Should().Be(ButtonAction.EndSession);
        }

        [Fact]
        public void SeedDefaults_DoesNotOverwrite_ExistingUserMappings()
        {
            var store = new InMemoryButtonMappingStore();
            store.Set("heycyan-glasses", "glasses-button", ButtonGesture.Tap, ButtonAction.Look);
            var map = new ActionMap(store);

            HeyCyanButtonDefaults.SeedDefaults(map);
            HeyCyanButtonDefaults.SeedDefaults(map); // simulates a second launch

            store.Get("heycyan-glasses", "glasses-button", ButtonGesture.Tap)
                 .Should().Be(ButtonAction.Look);
        }
    }
    ```

4. **End-to-end dispatch test** at `HeyCyanButtonDispatchTests.cs`:

    ```csharp
    public class HeyCyanButtonDispatchTests
    {
        [Fact]
        public async Task RemappedGesture_TriggersNewAction_LiveWithoutRestart()
        {
            var session  = new FakeHeyCyanSession();
            var provider = new HeyCyanButtonProvider(session, NullLogger<HeyCyanButtonProvider>.Instance);
            var store    = new InMemoryButtonMappingStore();
            var actionMap = new ActionMap(store);
            HeyCyanButtonDefaults.SeedDefaults(actionMap);

            var manager = new ButtonInputManager(new IButtonInputProvider[] { provider }, actionMap);
            await manager.StartAsync();

            ButtonAction? triggered = null;
            manager.ActionTriggered += (_, e) => triggered = e.Action;

            // Default: Tap → ToggleConversation
            session.RaiseButton(HeyCyanButtonGesture.Tap);
            triggered.Should().Be(ButtonAction.ToggleConversation);

            // User remap: Tap → CapturePhoto
            store.Set(HeyCyanButtonDefaults.ProviderId,
                      HeyCyanButtonDefaults.ButtonId,
                      ButtonGesture.Tap,
                      ButtonAction.CapturePhoto);
            triggered = null;
            session.RaiseButton(HeyCyanButtonGesture.Tap);
            triggered.Should().Be(ButtonAction.CapturePhoto);
        }
    }
    ```

5. **No timing dependencies.** Because all events are pre-recognized and
   raised synchronously off `FakeHeyCyanSession.RaiseButton`, no
   `Task.Delay`, `WaitAsync`, or `GestureRecognizer` debounce timers
   appear in any test. If you find yourself reaching for a delay, the
   fake or the production code is wrong.

6. **Wire into CI.** These tests live in the existing `BodyCam.Tests`
   project and pick up automatically through `dotnet test`. No new csproj.

## Verify

- [ ] All `HeyCyanButtonProviderTests` pass: forwarding, raw-never-fires,
      stop-unsubscribe, idempotent-stop, availability tracking
- [ ] `HeyCyanButtonDefaultsTests` confirm seed-on-empty and
      preserve-on-repeat behavior
- [ ] `HeyCyanButtonDispatchTests` confirm live remap takes effect
      without restart
- [ ] No test uses `Task.Delay`, `Thread.Sleep`, or relies on
      `GestureRecognizer` timing
- [ ] `dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj` is green
- [ ] Coverage of `HeyCyanButtonProvider` and `HeyCyanButtonDefaults`
      is 100% line/branch (per `dotnet test --collect:"XPlat Code Coverage"`)
