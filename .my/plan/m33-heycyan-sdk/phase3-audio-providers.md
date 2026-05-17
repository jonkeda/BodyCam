# M33 Phase 3 — Audio Providers (BT classic, live conversation)

Wrap the generic Bluetooth audio providers with HeyCyan-specific filtering and
auto-routing tied to the QCSDK glasses session, so live Realtime-API audio
flows through the glasses' BT Classic A2DP (speaker) + HFP/SCO (mic) the
moment QCSDK reports `Connected`, and falls back to the phone defaults the
moment it disconnects.

**Depends on:** M33 Phase 1 (`IHeyCyanGlassesSession`, MAC address from
`HeyCyanVersionInfo`), M12 Phase 2 (generic `BluetoothAudioInputProvider`),
M13 Phase 2 (generic `BluetoothAudioOutputProvider`).

**Key fact:** The QCSDK does **not** carry live audio. The glasses expose
themselves as a normal BT headset on the same physical device — A2DP for
playback, HFP/SCO for capture. The QCSDK is used here only to *trigger*
routing decisions, not to move audio bytes. Codec guarantees are whatever
the BT stack negotiates; document SBC as the minimum baseline (no aptX /
LDAC promises from this hardware).

---

## Wave 1: `HeyCyanAudioInputProvider`

A thin `IAudioInputProvider` that wraps the generic `BluetoothAudioInputProvider`
and constrains it to the BT endpoint whose MAC matches the QCSDK-paired
device. Reports `IsAvailable` only when the QCSDK session is `Connected`
**and** a matching BT capture endpoint is present.

```csharp
// Services/Glasses/HeyCyan/HeyCyanAudioInputProvider.cs
namespace BodyCam.Services.Glasses.HeyCyan;

public sealed class HeyCyanAudioInputProvider : IAudioInputProvider, IAsyncDisposable
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly IBluetoothAudioInputProvider _bt; // generic M12/P2 provider
    private readonly ILogger<HeyCyanAudioInputProvider> _log;

    public string ProviderId => "heycyan-glasses";
    public string DisplayName => "HeyCyan Glasses Mic";

    public bool IsAvailable =>
        _session.State == HeyCyanState.Connected &&
        _bt.HasEndpointWithMac(_session.Device?.Address);

    public bool IsCapturing => _bt.IsCapturing;

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public HeyCyanAudioInputProvider(
        IHeyCyanGlassesSession session,
        IBluetoothAudioInputProvider bt,
        ILogger<HeyCyanAudioInputProvider> log)
    {
        _session = session;
        _bt = bt;
        _log = log;

        _bt.AudioChunkAvailable += (_, c) => AudioChunkAvailable?.Invoke(this, c);
        _bt.Disconnected        += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);
        _session.StateChanged   += OnSessionStateChanged;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var mac = _session.Device?.Address
            ?? throw new InvalidOperationException("HeyCyan glasses not connected.");

        await _bt.SelectEndpointByMacAsync(mac, ct);
        await _bt.StartAsync(ct);
    }

    public Task StopAsync(CancellationToken ct = default) => _bt.StopAsync(ct);

    private void OnSessionStateChanged(object? sender, HeyCyanState state)
    {
        if (state != HeyCyanState.Connected && _bt.IsCapturing)
            _ = _bt.StopAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        _session.StateChanged -= OnSessionStateChanged;
        if (_bt is IAsyncDisposable d) await d.DisposeAsync();
    }
}
```

### Verify
- [ ] `IsAvailable` is `false` until `IHeyCyanGlassesSession.State == Connected`
      and a BT capture endpoint matching `Device.Address` is present.
- [ ] `StartAsync` selects the endpoint by MAC before starting the generic
      provider; refuses to start when no MAC match exists.
- [ ] `AudioChunkAvailable` re-emits PCM16 24 kHz mono chunks unchanged from
      the generic provider.
- [ ] On `StateChanged → Disconnected`, capture is stopped.

---

## Wave 2: `HeyCyanAudioOutputProvider`

Symmetric to Wave 1: wraps the generic `BluetoothAudioOutputProvider`, locks
to the MAC from `HeyCyanVersionInfo`, and only reports `IsAvailable` while
the QCSDK session is `Connected`.

```csharp
// Services/Glasses/HeyCyan/HeyCyanAudioOutputProvider.cs
public sealed class HeyCyanAudioOutputProvider : IAudioOutputProvider, IAsyncDisposable
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly IBluetoothAudioOutputProvider _bt;
    private readonly ILogger<HeyCyanAudioOutputProvider> _log;

    public string ProviderId => "heycyan-glasses";
    public string DisplayName => "HeyCyan Glasses Speaker";

    public bool IsAvailable =>
        _session.State == HeyCyanState.Connected &&
        _bt.HasEndpointWithMac(_session.Device?.Address);

    public bool IsPlaying => _bt.IsPlaying;

    public event EventHandler? Disconnected;

    public HeyCyanAudioOutputProvider(
        IHeyCyanGlassesSession session,
        IBluetoothAudioOutputProvider bt,
        ILogger<HeyCyanAudioOutputProvider> log)
    {
        _session = session;
        _bt = bt;
        _log = log;

        _bt.Disconnected      += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);
        _session.StateChanged += OnSessionStateChanged;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var mac = _session.Device?.Address
            ?? throw new InvalidOperationException("HeyCyan glasses not connected.");

        await _bt.SelectEndpointByMacAsync(mac, ct);
        await _bt.StartAsync(ct);
    }

    public Task PlayChunkAsync(byte[] pcm16, CancellationToken ct = default) =>
        _bt.PlayChunkAsync(pcm16, ct);

    public Task StopAsync(CancellationToken ct = default) => _bt.StopAsync(ct);

    private void OnSessionStateChanged(object? sender, HeyCyanState state)
    {
        if (state != HeyCyanState.Connected && _bt.IsPlaying)
            _ = _bt.StopAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        _session.StateChanged -= OnSessionStateChanged;
        if (_bt is IAsyncDisposable d) await d.DisposeAsync();
    }
}
```

### Verify
- [ ] `IsAvailable` follows the same gating as the input provider.
- [ ] `PlayChunkAsync` round-trips PCM16 24 kHz mono to the generic provider.
- [ ] On `StateChanged → Disconnected`, playback is stopped.

---

## Wave 3: Auto-Routing Service

The providers above are *passive* — they only describe themselves. A small
`HeyCyanAudioRouter` subscribes to `IHeyCyanGlassesSession.StateChanged` and
flips the active providers on `AudioInputManager` / `AudioOutputManager` so
the live conversation moves to the glasses on `Connected` and back to phone
defaults on `Disconnected`.

```csharp
// Services/Glasses/HeyCyan/HeyCyanAudioRouter.cs
public sealed class HeyCyanAudioRouter : IAsyncDisposable
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly AudioInputManager _input;   // M12
    private readonly AudioOutputManager _output; // M13
    private readonly ILogger<HeyCyanAudioRouter> _log;

    private string? _previousInputId;
    private string? _previousOutputId;

    public HeyCyanAudioRouter(
        IHeyCyanGlassesSession session,
        AudioInputManager input,
        AudioOutputManager output,
        ILogger<HeyCyanAudioRouter> log)
    {
        _session = session;
        _input = input;
        _output = output;
        _log = log;

        _session.StateChanged += OnStateChanged;
    }

    private async void OnStateChanged(object? sender, HeyCyanState state)
    {
        try
        {
            switch (state)
            {
                case HeyCyanState.Connected:
                    _previousInputId  = _input.ActiveProviderId;
                    _previousOutputId = _output.ActiveProviderId;
                    await _input.SetActiveProviderAsync("heycyan-glasses");
                    await _output.SetActiveProviderAsync("heycyan-glasses");
                    _log.LogInformation("Routed live audio to HeyCyan glasses ({Mac}).",
                        _session.Device?.Address);
                    break;

                case HeyCyanState.Disconnected:
                    await _input.SetActiveProviderAsync(_previousInputId  ?? "platform");
                    await _output.SetActiveProviderAsync(_previousOutputId ?? "platform");
                    _log.LogInformation("Glasses disconnected — fell back to phone audio.");
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HeyCyan audio routing failed (state={State}).", state);
        }
    }

    public ValueTask DisposeAsync()
    {
        _session.StateChanged -= OnStateChanged;
        return default;
    }
}
```

DI registration (cross-platform, alongside Phase 1):

```csharp
services.AddSingleton<HeyCyanAudioInputProvider>();
services.AddSingleton<HeyCyanAudioOutputProvider>();
services.AddSingleton<IAudioInputProvider>(sp => sp.GetRequiredService<HeyCyanAudioInputProvider>());
services.AddSingleton<IAudioOutputProvider>(sp => sp.GetRequiredService<HeyCyanAudioOutputProvider>());
services.AddSingleton<HeyCyanAudioRouter>();
// Resolve once at startup so it subscribes to StateChanged.
```

### Verify
- [ ] On `Connected`, `AudioInputManager.ActiveProviderId == "heycyan-glasses"`.
- [ ] On `Connected`, `AudioOutputManager.ActiveProviderId == "heycyan-glasses"`.
- [ ] On `Disconnected`, both managers fall back to whatever was active
      before the glasses connected (phone defaults if nothing else).
- [ ] Router survives repeated connect/disconnect cycles without leaking
      handlers.
- [ ] An in-flight Realtime conversation continues without dropping frames
      across the routing flip (verified end-to-end in Phase 7).

---

## Wave 4: A2DP Codec Verification & Diagnostics

We don't control codec negotiation, but we *do* surface what got negotiated
so the user can see whether they're on SBC (baseline) or something better.

```csharp
public sealed record HeyCyanAudioRouteInfo(
    string InputProviderId,
    string OutputProviderId,
    string? NegotiatedA2dpCodec, // "SBC", "AAC", "aptX", "LDAC", or null if unknown
    int    SampleRateHz,
    int    Channels,
    string? HfpCodec);           // "CVSD" or "mSBC", null if unknown

public interface IHeyCyanAudioDiagnostics
{
    HeyCyanAudioRouteInfo? Current { get; }
    event EventHandler<HeyCyanAudioRouteInfo>? Updated;
}
```

- Android: read codec via `BluetoothA2dp.getCodecStatus(device)` (API 28+)
  and HFP codec via `BluetoothHeadset` reflection where available.
- iOS: codec details are largely opaque; report `null` and document this in
  the diagnostics page.
- Minimum guarantee documented in `docs/glasses-audio.md`: **SBC mono/stereo,
  no aptX/LDAC commitment**. mSBC for HFP if the OS chooses it; CVSD
  otherwise.

### Verify
- [ ] Diagnostics surface populated within 1 s of `Connected` on Android.
- [ ] iOS path returns `null` codec without throwing.
- [ ] Settings page displays `Current` route info live.
- [ ] Documentation explicitly states SBC-minimum, no aptX/LDAC promise.

---

## Wave 5: Tests

Use a fake `IHeyCyanGlassesSession` and fakes for the generic BT input /
output providers so routing logic can be exercised without hardware.

```csharp
public sealed class FakeHeyCyanSession : IHeyCyanGlassesSession
{
    public HeyCyanState State { get; private set; } = HeyCyanState.Disconnected;
    public HeyCyanDeviceInfo? Device { get; private set; }

    public event EventHandler<HeyCyanState>? StateChanged;
    // ...other events stubbed...

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

    // remaining IHeyCyanGlassesSession members throw NotSupportedException
}

public class HeyCyanAudioRouterTests
{
    [Fact]
    public async Task Connected_RoutesBothManagersToHeyCyan()
    {
        var session = new FakeHeyCyanSession();
        var input   = new FakeAudioInputManager(initialId: "platform");
        var output  = new FakeAudioOutputManager(initialId: "platform");
        using var router = new HeyCyanAudioRouter(session, input, output, NullLogger<HeyCyanAudioRouter>.Instance);

        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await Task.Yield();

        input.ActiveProviderId.Should().Be("heycyan-glasses");
        output.ActiveProviderId.Should().Be("heycyan-glasses");
    }

    [Fact]
    public async Task Disconnected_RestoresPreviousProviders()
    {
        var session = new FakeHeyCyanSession();
        var input   = new FakeAudioInputManager(initialId: "platform");
        var output  = new FakeAudioOutputManager(initialId: "platform");
        using var router = new HeyCyanAudioRouter(session, input, output, NullLogger<HeyCyanAudioRouter>.Instance);

        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await Task.Yield();
        session.RaiseDisconnected();
        await Task.Yield();

        input.ActiveProviderId.Should().Be("platform");
        output.ActiveProviderId.Should().Be("platform");
    }
}

public class HeyCyanAudioInputProviderTests
{
    [Fact]
    public void IsAvailable_RequiresConnectedAndMatchingMac()
    {
        var session = new FakeHeyCyanSession();
        var bt = new FakeBluetoothAudioInputProvider(macsAvailable: new[] { "AA:BB:CC:DD:EE:FF" });
        var sut = new HeyCyanAudioInputProvider(session, bt, NullLogger<HeyCyanAudioInputProvider>.Instance);

        sut.IsAvailable.Should().BeFalse();
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        sut.IsAvailable.Should().BeTrue();
        session.RaiseConnected("11:22:33:44:55:66");
        sut.IsAvailable.Should().BeFalse();
    }
}
```

### Verify
- [ ] Router tests pass for connect, disconnect, and rapid toggling.
- [ ] Provider tests confirm MAC-based gating of `IsAvailable`.
- [ ] No test depends on real BT or QCSDK hardware.
- [ ] Tests live in `BodyCam.Tests/Services/Glasses/HeyCyan/`.

---

## Phase 3 Exit Checklist

- [ ] `HeyCyanAudioInputProvider` and `HeyCyanAudioOutputProvider` implemented
      and registered in DI alongside the generic BT providers.
- [ ] `HeyCyanAudioRouter` subscribes to `IHeyCyanGlassesSession.StateChanged`
      and flips `AudioInputManager` / `AudioOutputManager` accordingly.
- [ ] Auto-fallback to phone mic + speaker on `Disconnected` verified end-to-end.
- [ ] A2DP codec surfaced via `IHeyCyanAudioDiagnostics`; SBC documented as
      minimum baseline (no aptX/LDAC promised by this hardware).
- [ ] Unit tests cover routing, fallback, and MAC-based availability gating.
- [ ] Docs note: post-hoc OPUS recorded-audio retrieval is **out of scope
      for Phase 3** — covered in Phase 5.
