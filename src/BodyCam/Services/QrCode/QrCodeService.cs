using BodyCam.Models;

namespace BodyCam.Services.QrCode;

public class QrCodeService
{
    private readonly List<QrScanResult> _history = [];
    private readonly object _lock = new();
    private const int MaxHistory = 20;

    public QrScanResult? LastResult
    {
        get
        {
            lock (_lock)
                return _history.Count > 0 ? _history[^1] : null;
        }
    }

    public void Add(QrScanResult result)
    {
        lock (_lock)
        {
            _history.Add(result);
            if (_history.Count > MaxHistory)
                _history.RemoveAt(0);
        }
    }

    public IReadOnlyList<QrScanResult> SearchHistory(string query)
    {
        lock (_lock)
        {
            return _history
                .Where(r => r.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public IReadOnlyList<QrScanResult> GetHistory()
    {
        lock (_lock)
            return _history.ToList();
    }
}
