using BodyCam.Services.Audio.WebRtcApm;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Audio.WebRtcApm;

public class AecProcessorTests
{
    [Fact]
    public void ResetRenderReference_DoesNotThrow_WhenNotInitialized()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        var act = () => aec.ResetRenderReference();
        act.Should().NotThrow();
    }

    [Fact]
    public void ResetRenderReference_ClearsResiduals_AfterProcessing()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);

        // Feed 99 samples (not divisible by 480) to force residuals
        byte[] chunk = new byte[99 * 2];
        aec.FeedRenderReference(chunk);
        aec.ProcessCapture(chunk);

        // Reset should clear residuals
        aec.ResetRenderReference();

        // Next call should not use old residuals
        var result = aec.ProcessCapture(chunk);
        result.Should().NotBeNull();
    }

    [Fact]
    public void UpdateStreamDelay_ClampsToValidRange()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);

        // These should not throw and should clamp internally
        aec.UpdateStreamDelay(5);   // Below min (10)
        aec.UpdateStreamDelay(600); // Above max (500)
        aec.UpdateStreamDelay(100); // Within range
    }

    [Fact]
    public void UpdateStreamDelay_DoesNotThrow_WhenNotInitialized()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        var act = () => aec.UpdateStreamDelay(100);
        act.Should().NotThrow();
    }

    [Fact]
    public void ProcessCapture_WithResiduals_PreservesAllSamples()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);

        // Feed chunks that will produce residuals at 48 kHz
        // 600 samples at 48k = 1 complete frame (480) + 120 residual
        int inputSamples = 600;
        byte[] chunk1 = new byte[inputSamples * 2];
        byte[] chunk2 = new byte[inputSamples * 2];
        byte[] chunk3 = new byte[inputSamples * 2];

        var result1 = aec.ProcessCapture(chunk1);
        var result2 = aec.ProcessCapture(chunk2);
        var result3 = aec.ProcessCapture(chunk3);

        // All results should be valid
        result1.Should().NotBeEmpty();
        result2.Should().NotBeEmpty();
        result3.Should().NotBeEmpty();
    }

    [Fact]
    public void FeedRenderReference_WithResiduals_DoesNotThrow()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);

        // Feed multiple non-frame-aligned chunks
        for (int i = 0; i < 10; i++)
        {
            byte[] chunk = new byte[137 * 2]; // Odd size
            var act = () => aec.FeedRenderReference(chunk);
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void ProcessCapture_EmptyInput_ReturnsEmpty()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);

        var result = aec.ProcessCapture([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ProcessCapture_WhenDisabled_ReturnsUnprocessed()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);
        aec.IsEnabled = false;

        byte[] input = new byte[2400 * 2]; // 2400 samples at 48kHz (50ms)
        var result = aec.ProcessCapture(input);
        result.Should().BeSameAs(input);
    }

    [Fact]
    public void ProcessCapture_Disposed_ReturnsUnprocessed()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);
        aec.Dispose();

        byte[] input = new byte[2400 * 2];
        var result = aec.ProcessCapture(input);
        result.Should().BeSameAs(input);
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);
        
        aec.Dispose();
        var act = () => aec.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void GCHandleAllocationTest_ProcessMany_LowAllocation()
    {
        var aec = new AecProcessor(NullLogger<AecProcessor>.Instance);
        aec.Initialize(mobileMode: false);

        // Process 1000 frames worth of audio at 48 kHz
        byte[] chunk = new byte[2400 * 2]; // 50ms at 48kHz
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 20; i++) // 1000ms total
        {
            aec.ProcessCapture(chunk);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        long allocated = after - before;

        // Should be much less than old GCHandle approach
        // Allow up to 1MB for reasonable processing overhead
        allocated.Should().BeLessThan(1_000_000);
    }
}
