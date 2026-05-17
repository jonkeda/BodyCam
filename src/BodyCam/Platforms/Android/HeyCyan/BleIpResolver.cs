#if ANDROID
using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Extensions.Logging;
using System.Net;

namespace BodyCam.Platforms.Android.HeyCyan;

/// <summary>
/// Resolves the glasses IP address from BLE notify frames where LoadData[6] == 0x08.
/// Tolerates noisy 0x09 0xFF P2P state transitions during group formation.
/// All callbacks arrive on the BLE I/O HandlerThread; marshals IP delivery to the
/// captured SynchronizationContext.
/// </summary>
internal sealed class BleIpResolver : IDisposable
{
    private readonly SynchronizationContext _dispatcher;
    private readonly ILogger _log;
    private readonly TaskCompletionSource<IPAddress> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _timeoutCts;
    private bool _disposed;

    public BleIpResolver(TimeSpan timeout, ILogger log)
    {
        _dispatcher = SynchronizationContext.Current
            ?? throw new InvalidOperationException("BleIpResolver must be constructed on a SynchronizationContext");
        _log = log;
        _timeoutCts = new CancellationTokenSource(timeout);
        _timeoutCts.Token.Register(() =>
            _tcs.TrySetException(new TimeoutException($"BLE IP notify not received within {timeout}")));
    }

    /// <summary>
    /// Feed incoming GlassesDeviceNotifyRsp frames here.
    /// Called on the BLE I/O HandlerThread by LargeDataHandler.AddOutDeviceListener.
    /// </summary>
    public void OnRawNotify(byte[] frame)
    {
        if (_disposed || _tcs.Task.IsCompleted) return;

        // Parse transfer IP (LoadData[6] == 0x08).
        if (HeyCyanFrameParser.TryParseTransferIp(frame, out var ip) && ip is not null)
        {
            _log.LogInformation("BLE IP notify: {Ip}", ip);
            _dispatcher.Post(_ => _tcs.TrySetResult(ip), null);
            return;
        }

        // Classify P2P error (LoadData[6] == 0x09).
        var errorKind = HeyCyanFrameParser.ClassifyP2pError(frame);
        if (errorKind == HeyCyanP2pErrorKind.Noisy)
        {
            // 0x09 0xFF is transient noise during group formation — log and ignore.
            _log.LogInformation("BLE P2P transient noise (0x09 0xFF) — continuing to wait for IP");
        }
        else if (errorKind == HeyCyanP2pErrorKind.Fatal)
        {
            var code = frame.Length >= 8 ? frame[7] : (byte)0;
            _log.LogError("BLE P2P error: code 0x{Code:X2}", code);
            var ex = new InvalidOperationException($"P2P group formation failed, error code 0x{code:X2}");
            _dispatcher.Post(_ => _tcs.TrySetException(ex), null);
        }
    }

    public Task<IPAddress> WaitForIpAsync() => _tcs.Task;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timeoutCts?.Dispose();
        _tcs.TrySetCanceled();
    }
}
#endif
