using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Logging;

/// <summary>
/// Thread-safe ring buffer holding the last N log entries for the in-app debug overlay.
/// </summary>
public class InAppLogSink
{
    private readonly InAppLogEntry[] _buffer;
    private int _head;
    private int _count;
    private readonly object _lock = new();

    public event EventHandler<InAppLogEntry>? EntryAdded;

    public InAppLogSink(int capacity = 500)
    {
        _buffer = new InAppLogEntry[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count { get { lock (_lock) return _count; } }

    public void Add(InAppLogEntry entry)
    {
        lock (_lock)
        {
            _buffer[_head] = entry;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
                _count++;
        }

        EntryAdded?.Invoke(this, entry);
    }

    public IReadOnlyList<InAppLogEntry> GetEntries()
    {
        lock (_lock)
        {
            if (_count == 0) return [];

            var result = new InAppLogEntry[_count];
            int start = _count < _buffer.Length ? 0 : _head;
            for (int i = 0; i < _count; i++)
            {
                result[i] = _buffer[(start + i) % _buffer.Length];
            }
            return result;
        }
    }

    public string GetFormattedLog()
    {
        var entries = GetEntries();
        if (entries.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder(entries.Count * 80);
        foreach (var entry in entries)
        {
            sb.Append('[').Append(entry.Timestamp.ToString("HH:mm:ss")).Append("] ");
            if (entry.Level >= LogLevel.Warning)
                sb.Append(entry.Level.ToString().ToUpperInvariant()).Append(": ");
            sb.AppendLine(entry.Message);
        }
        return sb.ToString();
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
        }
    }
}
