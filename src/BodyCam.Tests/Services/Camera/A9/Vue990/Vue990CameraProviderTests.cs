using BodyCam.Services.Camera.A9.Vue990;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public sealed class Vue990CameraProviderTests
{
    [Fact]
    public async Task StartAsync_WithConfiguredHost_MakesProviderAvailable()
    {
        var provider = CreateProvider(new FakeSettingsService { Vue990CameraIp = "192.168.168.1" });

        await provider.StartAsync();

        provider.ProviderId.Should().Be(Vue990CameraProvider.Id);
        provider.DisplayName.Should().Be("Vue990 Camera");
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task CaptureFrameAsync_WithoutConfiguredHost_ReturnsNull()
    {
        var captureClient = Substitute.For<IA9Vue990DirectCaptureClient>();
        var provider = CreateProvider(new FakeSettingsService(), captureClient);

        var frame = await provider.CaptureFrameAsync();

        frame.Should().BeNull();
        await captureClient.DidNotReceiveWithAnyArgs()
            .CaptureAsync(default!, default, default);
    }

    [Fact]
    public async Task CaptureFrameAsync_WhenDirectCaptureSavesStill_ReturnsJpegBytes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"vue990-provider-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var jpeg = new byte[] { 0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0xff, 0xd9 };
            var stillPath = Path.Combine(tempDir, "managed-direct-still.jpg");
            await File.WriteAllBytesAsync(stillPath, jpeg);

            A9Vue990DirectCaptureOptions? options = null;
            var result = new A9Vue990DirectCaptureResult
            {
                Timestamp = DateTimeOffset.Now,
                Host = "192.168.168.1",
                OutputDirectory = tempDir,
                Success = true,
                CapturedImage = true,
                CapturedVideo = false,
            };
            result.Artifacts.Add(new A9Vue990DirectCaptureArtifact
            {
                LocalPath = stillPath,
                Bytes = jpeg.Length,
                Sha256 = "test",
            });

            var captureClient = Substitute.For<IA9Vue990DirectCaptureClient>();
            captureClient
                .CaptureAsync(
                    Arg.Any<A9Vue990DirectCaptureOptions>(),
                    Arg.Any<Action<string>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    options = call.ArgAt<A9Vue990DirectCaptureOptions>(0);
                    return Task.FromResult(result);
                });

            var provider = CreateProvider(
                new FakeSettingsService { Vue990CameraIp = " 192.168.168.1 " },
                captureClient);

            var frame = await provider.CaptureFrameAsync();

            frame.Should().Equal(jpeg);
            options.Should().NotBeNull();
            options!.Host.Should().Be("192.168.168.1");
            options.CaptureImage.Should().BeTrue();
            options.CaptureVideo.Should().BeFalse();
            options.MaxFrames.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureFrameAsync_WhenDirectCaptureFails_ReturnsNull()
    {
        var captureClient = Substitute.For<IA9Vue990DirectCaptureClient>();
        captureClient
            .CaptureAsync(
                Arg.Any<A9Vue990DirectCaptureOptions>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new A9Vue990DirectCaptureResult
            {
                Timestamp = DateTimeOffset.Now,
                Host = "192.168.168.1",
                OutputDirectory = Path.GetTempPath(),
                Success = false,
                Error = "No JPEG frames",
            });

        var provider = CreateProvider(
            new FakeSettingsService { Vue990CameraIp = "192.168.168.1" },
            captureClient);

        var frame = await provider.CaptureFrameAsync();

        frame.Should().BeNull();
    }

    private static Vue990CameraProvider CreateProvider(
        FakeSettingsService settings,
        IA9Vue990DirectCaptureClient? captureClient = null)
    {
        return new Vue990CameraProvider(
            settings,
            captureClient ?? Substitute.For<IA9Vue990DirectCaptureClient>(),
            NullLogger<Vue990CameraProvider>.Instance);
    }
}
