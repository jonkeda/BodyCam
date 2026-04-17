using BodyCam.Services;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class MemoryStoreTests
{
    [Fact]
    public async Task SaveAsync_AddsEntry()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new MemoryStore(tempFile);
            await store.SaveAsync(new MemoryEntry { Content = "Test note" });

            store.Count.Should().Be(1);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SearchAsync_FindsMatching()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new MemoryStore(tempFile);
            await store.SaveAsync(new MemoryEntry { Content = "my red car" });
            await store.SaveAsync(new MemoryEntry { Content = "blue hat" });

            var results = await store.SearchAsync("car");

            results.Should().HaveCount(1);
            results[0].Content.Should().Be("my red car");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new MemoryStore(tempFile);
            await store.SaveAsync(new MemoryEntry { Content = "my red car" });

            var results = await store.SearchAsync("airplane");

            results.Should().BeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsLatest()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new MemoryStore(tempFile);
            await store.SaveAsync(new MemoryEntry { Content = "first", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-3) });
            await store.SaveAsync(new MemoryEntry { Content = "second", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2) });
            await store.SaveAsync(new MemoryEntry { Content = "third", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1) });

            var results = await store.GetRecentAsync(2);

            results.Should().HaveCount(2);
            results[0].Content.Should().Be("third");
            results[1].Content.Should().Be("second");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PersistAndReload_RoundTrip()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store1 = new MemoryStore(tempFile);
            await store1.SaveAsync(new MemoryEntry { Content = "persisted note", Category = "test" });

            // Create a new store pointing at the same file
            var store2 = new MemoryStore(tempFile);
            var results = await store2.SearchAsync("persisted");

            results.Should().HaveCount(1);
            results[0].Content.Should().Be("persisted note");
            results[0].Category.Should().Be("test");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
