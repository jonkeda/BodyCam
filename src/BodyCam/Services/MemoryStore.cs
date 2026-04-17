using System.Text.Json;

namespace BodyCam.Services;

public class MemoryStore
{
    private readonly string _filePath;
    private List<MemoryEntry> _entries = new();
    private bool _loaded;

    public MemoryStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task SaveAsync(MemoryEntry entry)
    {
        await EnsureLoadedAsync();
        _entries.Add(entry);
        await PersistAsync();
    }

    public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(string query)
    {
        await EnsureLoadedAsync();
        if (string.IsNullOrWhiteSpace(query))
            return _entries.OrderByDescending(e => e.Timestamp).Take(10).ToList();

        var lower = query.ToLowerInvariant();
        return _entries
            .Where(e => e.Content.Contains(lower, StringComparison.OrdinalIgnoreCase)
                     || (e.Category?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .ToList();
    }

    public async Task<IReadOnlyList<MemoryEntry>> GetRecentAsync(int count = 10)
    {
        await EnsureLoadedAsync();
        return _entries.OrderByDescending(e => e.Timestamp).Take(count).ToList();
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        if (File.Exists(_filePath))
        {
            var json = await File.ReadAllTextAsync(_filePath);
            if (!string.IsNullOrWhiteSpace(json))
                _entries = JsonSerializer.Deserialize<List<MemoryEntry>>(json) ?? new();
        }
        _loaded = true;
    }

    private async Task PersistAsync()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json);
    }

    // For testing
    internal int Count => _entries.Count;
}

public record MemoryEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Content { get; init; } = "";
    public string? Category { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
