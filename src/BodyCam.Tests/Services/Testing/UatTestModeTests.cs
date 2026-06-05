using BodyCam.Services.Input;
using BodyCam.Services.Testing;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace BodyCam.Tests.Services.Testing;

public sealed class UatTestModeTests
{
    [Fact]
    public async Task UatCameraProvider_ReturnsConfiguredFramesInOrder()
    {
        var previousAssets = Environment.GetEnvironmentVariable(UatTestMode.AssetsVariable);
        var root = Path.Combine(Path.GetTempPath(), $"bodycam-uat-{Guid.NewGuid():N}");
        try
        {
            var cameraDirectory = Path.Combine(root, "camera");
            Directory.CreateDirectory(cameraDirectory);
            File.WriteAllBytes(Path.Combine(cameraDirectory, "001.jpg"), [0xFF, 0xD8, 0x01]);
            File.WriteAllBytes(Path.Combine(cameraDirectory, "002.jpeg"), [0xFF, 0xD8, 0x02]);
            Environment.SetEnvironmentVariable(UatTestMode.AssetsVariable, root);

            var provider = new UatCameraProvider();
            await provider.StartAsync();

            (await provider.CaptureFrameAsync()).Should().Equal([0xFF, 0xD8, 0x01]);
            (await provider.CaptureFrameAsync()).Should().Equal([0xFF, 0xD8, 0x02]);
            (await provider.CaptureFrameAsync()).Should().Equal([0xFF, 0xD8, 0x01]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UatTestMode.AssetsVariable, previousAssets);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UatSilentAudioInputProvider_EmitsOnlyInjectedAudio()
    {
        var provider = new UatSilentAudioInputProvider();
        var chunks = new List<byte[]>();
        provider.AudioChunkAvailable += (_, chunk) => chunks.Add(chunk);

        await provider.StartAsync();

        provider.IsCapturing.Should().BeTrue();
        chunks.Should().BeEmpty();

        provider.InjectPcm([1, 2, 3]);

        chunks.Should().ContainSingle()
            .Which.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public async Task UatCapturingAudioOutputProvider_CapturesChunksAndCounters()
    {
        var provider = new UatCapturingAudioOutputProvider();
        await provider.StartAsync(sampleRate: 24000);

        await provider.PlayChunkAsync([1, 2]);
        await provider.PlayChunkAsync([3, 4, 5]);

        provider.IsPlaying.Should().BeTrue();
        provider.SampleRate.Should().Be(24000);
        provider.ChunkCount.Should().Be(2);
        provider.ByteCount.Should().Be(5);
        provider.CapturedChunks.Select(chunk => chunk.Length).Should().Equal(2, 3);

        provider.ClearBuffer();

        provider.ChunkCount.Should().Be(0);
        provider.ByteCount.Should().Be(0);
        provider.CapturedChunks.Should().BeEmpty();
    }

    [Fact]
    public async Task UatButtonInputProvider_SimulatesGestures()
    {
        var provider = new UatButtonInputProvider();
        var gestures = new List<ButtonGestureEvent>();
        provider.PreRecognizedGesture += (_, gesture) => gestures.Add(gesture);

        await provider.StartAsync();
        provider.SimulateGesture(ButtonGesture.DoubleTap, "secondary");

        provider.IsActive.Should().BeTrue();
        gestures.Should().ContainSingle();
        gestures[0].ProviderId.Should().Be(UatButtonInputProvider.ProviderIdConst);
        gestures[0].ButtonId.Should().Be("secondary");
        gestures[0].Gesture.Should().Be(ButtonGesture.DoubleTap);
    }

    [Fact]
    public async Task UatChatClient_ReturnsScriptedVisionText()
    {
        using var client = new UatChatClient();
        var response = await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.User,
            [
                new DataContent(new byte[] { 0xFF, 0xD8 }, "image/jpeg"),
                new TextContent("What objects can you find?")
            ])
        ]);

        response.Text.Should().Contain("UAT frame contains");
    }
}
