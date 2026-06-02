using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

public class HeyCyanGlassesSessionCoreTests
{
    private static (HeyCyanGlassesSessionCore session, FakeHeyCyanSdkBridge fake) Build()
    {
        var fake = new FakeHeyCyanSdkBridge();
        var session = new HeyCyanGlassesSessionCore(fake, NullLogger.Instance);
        return (session, fake);
    }

    [Fact]
    public async Task ScanAsync_returns_deduplicated_devices()
    {
        var (s, fake) = Build();
        fake.ScriptedScan.AddRange(new[]
        {
            new HeyCyanScanResult("Glasses", "AA:BB", -50),
            new HeyCyanScanResult("Glasses", "AA:BB", -52), // duplicate MAC
            new HeyCyanScanResult("Glasses", "CC:DD", -60),
        });

        var result = await s.ScanAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(r => r.Address).Should().BeEquivalentTo(new[] { "AA:BB", "CC:DD" });
        fake.ScanStarted.Should().BeTrue();
        fake.ScanStopped.Should().BeTrue();
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
        s.State.Should().Be(HeyCyanState.Connected);
        s.Device.Should().NotBeNull();
        s.Device!.Address.Should().Be("AA:BB");
    }

    [Fact]
    public async Task ConnectAsync_when_already_connected_throws()
    {
        var (s, fake) = Build();
        fake.ConnectGate.SetResult();
        await s.ConnectAsync(new("X", "AA:BB", -50), default);

        Func<Task> act = () => s.ConnectAsync(new("X", "AA:BB", -50), default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*connected*");
    }

    [Fact]
    public async Task ConnectAsync_cancellation_propagates()
    {
        var (s, fake) = Build();
        using var cts = new CancellationTokenSource();
        var task = s.ConnectAsync(new("X", "AA:BB", -50), cts.Token);

        cts.Cancel();

        await FluentActions.Awaiting(() => task)
            .Should().ThrowAsync<OperationCanceledException>();
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
        fake.OnSend = _ => new HeyCyanResponse(
            0x02,
            System.Text.Encoding.UTF8.GetBytes("HW1.0,FW2.3,WiFiHW1.1,WiFiFW2.0,AA:BB:CC:DD:EE:FF"));

        var v = await s.GetVersionAsync(default);

        v.Firmware.Should().Be("FW2.3");
        v.Hardware.Should().Be("HW1.0");
    }

    [Fact]
    public async Task SyncTimeAsync_sends_unix_LE_payload()
    {
        var (s, fake) = Build();
        byte[]? sent = null;
        fake.OnSend = p => { sent = p; return new HeyCyanResponse(0x03, []); };

        await s.SyncTimeAsync(default);

        sent.Should().NotBeNull();
        sent![0].Should().Be(0xBC);
        sent[1].Should().Be(0x40);
        sent[2].Should().Be(0x04);
        sent[3].Should().Be(0x00);
        sent.Should().HaveCount(10);
        // bytes 6..9 are unix timestamp LE
        BitConverter.IsLittleEndian.Should().BeTrue();
    }

    [Fact]
    public async Task StartVideoAsync_sends_video_recording_command()
    {
        var (s, fake) = Build();
        fake.OnSend = _ => new HeyCyanResponse(0x41, []);

        await s.StartVideoAsync(default);

        fake.SentCommands.Should().ContainSingle();
        fake.SentCommands[0][1].Should().Be(0x41);
        fake.SentCommands[0].Skip(6).Should().Equal(0x02, 0x01, 0x02);
    }

    [Fact]
    public async Task StopVideoAsync_sends_video_stop_command()
    {
        var (s, fake) = Build();
        fake.OnSend = _ => new HeyCyanResponse(0x41, []);

        await s.StopVideoAsync(default);

        fake.SentCommands.Should().ContainSingle();
        fake.SentCommands[0][1].Should().Be(0x41);
        fake.SentCommands[0].Skip(6).Should().Equal(0x02, 0x01, 0x03);
    }

    [Fact]
    public void GetDeviceConfig_uses_android_sdk_support_payload()
    {
        var command = HeyCyanCommands.GetDeviceConfig();

        command[1].Should().Be(HeyCyanCommands.ActionDeviceConfig);
        command.Skip(6).Should().Equal(0x01, 0x00);
    }

    [Fact]
    public void ButtonPressed_forwards_each_gesture_exactly_once()
    {
        var (s, fake) = Build();
        var got = new List<HeyCyanButtonGesture>();
        s.ButtonPressed += (_, e) => got.Add(e.Gesture);

        fake.RaiseButton(HeyCyanButtonGesture.Tap);
        fake.RaiseButton(HeyCyanButtonGesture.DoubleTap);
        fake.RaiseButton(HeyCyanButtonGesture.LongPress);

        got.Should().Equal(
            HeyCyanButtonGesture.Tap,
            HeyCyanButtonGesture.DoubleTap,
            HeyCyanButtonGesture.LongPress);
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        var (s, fake) = Build();

        await s.DisposeAsync();
        await s.DisposeAsync(); // must not throw

        fake.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisconnectAsync_when_already_disconnected_is_noop()
    {
        var (s, fake) = Build();

        await s.DisconnectAsync(default);

        s.State.Should().Be(HeyCyanState.Disconnected);
    }

    [Fact]
    public async Task EnterTransferModeAsync_catches_transfer_ip_raised_during_send()
    {
        var (s, fake) = Build();
        fake.ConnectGate.SetResult();
        await s.ConnectAsync(new("M01 Pro", "AA:BB", -50), default);
        fake.SentCommands.Clear();
        fake.OnSend = _ =>
        {
            fake.RaiseRawNotify([0, 0, 0, 0, 0, 0, 0x08, 192, 168, 49, 183]);
            return new HeyCyanResponse(0x41, []);
        };

        var transfer = await s.EnterTransferModeAsync(CancellationToken.None);

        fake.SentCommands.Should().HaveCount(2);
        fake.SentCommands[0][1].Should().Be(0x41);
        fake.SentCommands[0].Skip(6).Should().Equal(0x02, 0x01, 0x04);
        fake.SentCommands[1][1].Should().Be(0x47);
        fake.SentCommands[1].Skip(6).Should().Equal(0x01, 0x00);
        transfer.BaseUrl.Should().Be("http://192.168.49.183/");
        s.State.Should().Be(HeyCyanState.TransferMode);

        await s.ExitTransferModeAsync(CancellationToken.None);
    }
}
