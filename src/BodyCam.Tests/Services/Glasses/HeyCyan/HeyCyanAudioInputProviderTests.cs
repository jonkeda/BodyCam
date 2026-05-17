using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// Unit tests for <see cref="HeyCyanAudioInputProvider"/>.
/// </summary>
public sealed class HeyCyanAudioInputProviderTests : IAsyncDisposable
{
    private const string Mac = "AA:BB:CC:DD:EE:FF";

    [Fact]
    public void IsAvailable_RequiresConnectedAndMatchingMac()
    {
        var session = new FakeHeyCyanSession();
        var bt      = new FakeBluetoothAudioInputProvider(new[] { Mac });
        var sut     = new HeyCyanAudioInputProvider(session, bt, NullLogger<HeyCyanAudioInputProvider>.Instance);

        sut.IsAvailable.Should().BeFalse("session is disconnected");

        session.RaiseConnected(Mac);
        sut.IsAvailable.Should().BeTrue("session is connected and MAC matches");

        session.RaiseConnected("11:22:33:44:55:66");
        sut.IsAvailable.Should().BeFalse("session is connected but MAC does not match");
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

        await sut.Awaiting(s => s.StartAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No BT capture endpoint matching glasses MAC*");
    }

    [Fact]
    public async Task StateChanged_ToDisconnected_StopsCapture()
    {
        var session = new FakeHeyCyanSession();
        var bt      = new FakeBluetoothAudioInputProvider(new[] { Mac });
        var sut     = new HeyCyanAudioInputProvider(session, bt, NullLogger<HeyCyanAudioInputProvider>.Instance);
        session.RaiseConnected(Mac);
        await sut.StartAsync();

        session.RaiseDisconnected();
        await Task.Delay(10); // Let async-void handler run

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

        var chunk = new byte[] { 1, 2, 3, 4 };
        bt.RaiseChunk(chunk);

        received.Should().BeSameAs(chunk, "chunk should be forwarded verbatim");
    }

    [Fact]
    public async Task BtDisconnected_RaisesDisconnectedEvent()
    {
        var session = new FakeHeyCyanSession();
        var bt      = new FakeBluetoothAudioInputProvider(new[] { Mac });
        var sut     = new HeyCyanAudioInputProvider(session, bt, NullLogger<HeyCyanAudioInputProvider>.Instance);

        var disconnectRaised = false;
        sut.Disconnected += (_, _) => disconnectRaised = true;

        bt.RaiseDisconnected();
        await Task.Delay(5);

        disconnectRaised.Should().BeTrue();
    }

    public async ValueTask DisposeAsync()
    {
        // Clean up any test resources if needed
        await Task.CompletedTask;
    }
}
