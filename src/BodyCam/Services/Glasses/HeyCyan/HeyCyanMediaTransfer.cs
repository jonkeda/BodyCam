using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Cross-platform orchestrator for HeyCyan glasses media transfer.
/// Implements warm transfer mode: holds the session open across consecutive
/// captures with an 8s idle timeout, amortizing group-formation cost from ~2-5s
/// down to 700ms-1.5s for back-to-back frames.
/// </summary>
internal sealed class HeyCyanMediaTransfer : IHeyCyanMediaTransfer
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly IHeyCyanHttpClientFactory _httpFactory;
    private readonly TimeSpan _warmIdle;
    private readonly TimeProvider _time;
    private readonly ILogger<HeyCyanMediaTransfer> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IHeyCyanHttpClient? _http;
    private CancellationTokenSource? _idleCts;
    private bool _disposed;

    public bool IsWarm => _http is not null;

    public HeyCyanMediaTransfer(
        IHeyCyanGlassesSession session,
        IHeyCyanHttpClientFactory httpFactory,
        ILogger<HeyCyanMediaTransfer> log,
        TimeProvider? timeProvider = null,
        TimeSpan? warmIdle = null)
    {
        _session = session;
        _httpFactory = httpFactory;
        _log = log;
        _time = timeProvider ?? TimeProvider.System;
        _warmIdle = warmIdle ?? TimeSpan.FromSeconds(8);
    }

    public async Task<IReadOnlyList<HeyCyanMediaEntry>> ListAsync(CancellationToken ct)
    {
        await EnsureTransferModeAsync(ct).ConfigureAwait(false);

        try
        {
            var raw = await _http!.GetStringAsync("/files/media.config", ct).ConfigureAwait(false);
            var entries = MediaConfigParser.Parse(raw);
            
            _log.LogInformation("Listed {Count} media entries from glasses", entries.Count);
            
            ScheduleIdleExit();
            return entries;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to list media files");
            throw;
        }
    }

    public async Task<byte[]> DownloadAsync(string fileName, CancellationToken ct)
    {
        await EnsureTransferModeAsync(ct).ConfigureAwait(false);

        try
        {
            var bytes = await _http!.GetByteArrayAsync($"/files/{fileName}", ct).ConfigureAwait(false);
            
            _log.LogInformation("Downloaded {FileName} ({Size} bytes)", fileName, bytes.Length);
            
            ScheduleIdleExit();
            return bytes;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to download {FileName}", fileName);
            throw;
        }
    }

    public async Task<Stream> OpenAsync(string fileName, CancellationToken ct)
    {
        await EnsureTransferModeAsync(ct).ConfigureAwait(false);

        try
        {
            var stream = await _http!.GetStreamAsync($"/files/{fileName}", ct).ConfigureAwait(false);
            
            _log.LogInformation("Opened stream for {FileName}", fileName);
            
            // Wrap the stream so ScheduleIdleExit is called when the caller disposes it.
            return new IdleExitStream(stream, () => ScheduleIdleExit());
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to open stream for {FileName}", fileName);
            throw;
        }
    }

    public Task ExitAsync(CancellationToken ct) => TeardownAsync(ct);

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return default;

        _disposed = true;
        _ = TeardownAsync(CancellationToken.None);
        _gate.Dispose();
        return default;
    }

    /// <summary>
    /// Ensures transfer mode is active. If already warm, cancels pending idle teardown.
    /// If cold, enters transfer mode (BLE command + wait for IP + bind P2P network).
    /// </summary>
    private async Task EnsureTransferModeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Cancel pending idle teardown (keep session warm).
            _idleCts?.Cancel();

            // Already warm — reuse existing session.
            if (_http is not null)
            {
                _log.LogDebug("Transfer mode already active (warm)");
                return;
            }

            _log.LogInformation("Entering transfer mode (cold start)");

            if (_httpFactory is IHeyCyanTransferPreparation preparation)
                await preparation.PrepareForTransferAsync(ct).ConfigureAwait(false);

            // Enter transfer mode via the session (BLE + wait for IP notify).
            // The session sends:
            //   1) LargeDataHandler.GlassesControl(new byte[] { 0x02, 0x01, 0x04 }, callback)
            //   2) Polls 0x02,0x03 and prefers GlassesDeviceNotifyRsp where LoadData[6] == 0x08
            var transfer = await _session.EnterTransferModeAsync(ct).ConfigureAwait(false);

            // Create the platform-specific HTTP client (Android: WiFiP2pHttpClient with process binding).
            _http = await _httpFactory.CreateAsync(new Uri(transfer.BaseUrl), ct).ConfigureAwait(false);

            _log.LogInformation("Transfer mode active, base URL: {BaseUrl}", transfer.BaseUrl);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Schedules (or reschedules) the idle teardown timer.
    /// After _warmIdle elapses with no activity, tears down the transfer session.
    /// </summary>
    private void ScheduleIdleExit()
    {
        _idleCts?.Cancel();
        var cts = _idleCts = new CancellationTokenSource();

        _ = Task.Delay(_warmIdle, _time, cts.Token).ContinueWith(async t =>
        {
            if (!t.IsCanceled && !_disposed)
            {
                _log.LogInformation("Idle timeout ({Timeout}s) — tearing down transfer mode", _warmIdle.TotalSeconds);
                await TeardownAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Tears down the transfer session (idempotent).
    /// - Disposes the HTTP client (which unbinds the process on Android).
    /// - Sends BLE exit transfer mode command (0x02, 0x01, 0x09) via the session.
    /// - Sets _http = null so the next operation re-enters transfer mode.
    /// </summary>
    private async Task TeardownAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_http is null)
            {
                _log.LogDebug("Teardown called but transfer mode already inactive");
                return;
            }

            _log.LogInformation("Tearing down transfer mode");

            // Dispose HTTP client first (unbinds process on Android).
            await _http.DisposeAsync().ConfigureAwait(false);
            _http = null;

            // Send BLE exit transfer mode command to glasses.
            try
            {
                await _session.ExitTransferModeAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "ExitTransferModeAsync failed during teardown");
            }

            // Cancel idle timer if still pending.
            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _idleCts = null;

            _log.LogInformation("Transfer mode torn down");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stream wrapper that calls a callback on dispose (to schedule idle exit).
    /// </summary>
    private sealed class IdleExitStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action _onDispose;
        private bool _disposed;

        public IdleExitStream(Stream inner, Action onDispose)
        {
            _inner = inner;
            _onDispose = onDispose;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => await _inner.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                _inner.Dispose();
                _onDispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await _inner.DisposeAsync().ConfigureAwait(false);
                _onDispose();
            }
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
