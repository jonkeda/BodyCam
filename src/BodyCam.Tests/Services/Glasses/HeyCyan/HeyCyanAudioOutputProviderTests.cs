using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// Unit tests for <see cref="HeyCyanAudioOutputProvider"/>.
/// </summary>
public sealed class HeyCyanAudioOutputProviderTests : IAsyncDisposable
{
    private const string Mac = "AA:BB:CC:DD:EE:FF";

    [Fact]
    public void IsAvailable_RequiresConnectedAndMatchingMac()
    {
        var session = new FakeHeyCyanSession();
        var bt      = new FakeBluetoothAudioOutputProvider(new[] { Mac });
        var sut     = new HeyCyanAudioOutputProvider(session, bt, NullLogger<HeyCyanAudioOutputProvider>.Instance);

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
        var bt      = new FakeBluetoothAudioOutputProvider(new[] { Mac });
        var sut     = new HeyCyanAudioOutputProvider(session, bt, NullLogger<HeyCyanAudioOutputProvider>.Instance);
        session.RaiseConnected(Mac);

        await sut.StartAsync(24000);

        bt.SelectedMac.Should().Be(Mac);
        bt.StartCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenNoMatchingEndpoint()
    {
        var session = new FakeHeyCyanSession();
        var bt      = new FakeBluetoothAudioOutputProvider(Array.Empty<string>());
        var sut     = new HeyCyanAudioOutputProvider(session, bt, NullLogger<HeyCyanAudioOutputProvider>.Instance);
        session.RaiseConnected(Mac);

        await sut.Awaiting(s => s.StartAsync(24000))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No BT render endpoint matching glasses MAC*");
    }

    [Fact]
    public async Task StateChanged_ToDisconnected_StopsPlayback()
    {
        var session = new FakeHeyCyanSession();
        var bt      = new FakeBluetoothAudioOutputProvider(new[] { Mac });
        var sut     = new HeyCyanAudioOutputProvider(session, bt, NullLogger<HeyCyanAudioOutputProvider>.Instance);
        session.RaiseConnected(Mac);
        await sut.StartAsync(24000);

        session.RaiseDisconnected();
        await Task.Delay(10); // Let async-void handler run

        bt.StopCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PlayChunkAsync_ForwardsToInner()
    {
        var session = new FakeHeyCyanSession();
        var bt      = new FakeBluetoothAudioOutputProvider(new[] { Mac });
        var sut     = new HeyCyanAudioOutputProvider(session, bt, NullLogger<HeyCyanAudioOutputProvider>.Instance);
        session.RaiseConnected(Mac);
        await sut.StartAsync(24000);

        var chunk = new byte[] { 10, 20, 30, 40 };
        await sut.PlayChunkAsync(chunk);

        bt.PlayedChunks.Should().ContainSingle()
            .Which.Should().BeSameAs(chunk, "chunk should be forwarded verbatim");
    }

    [Fact]
    public void ClearBuffer_ForwardsToInner()
    {
        var session = new FakeHeyCyanSession();
        var bt      = new FakeBluetoothAudioOutputProvider(new[] { Mac });
        var sut     = new HeyCyanAudioOutputProvider(session, bt, NullLogger<HeyCyanAudioOutputProvider>.Instance);

        bt.PlayedChunks.Should().BeEmpty();

        // Add a chunk, then clear
        _ = bt.PlayChunkAsync(new byte[] { 1, 2 });
        bt.PlayedChunks.Should().HaveCount(1);

        sut.ClearBuffer();
        bt.PlayedChunks.Should().BeEmpty();
    }

    [Fact]
    public async Task BtDisconnected_RaisesDisconnectedEvent()
    {
        var session = new FakeHeyCyanSession();
        var bt      = new FakeBluetoothAudioOutputProvider(new[] { Mac });
        var sut     = new HeyCyanAudioOutputProvider(session, bt, NullLogger<HeyCyanAudioOutputProvider>.Instance);

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
