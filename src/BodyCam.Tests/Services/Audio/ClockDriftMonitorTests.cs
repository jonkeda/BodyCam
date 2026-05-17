using BodyCam.Services.Audio;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BodyCam.Tests.Services.Audio;

/// <summary>
/// Phase 6.2: Tests for clock drift monitoring between capture and render paths.
/// </summary>
public class ClockDriftMonitorTests
{
    [Fact]
    public void NoDrift_NoAlarmRaised()
    {
        // Arrange
        var monitor = new ClockDriftMonitor(NullLogger<ClockDriftMonitor>.Instance, thresholdPercent: 0.5);
        var alarmRaised = false;
        monitor.DriftAlarmRaised += (_, _) => alarmRaised = true;

        // Simulate 60 seconds of balanced audio (48kHz * 60s = 2,880,000 samples)
        monitor.RecordCaptureSamples(2_880_000);
        monitor.RecordRenderSamples(2_880_000);

        // Act: Tick immediately (force evaluation)
        SimulateElapsed60Seconds(monitor);
        monitor.Tick();

        // Assert
        alarmRaised.Should().BeFalse("balanced capture/render should not trigger alarm");
    }

    [Fact]
    public void DriftAboveThreshold_AlarmRaised()
    {
        // Arrange
        var monitor = new ClockDriftMonitor(NullLogger<ClockDriftMonitor>.Instance, thresholdPercent: 0.5);
        ClockDriftAlarm? alarm = null;
        monitor.DriftAlarmRaised += (_, e) => alarm = e;

        // Simulate 1% drift over 60 seconds (capture 2,880,000, render 2,851,200 = -1%)
        monitor.RecordCaptureSamples(2_880_000);
        monitor.RecordRenderSamples(2_851_200);

        // Act
        SimulateElapsed60Seconds(monitor);
        monitor.Tick();

        // Assert
        alarm.Should().NotBeNull();
        alarm!.Percent.Should().BeGreaterThan(0.5);
        alarm.CaptureSamples.Should().Be(2_880_000);
        alarm.RenderSamples.Should().Be(2_851_200);
    }

    [Fact]
    public void DriftBelowThreshold_NoAlarm()
    {
        // Arrange
        var monitor = new ClockDriftMonitor(NullLogger<ClockDriftMonitor>.Instance, thresholdPercent: 0.5);
        var alarmRaised = false;
        monitor.DriftAlarmRaised += (_, _) => alarmRaised = true;

        // Simulate 0.3% drift (below 0.5% threshold)
        monitor.RecordCaptureSamples(2_880_000);
        monitor.RecordRenderSamples(2_871_360); // 0.3% less

        // Act
        SimulateElapsed60Seconds(monitor);
        monitor.Tick();

        // Assert
        alarmRaised.Should().BeFalse("0.3% drift is below 0.5% threshold");
    }

    [Fact]
    public void Tick_ResetsWindowAfter60Seconds()
    {
        // Arrange
        var monitor = new ClockDriftMonitor(NullLogger<ClockDriftMonitor>.Instance, thresholdPercent: 0.5);
        var alarmCount = 0;
        monitor.DriftAlarmRaised += (_, _) => alarmCount++;

        // Introduce drift in first window
        monitor.RecordCaptureSamples(2_880_000);
        monitor.RecordRenderSamples(2_851_200); // 1% drift

        SimulateElapsed60Seconds(monitor);
        monitor.Tick(); // Should alarm

        // Introduce balanced samples in second window (simulating recovery)
        monitor.RecordCaptureSamples(2_880_000);
        monitor.RecordRenderSamples(2_880_000);

        SimulateElapsed60Seconds(monitor);
        monitor.Tick(); // Should not alarm

        // Assert
        alarmCount.Should().Be(1, "only the first window had drift");
    }

    [Fact]
    public void ZeroSamples_NoAlarm()
    {
        // Arrange
        var monitor = new ClockDriftMonitor(NullLogger<ClockDriftMonitor>.Instance, thresholdPercent: 0.5);
        var alarmRaised = false;
        monitor.DriftAlarmRaised += (_, _) => alarmRaised = true;

        // Act: No samples recorded
        SimulateElapsed60Seconds(monitor);
        monitor.Tick();

        // Assert
        alarmRaised.Should().BeFalse("zero samples should not trigger alarm");
    }

    /// <summary>
    /// Simulate 60 seconds passing by reflecting the internal _windowStart field.
    /// This is a test-only hack to avoid real-time delays.
    /// </summary>
    private static void SimulateElapsed60Seconds(ClockDriftMonitor monitor)
    {
        var field = typeof(ClockDriftMonitor).GetField("_windowStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field is not null)
            field.SetValue(monitor, DateTime.UtcNow.AddSeconds(-61));
    }
}
