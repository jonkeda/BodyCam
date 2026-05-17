using BodyCam.Services.Audio;
using BodyCam.Services.Audio.WebRtcApm;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BodyCam.Tests.Services.Audio;

/// <summary>
/// Phase 6.1: Tests for AEC statistics tracking and event emission.
/// </summary>
public class AecProcessorStatisticsTests
{
    [Fact]
    public void GetStatistics_WhenNotInitialized_ReturnsNull()
    {
        // Arrange
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);

        // Act
        var stats = aec.GetStatistics();

        // Assert
        stats.Should().BeNull();
    }

    [Fact]
    public void GetStatistics_WhenDisposed_ReturnsNull()
    {
        // Arrange
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);
        aec.Dispose();

        // Act
        var stats = aec.GetStatistics();

        // Assert
        stats.Should().BeNull();
    }

    [Fact]
    public void GetStatistics_WhenInitialized_ReturnsStatisticsOrNull()
    {
        // Arrange
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);

        try
        {
            // Act
            var stats = aec.GetStatistics();

            // Assert
            // Stats may be null if the native library doesn't export the symbol,
            // or valid if it does. We accept both outcomes.
            if (stats.HasValue)
            {
                // If we got stats, they should have reasonable bounds
                stats.Value.EchoReturnLossDb.Should().BeInRange(-100f, 100f);
                stats.Value.EchoReturnLossEnhancementDb.Should().BeInRange(-100f, 100f);
                stats.Value.DelayMs.Should().BeInRange(0, 1000);
                stats.Value.ResidualEchoLikelihood.Should().BeInRange(0f, 1f);
                stats.Value.DivergentFilterFraction.Should().BeInRange(0f, 1f);
            }
        }
        finally
        {
            aec.Dispose();
        }
    }

    [Fact]
    public async Task StatisticsUpdated_FiresAtApproximately1Hz()
    {
        // Arrange
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        long eventCount = 0;
        aec.StatisticsUpdated += (_, _) => Interlocked.Increment(ref eventCount);

        aec.Initialize(mobileMode: false);

        try
        {
            // Act: Wait 2.5 seconds
            await Task.Delay(2500);

            // Assert: Should have fired 2-3 times (1 Hz timer)
            // Note: If the native library doesn't export GetStatistics, event count will be 0
            var finalCount = Interlocked.Read(ref eventCount);
            if (aec.GetStatistics() is not null)
            {
                // Native lib supports statistics
                finalCount.Should().BeInRange(2, 3);
            }
            else
            {
                // Native lib doesn't export GetStatistics - event never fires
                finalCount.Should().Be(0);
            }
        }
        finally
        {
            aec.Dispose();
        }
    }

    [Fact]
    public void StatisticsUpdated_StopsAfterDispose()
    {
        // Arrange
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        long eventCount = 0;
        aec.StatisticsUpdated += (_, _) => Interlocked.Increment(ref eventCount);

        aec.Initialize(mobileMode: false);
        aec.Dispose();

        // Act: Wait 1.5 seconds after dispose
        Thread.Sleep(1500);

        // Assert: No events should fire after dispose
        var finalCount = Interlocked.Read(ref eventCount);
        finalCount.Should().Be(0, "timer should not fire after dispose");
    }
}
