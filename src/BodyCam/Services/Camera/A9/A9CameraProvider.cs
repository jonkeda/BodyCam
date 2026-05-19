using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Camera.A9;

/// <summary>
/// Camera provider for A9/X5 IP cameras using the iLnkP2P/PPPP protocol.
/// These cameras stream JPEG frames over UDP on port 32108.
///
/// The provider manages connection, reconnection, and frame delivery through
/// the standard <see cref="ICameraProvider"/> interface.
/// </summary>
public sealed class A9CameraProvider : ICameraProvider
{
    private readonly ISettingsService _settings;
    private readonly ILogger<A9CameraProvider> _log;

    private A9Session? _session;
    private CancellationTokenSource? _reconnectCts;
    private TaskCompletionSource<byte[]>? _pendingCapture;
    private byte[]? _latestFrame;
    private bool _started;

    private const int ReconnectDelayMs = 3000;
    private const int MaxReconnectAttempts = 5;

    public string DisplayName => "A9 Camera";
    public string ProviderId => "a9-camera";
    public bool IsAvailable => _session?.IsStreaming == true;

    public event EventHandler? Disconnected;

    public A9CameraProvider(ISettingsService settings, ILogger<A9CameraProvider> log)
    {
        _settings = settings;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started) return;
        _started = true;

        var ip = _settings.A9CameraIp;
        if (string.IsNullOrWhiteSpace(ip))
        {
            _log.LogWarning("A9: No camera IP configured — cannot start");
            _started = false;
            return;
        }

        _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await ConnectWithRetryAsync(_reconnectCts.Token);
    }

    public async Task StopAsync()
    {
        if (!_started) return;
        _started = false;

        if (_reconnectCts is not null)
        {
            await _reconnectCts.CancelAsync();
            _reconnectCts.Dispose();
            _reconnectCts = null;
        }

        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        _latestFrame = null;
    }

    public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        // If we already have a recent frame, return it immediately
        if (_latestFrame is not null)
        {
            var frame = _latestFrame;
            return Task.FromResult<byte[]?>(frame);
        }

        // Otherwise, wait for the next frame with a timeout
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCapture = tcs;

        ct.Register(() => tcs.TrySetCanceled());

        return WaitForFrameAsync(tcs, ct);
    }

    private async Task<byte[]?> WaitForFrameAsync(TaskCompletionSource<byte[]> tcs, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(5000);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            linked.Token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _pendingCapture = null;
        }
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await CaptureFrameAsync(ct);
            if (frame is not null)
                yield return frame;
            else
                await Task.Delay(100, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    /// <summary>
    /// Connect to the camera with retry logic. On disconnect, automatically
    /// attempts to reconnect up to <see cref="MaxReconnectAttempts"/> times
    /// with a <see cref="ReconnectDelayMs"/> delay between attempts.
    /// </summary>
    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested && attempt < MaxReconnectAttempts)
        {
            try
            {
                var ip = _settings.A9CameraIp!;
                var user = _settings.A9CameraUsername ?? "admin";
                var pass = _settings.A9CameraPassword ?? "admin";

                if (_session is not null)
                    await _session.DisposeAsync();

                _session = new A9Session(ip, user, pass, _log);
                _session.FrameReceived += OnFrameReceived;
                _session.Disconnected += OnSessionDisconnected;

                await _session.ConnectAsync(ct);
                attempt = 0; // reset on successful connection
                return;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                attempt++;
                _log.LogWarning(ex, "A9: Connection attempt {Attempt}/{Max} failed",
                    attempt, MaxReconnectAttempts);

                if (attempt < MaxReconnectAttempts && !ct.IsCancellationRequested)
                {
                    try { await Task.Delay(ReconnectDelayMs, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        _log.LogError("A9: Failed to connect after {Max} attempts", MaxReconnectAttempts);
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnFrameReceived(byte[] jpeg)
    {
        _latestFrame = jpeg;

        // Fulfill any pending single-frame capture
        _pendingCapture?.TrySetResult(jpeg);
    }

    private async void OnSessionDisconnected()
    {
        _log.LogWarning("A9: Camera disconnected, attempting reconnect...");

        if (!_started || _reconnectCts is null || _reconnectCts.IsCancellationRequested) return;

        try
        {
            await Task.Delay(ReconnectDelayMs, _reconnectCts.Token);
            await ConnectWithRetryAsync(_reconnectCts.Token);
        }
        catch (OperationCanceledException) { /* stopping */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "A9: Reconnect failed");
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}
