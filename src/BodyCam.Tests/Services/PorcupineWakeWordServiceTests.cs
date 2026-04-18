using BodyCam.Services;
using BodyCam.Services.WakeWord;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services;

public class PorcupineWakeWordServiceTests
{
    private readonly IAudioInputService _audioInput = Substitute.For<IAudioInputService>();

    private PorcupineWakeWordService CreateService() => new(_audioInput);

    [Fact]
    public void IsListening_InitiallyFalse()
    {
        var service = CreateService();
        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public void RegisterKeywords_StoresEntries()
    {
        var service = CreateService();
        var entries = new[]
        {
            new WakeWordEntry
            {
                KeywordPath = "test.ppn",
                Label = "test",
                Sensitivity = 0.5f,
                Action = WakeWordAction.StartSession
            }
        };

        service.RegisterKeywords(entries);

        // No exception, entries stored — verified by StartAsync using them
        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_WithNoEntries_DoesNothing()
    {
        var service = CreateService();

        await service.StartAsync();

        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_WithoutAccessKey_ThrowsInvalidOperation()
    {
        // Ensure no env var is set for this test
        var originalKey = Environment.GetEnvironmentVariable("PICOVOICE_ACCESS_KEY");
        try
        {
            Environment.SetEnvironmentVariable("PICOVOICE_ACCESS_KEY", null);

            var service = CreateService();
            service.RegisterKeywords([
                new WakeWordEntry
                {
                    KeywordPath = "test.ppn",
                    Label = "test",
                    Sensitivity = 0.5f,
                    Action = WakeWordAction.StartSession
                }
            ]);

            var act = () => service.StartAsync();

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*AccessKey*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PICOVOICE_ACCESS_KEY", originalKey);
        }
    }

    [Fact]
    public async Task StopAsync_WhenNotListening_DoesNotThrow()
    {
        var service = CreateService();

        await service.StopAsync();

        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public void Dispose_WhenNotListening_DoesNotThrow()
    {
        var service = CreateService();
        service.Dispose();
    }
}
