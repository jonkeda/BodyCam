using BodyCam.Services.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace BodyCam.Tests.Services.Logging;

public class InAppLoggerProviderTests
{
    [Fact]
    public void Logger_WritesToSink()
    {
        var sink = new InAppLogSink();
        using var provider = new InAppLoggerProvider(sink, LogLevel.Debug);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("test message");

        var entries = sink.GetEntries();
        entries.Should().ContainSingle();
        entries[0].Category.Should().Be("TestCategory");
        entries[0].Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public void Logger_RespectsMinimumLevel()
    {
        var sink = new InAppLogSink();
        using var provider = new InAppLoggerProvider(sink, LogLevel.Warning);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogDebug("should be filtered");
        logger.LogInformation("should also be filtered");
        logger.LogWarning("should appear");

        var entries = sink.GetEntries();
        entries.Should().ContainSingle()
            .Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void Logger_IncludesException()
    {
        var sink = new InAppLogSink();
        using var provider = new InAppLoggerProvider(sink, LogLevel.Debug);
        var logger = provider.CreateLogger("Cat");
        var ex = new InvalidOperationException("bad");

        logger.LogError(ex, "something failed");

        var entries = sink.GetEntries();
        entries.Should().ContainSingle();
        entries[0].Exception.Should().BeSameAs(ex);
    }
}
