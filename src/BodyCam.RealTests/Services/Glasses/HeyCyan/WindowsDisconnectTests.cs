using BodyCam.RealTests.Fixtures;
using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan;

/// <summary>
/// Disconnect/reconnect tests that create their own fixture instance.
/// These are separate from <see cref="WindowsConnectionFlowTests"/> because
/// they modify the shared connection state (disconnect) and GATT rediscovery
/// is unreliable after disconnect with the same session.
/// </summary>
[Trait("Category", "RealDisconnect")]
public sealed class WindowsDisconnectTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private WindowsHeyCyanRealFixture? _fixture;

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    private static string Mac =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC") ?? "";

    public WindowsDisconnectTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!RealEnabled || string.IsNullOrEmpty(Mac)) return;

        _fixture = await WindowsHeyCyanRealFixture.CreateAsync();
        await _fixture.ConnectByAddressAsync(Mac, CancellationToken.None);
        _output.WriteLine($"Connected to {Mac}, state: {_fixture.Session.State}");
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
            await _fixture.DisposeAsync();
    }

    [SkippableFact]
    public async Task Disconnect_StateIsDisconnected()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        _fixture!.Session.State.Should().Be(HeyCyanState.Connected,
            "session should be connected before disconnect test");

        _output.WriteLine("Disconnecting...");
        await _fixture.Session.DisconnectAsync(CancellationToken.None);

        _output.WriteLine($"Session state: {_fixture.Session.State}");
        _fixture.Session.State.Should().Be(HeyCyanState.Disconnected);
    }

    [SkippableFact]
    public async Task Disconnect_MicBecomesUnavailable()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        _fixture!.Session.State.Should().Be(HeyCyanState.Connected,
            "session should be connected before disconnect test");

        await _fixture.Session.DisconnectAsync(CancellationToken.None);
        await Task.Delay(1000);

        _output.WriteLine($"MicProvider.IsAvailable after disconnect: {_fixture.MicProvider.IsAvailable}");
        _fixture.MicProvider.IsAvailable.Should().BeFalse(
            "mic should not be available after disconnect");
    }

    [SkippableFact]
    public async Task Reconnect_AfterDisconnect_Succeeds()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        _fixture!.Session.State.Should().Be(HeyCyanState.Connected,
            "session should be connected before reconnect test");

        _output.WriteLine("Connected — now disconnecting...");
        await _fixture.Session.DisconnectAsync(CancellationToken.None);
        _output.WriteLine("Disconnected");
        await Task.Delay(3000);

        _output.WriteLine("Reconnecting...");
        await _fixture.ConnectByAddressAsync(Mac, CancellationToken.None);
        _output.WriteLine("Reconnected");

        _fixture.Session.State.Should().Be(HeyCyanState.Connected);
        _fixture.DeviceManager.Version.Should().NotBeNull();
    }
}
