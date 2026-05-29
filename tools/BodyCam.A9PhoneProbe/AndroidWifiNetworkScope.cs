using Android.Content;
using Android.Net;
using Android.Net.Wifi;
using Android.OS;

namespace BodyCam.A9PhoneProbe;

internal sealed class AndroidWifiNetworkScope : IDisposable
{
    private readonly ConnectivityManager? _connectivityManager;
    private readonly WifiManager.MulticastLock? _multicastLock;
    private readonly WifiManager.WifiLock? _wifiLock;
    private readonly Action<string> _line;
    private bool _disposed;

    private AndroidWifiNetworkScope(
        ConnectivityManager? connectivityManager,
        WifiManager.MulticastLock? multicastLock,
        WifiManager.WifiLock? wifiLock,
        Action<string> line)
    {
        _connectivityManager = connectivityManager;
        _multicastLock = multicastLock;
        _wifiLock = wifiLock;
        _line = line;
    }

    public static AndroidWifiNetworkScope Enter(Context context, Action<string> line)
    {
        var scope = new AndroidWifiNetworkScope(null, null, null, line);
        return scope.EnterCore(context);
    }

    private AndroidWifiNetworkScope EnterCore(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        var connectivityManager = appContext.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        var wifiManager = appContext.GetSystemService(Context.WifiService) as WifiManager;

        Network? wifiNetwork = null;
        if (connectivityManager is not null)
        {
#pragma warning disable CA1422
            foreach (var network in connectivityManager.GetAllNetworks())
#pragma warning restore CA1422
            {
                var capabilities = connectivityManager.GetNetworkCapabilities(network);
                if (capabilities is null || !capabilities.HasTransport(TransportType.Wifi))
                    continue;

                wifiNetwork = network;
                break;
            }
        }

        if (connectivityManager is not null && wifiNetwork is not null)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                var bound = connectivityManager.BindProcessToNetwork(wifiNetwork);
                _line($"- Android Wi-Fi bind: network={wifiNetwork} bound={bound}");
            }
            else
            {
#pragma warning disable CA1422
                var bound = ConnectivityManager.SetProcessDefaultNetwork(wifiNetwork);
#pragma warning restore CA1422
                _line($"- Android Wi-Fi bind: network={wifiNetwork} bound={bound}");
            }
        }
        else
        {
            _line("- Android Wi-Fi bind: no active Wi-Fi network object found");
        }

        WifiManager.MulticastLock? multicastLock = null;
        WifiManager.WifiLock? wifiLock = null;
        if (wifiManager is not null)
        {
            try
            {
                multicastLock = wifiManager.CreateMulticastLock("BodyCamA9PpcsMulticast");
                if (multicastLock is not null)
                {
                    multicastLock.SetReferenceCounted(false);
                    multicastLock.Acquire();
                }
                _line("- Android multicast lock: acquired");
            }
            catch (Exception ex)
            {
                _line($"- Android multicast lock: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                wifiLock = wifiManager.CreateWifiLock(WifiMode.FullHighPerf, "BodyCamA9PpcsWifi");
                if (wifiLock is not null)
                {
                    wifiLock.SetReferenceCounted(false);
                    wifiLock.Acquire();
                }
                _line("- Android Wi-Fi lock: acquired");
            }
            catch (Exception ex)
            {
                _line($"- Android Wi-Fi lock: {ex.GetType().Name}: {ex.Message}");
            }
        }
        else
        {
            _line("- Android Wi-Fi locks: WifiManager unavailable");
        }

        return new AndroidWifiNetworkScope(connectivityManager, multicastLock, wifiLock, _line);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (_wifiLock?.IsHeld == true)
                _wifiLock.Release();
        }
        catch (Exception ex)
        {
            _line($"- Android Wi-Fi lock release: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            if (_multicastLock?.IsHeld == true)
                _multicastLock.Release();
        }
        catch (Exception ex)
        {
            _line($"- Android multicast lock release: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            if (_connectivityManager is not null)
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    _connectivityManager.BindProcessToNetwork(null);
                else
                {
#pragma warning disable CA1422
                    ConnectivityManager.SetProcessDefaultNetwork(null);
#pragma warning restore CA1422
                }
            }
        }
        catch (Exception ex)
        {
            _line($"- Android Wi-Fi unbind: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
