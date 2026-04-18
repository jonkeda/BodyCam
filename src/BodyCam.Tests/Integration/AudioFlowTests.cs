using BodyCam.Tests.TestInfrastructure;
using BodyCam.Tests.TestInfrastructure.Providers;
using FluentAssertions;

namespace BodyCam.Tests.Integration;

public class AudioFlowTests : IAsyncLifetime
{
    private BodyCamTestHost _host = null!;

    public async Task InitializeAsync()
    {
        // Use short PCM data so emission completes quickly
        _host = BodyCamTestHost.Create(services =>
        {
            // Default host already has mic with 1s silence — that's fine
        });
        await _host.InitializeAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task MicToManager_ChunksRoutedThroughInputManager()
    {
        var received = new List<byte[]>();
        _host.AudioInput.AudioChunkAvailable += (_, chunk) => received.Add(chunk);

        await _host.AudioInput.StartAsync();
        await Task.Delay(500);
        await _host.AudioInput.StopAsync();

        received.Should().NotBeEmpty();
        received.Sum(c => c.Length).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ManagerToSpeaker_ChunksPlayedThroughOutputManager()
    {
        await _host.AudioOutput.StartAsync();

        var testData = new byte[] { 10, 20, 30, 40, 50 };
        await _host.AudioOutput.PlayChunkAsync(testData);

        _host.Speaker.ChunkCount.Should().Be(1);
        _host.Speaker.GetCapturedBytes().Should().BeEquivalentTo(testData);
    }

    [Fact]
    public async Task ClearBuffer_ClearsSpeakerQueue()
    {
        await _host.AudioOutput.StartAsync();
        await _host.AudioOutput.PlayChunkAsync(new byte[] { 1, 2, 3 });
        _host.Speaker.ChunkCount.Should().Be(1);

        _host.AudioOutput.ClearBuffer();

        _host.Speaker.ChunkCount.Should().Be(0);
    }

    [Fact]
    public async Task MicRoundTrip_DataFlowsFromMicThroughManagerToConsumer()
    {
        var capturedChunks = new List<byte[]>();
        _host.AudioInput.AudioChunkAvailable += (_, chunk) =>
        {
            // Echo each mic chunk to the speaker (simulating a simple passthrough)
            _host.AudioOutput.PlayChunkAsync(chunk).GetAwaiter().GetResult();
            capturedChunks.Add(chunk);
        };

        await _host.AudioOutput.StartAsync();
        await _host.AudioInput.StartAsync();
        await Task.Delay(500);
        await _host.AudioInput.StopAsync();

        capturedChunks.Should().NotBeEmpty();
        _host.Speaker.ChunkCount.Should().Be(capturedChunks.Count);
    }

    [Fact]
    public async Task StopCapture_StopsChunkFlow()
    {
        var chunkCount = 0;
        _host.AudioInput.AudioChunkAvailable += (_, _) => chunkCount++;

        await _host.AudioInput.StartAsync();
        await Task.Delay(200);
        await _host.AudioInput.StopAsync();
        var countAfterStop = chunkCount;

        await Task.Delay(200);

        chunkCount.Should().Be(countAfterStop);
    }
}
