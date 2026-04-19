using BodyCam.Services.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace BodyCam.Tests.Services.Logging;

public class InAppLogSinkTests
{
    private static InAppLogEntry Entry(LogLevel level, string category, string message, Exception? ex = null)
        => new(DateTimeOffset.UtcNow, level, category, message, ex);

    [Fact]
    public void Add_AddsEntry()
    {
        var sink = new InAppLogSink();

        sink.Add(Entry(LogLevel.Information, "TestCat", "hello"));

        var entries = sink.GetEntries();
        entries.Should().ContainSingle()
            .Which.Message.Should().Be("hello");
    }

    [Fact]
    public void Add_RaisesEntryAdded()
    {
        var sink = new InAppLogSink();
        InAppLogEntry? received = null;
        sink.EntryAdded += (_, e) => received = e;

        sink.Add(Entry(LogLevel.Warning, "Cat", "warn"));

        received.Should().NotBeNull();
        received!.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void GetEntries_ReturnsOldestFirst()
    {
        var sink = new InAppLogSink();
        sink.Add(Entry(LogLevel.Information, "C", "first"));
        sink.Add(Entry(LogLevel.Information, "C", "second"));

        var entries = sink.GetEntries();
        entries[0].Message.Should().Be("first");
        entries[1].Message.Should().Be("second");
    }

    [Fact]
    public void RingBuffer_WrapsAtCapacity()
    {
        var sink = new InAppLogSink(capacity: 500);
        for (int i = 0; i < 510; i++)
            sink.Add(Entry(LogLevel.Debug, "C", $"msg-{i}"));

        var entries = sink.GetEntries();
        entries.Should().HaveCount(500);
        // oldest remaining should be msg-10
        entries[0].Message.Should().Be("msg-10");
        // newest should be msg-509
        entries[^1].Message.Should().Be("msg-509");
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var sink = new InAppLogSink();
        sink.Add(Entry(LogLevel.Information, "C", "msg"));

        sink.Clear();

        sink.GetEntries().Should().BeEmpty();
    }

    [Fact]
    public void GetFormattedLog_ReturnsFormattedString()
    {
        var sink = new InAppLogSink();
        sink.Add(Entry(LogLevel.Error, "MyService", "boom"));

        var log = sink.GetFormattedLog();
        log.Should().Contain("ERROR");
        log.Should().Contain("boom");
    }

    [Fact]
    public void ThreadSafety_ConcurrentWrites()
    {
        var sink = new InAppLogSink(capacity: 500);
        var tasks = Enumerable.Range(0, 10).Select(t =>
            Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                    sink.Add(Entry(LogLevel.Debug, "Thread", $"t{t}-{i}"));
            }));

        Task.WhenAll(tasks).GetAwaiter().GetResult();

        // 1000 writes, but ring buffer caps at 500
        sink.GetEntries().Should().HaveCount(500);
    }
}
