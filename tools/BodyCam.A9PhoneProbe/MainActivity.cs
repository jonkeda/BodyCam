using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Color = Android.Graphics.Color;
using JString = Java.Lang.String;
using Typeface = Android.Graphics.Typeface;

namespace BodyCam.A9PhoneProbe;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private const string LogTag = "A9PhoneProbe";
    private const string ReportFileName = "latest-a9-phone-probe.txt";
    private const string TraceFileName = "a9-phone-probe-trace.txt";
    private const int MaxUiReportChars = 60_000;

    private readonly StringBuilder _report = new();
    private readonly StringBuilder _uiReport = new();

    private EditText _hostInput = null!;
    private Button _runButton = null!;
    private Button _ppcsButton = null!;
    private Button _copyButton = null!;
    private Button _saveButton = null!;
    private TextView _statusText = null!;
    private TextView _reportText = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TraceLine("OnCreate entered.");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            TraceLine($"UnobservedTaskException: {args.Exception}");
            args.SetObserved();
        };

        try
        {
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
            BuildUi();
            SetStatus("Ready. Connect this phone to @MC-0025644, then run the probe.");
            HandleIntent(Intent, "OnCreate");
        }
        catch (Exception ex)
        {
            TraceLine($"OnCreate fatal: {ex}");
            throw;
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        TraceLine("OnNewIntent entered.");
        HandleIntent(intent, "OnNewIntent");
    }

    private void HandleIntent(Intent? intent, string source)
    {
        var autorun = intent?.GetBooleanExtra("autorun", false) == true;
        var ppcsOnly = intent?.GetBooleanExtra("ppcs", false) == true;
        var captureImage = intent?.GetBooleanExtra("capture_image", false) == true;
        var captureVideo = intent?.GetBooleanExtra("capture_video", false) == true;
        var nativeOracle = intent?.GetBooleanExtra("native_oracle", false) == true;
        var nativeOracleSocket = intent?.GetBooleanExtra("native_oracle_socket", false) == true;
        var nativeOracleVariants = intent?.GetBooleanExtra("native_oracle_variants", false) == true;
        var nativeChannelOracle = intent?.GetBooleanExtra("native_channel_oracle", false) == true;
        var managedLiveCgi = intent?.GetBooleanExtra("managed_live_cgi", false) == true;
        var managedLiveCgiMode = intent?.GetStringExtra("managed_live_cgi_mode");
        var managedDirect = intent?.GetBooleanExtra("managed_direct", false) == true;
        var fakeRelay = intent?.GetBooleanExtra("fake_relay", false) == true;
        var host = intent?.GetStringExtra("host");
        var serverOverride = intent?.GetStringExtra("server_override");
        var nativeOracleVariantCase = intent?.GetStringExtra("native_oracle_variant_case");
        TraceLine(
            $"{source}: autorun={autorun}; ppcs={ppcsOnly}; capture_image={captureImage}; " +
            $"capture_video={captureVideo}; native_oracle={nativeOracle}; native_oracle_socket={nativeOracleSocket}; " +
            $"native_oracle_variants={nativeOracleVariants}; native_channel_oracle={nativeChannelOracle}; " +
            $"managed_live_cgi={managedLiveCgi}; managed_live_cgi_mode={managedLiveCgiMode ?? "<none>"}; " +
            $"managed_direct={managedDirect}; " +
            $"fake_relay={fakeRelay}; host={host ?? "<none>"}; " +
            $"server_override_len={serverOverride?.Length ?? 0}; native_oracle_variant_case={nativeOracleVariantCase ?? "<none>"}.");

        if (!string.IsNullOrWhiteSpace(host))
            _hostInput.Text = host;

        if (!autorun)
            return;

        var runTask = nativeOracle
            ? RunNativeOracleFromUiAsync(nativeOracleSocket, nativeOracleVariants, nativeOracleVariantCase)
            : managedDirect ? RunManagedDirectFromUiAsync(captureImage, captureVideo)
            : ppcsOnly ? RunPpcsFromUiAsync(captureImage, captureVideo, serverOverride, fakeRelay, nativeChannelOracle, managedLiveCgi, managedLiveCgiMode) : RunProbeFromUiAsync();
        _ = runTask.ContinueWith(task =>
        {
            if (task.Exception is not null)
                TraceLine($"Autorun task faulted: {task.Exception}");
        });
    }

    private void BuildUi()
    {
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        root.SetPadding(Dp(16), Dp(14), Dp(16), Dp(14));
        root.SetBackgroundColor(Color.Rgb(248, 249, 250));

        var title = new TextView(this)
        {
            Text = "A9 Phone Probe",
            TextSize = 22,
            Typeface = Typeface.DefaultBold,
        };
        title.SetTextColor(Color.Rgb(21, 28, 36));
        root.AddView(title, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));

        _statusText = new TextView(this)
        {
            TextSize = 14,
        };
        _statusText.SetTextColor(Color.Rgb(63, 74, 86));
        var statusLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(6),
            BottomMargin = Dp(12),
        };
        root.AddView(_statusText, statusLayout);

        _hostInput = new EditText(this)
        {
            Text = "192.168.168.1",
            Hint = "Camera host",
            InputType = Android.Text.InputTypes.ClassPhone,
        };
        _hostInput.SetSingleLine(true);
        _hostInput.SetSelectAllOnFocus(true);
        root.AddView(_hostInput, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));

        var buttons = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
        };
        buttons.SetGravity(GravityFlags.CenterVertical);
        buttons.SetPadding(0, Dp(8), 0, Dp(8));

        _runButton = new Button(this) { Text = "Run" };
        _ppcsButton = new Button(this) { Text = "PPCS" };
        _copyButton = new Button(this) { Text = "Copy" };
        _saveButton = new Button(this) { Text = "Save" };

        buttons.AddView(_runButton, WeightedButtonLayout());
        buttons.AddView(_ppcsButton, WeightedButtonLayout());
        buttons.AddView(_copyButton, WeightedButtonLayout());
        buttons.AddView(_saveButton, WeightedButtonLayout());
        root.AddView(buttons, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));

        _reportText = new TextView(this)
        {
            Text = string.Empty,
            TextSize = 12,
            Typeface = Typeface.Monospace,
        };
        _reportText.SetTextColor(Color.Rgb(24, 32, 40));
        _reportText.SetTextIsSelectable(true);

        var scroll = new ScrollView(this)
        {
            FillViewport = true,
        };
        scroll.SetBackgroundColor(Color.White);
        scroll.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        scroll.AddView(_reportText, new ScrollView.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1));

        _runButton.Click += async (_, _) => await RunProbeFromUiAsync();
        _ppcsButton.Click += async (_, _) => await RunPpcsFromUiAsync(captureImage: false, captureVideo: false);
        _copyButton.Click += (_, _) => CopyReport();
        _saveButton.Click += (_, _) => SaveReport();

        SetContentView(root);
    }

    private static LinearLayout.LayoutParams WeightedButtonLayout()
    {
        return new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1)
        {
            LeftMargin = 4,
            RightMargin = 4,
        };
    }

    private async Task RunProbeFromUiAsync()
    {
        try
        {
            TraceLine("RunProbeFromUiAsync entered.");
            HideKeyboard();

            var host = _hostInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                Toast.MakeText(this, "Enter a camera host.", ToastLength.Short)?.Show();
                TraceLine("RunProbeFromUiAsync stopped: empty host.");
                return;
            }

            SetButtonsEnabled(false);
            _report.Clear();
            _uiReport.Clear();
            _reportText.Text = string.Empty;
            PersistReportSnapshot();
            SetStatus("Running probe...");

            await Task.Run(() => RunProbeAsync(host));
            SetStatus("Probe complete.");
            TraceLine("RunProbeFromUiAsync completed.");
        }
        catch (Exception ex)
        {
            Append($"Fatal: {ex.GetType().Name}: {ex.Message}");
            TraceLine($"RunProbeFromUiAsync fatal: {ex}");
            SetStatus("Probe failed.");
        }
        finally
        {
            SetButtonsEnabled(true);
            SaveReport();
        }
    }

    private async Task RunPpcsFromUiAsync(
        bool captureImage = false,
        bool captureVideo = false,
        string? serverOverride = null,
        bool fakeRelay = false,
        bool nativeChannelOracle = false,
        bool managedLiveCgi = false,
        string? managedLiveCgiMode = null)
    {
        try
        {
            TraceLine(
                $"RunPpcsFromUiAsync entered. captureImage={captureImage} captureVideo={captureVideo} " +
                $"serverOverrideLen={serverOverride?.Length ?? 0} fakeRelay={fakeRelay} " +
                $"nativeChannelOracle={nativeChannelOracle} managedLiveCgi={managedLiveCgi} " +
                $"managedLiveCgiMode={managedLiveCgiMode ?? "<none>"}");
            HideKeyboard();

            var host = _hostInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                Toast.MakeText(this, "Enter a camera host.", ToastLength.Short)?.Show();
                TraceLine("RunPpcsFromUiAsync stopped: empty host.");
                return;
            }

            SetButtonsEnabled(false);
            _report.Clear();
            _uiReport.Clear();
            _reportText.Text = string.Empty;
            PersistReportSnapshot();
            SetStatus(nativeChannelOracle
                ? "Running PPCS channel oracle..."
                : captureImage || captureVideo ? "Running PPCS capture..." : "Running PPCS probe...");

            Append(nativeChannelOracle
                ? "A9 Vue990 PPCS Channel Oracle"
                : captureImage || captureVideo ? "A9 Vue990 PPCS Capture Probe" : "A9 Vue990 PPCS Probe");
            Append($"Timestamp: {DateTimeOffset.Now:O}");
            Append($"Host: {host}");
            Append($"Capture image: {captureImage}");
            Append($"Capture video: {captureVideo}");
            Append($"Server override: {!string.IsNullOrWhiteSpace(serverOverride)}");
            Append($"Fake relay: {fakeRelay}");
            Append($"Native channel oracle: {nativeChannelOracle}");
            Append($"Managed live CGI: {managedLiveCgi}");
            Append($"Managed live CGI mode: {managedLiveCgiMode ?? "<none>"}");
            Append("");
            AppendNetworkState();
            Append("");
            await Task.Run(async () => await ProbeVue990PpcsAsync(host, captureImage, captureVideo, serverOverride, fakeRelay, nativeChannelOracle, managedLiveCgi, managedLiveCgiMode));
            SetStatus(nativeChannelOracle
                ? "PPCS channel oracle complete."
                : captureImage || captureVideo ? "PPCS capture complete." : "PPCS probe complete.");
            TraceLine("RunPpcsFromUiAsync completed.");
        }
        catch (Exception ex)
        {
            Append($"Fatal: {ex.GetType().Name}: {ex.Message}");
            TraceLine($"RunPpcsFromUiAsync fatal: {ex}");
            SetStatus("PPCS probe failed.");
        }
        finally
        {
            SetButtonsEnabled(true);
            SaveReport();
        }
    }

    private async Task RunManagedDirectFromUiAsync(bool captureImage, bool captureVideo)
    {
        try
        {
            TraceLine($"RunManagedDirectFromUiAsync entered. captureImage={captureImage} captureVideo={captureVideo}");
            HideKeyboard();

            var host = _hostInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                Toast.MakeText(this, "Enter a camera host.", ToastLength.Short)?.Show();
                TraceLine("RunManagedDirectFromUiAsync stopped: empty host.");
                return;
            }

            SetButtonsEnabled(false);
            _report.Clear();
            _uiReport.Clear();
            _reportText.Text = string.Empty;
            PersistReportSnapshot();
            SetStatus("Running managed-direct C# probe...");

            Append("A9 Vue990 Managed Direct C# Probe");
            Append($"Timestamp: {DateTimeOffset.Now:O}");
            Append($"Host: {host}");
            Append("");
            AppendNetworkState();
            Append("");

            using (AndroidWifiNetworkScope.Enter(this, Append))
            {
                Append("");
                var filesDir = FilesDir?.AbsolutePath ?? CacheDir?.AbsolutePath ?? "/data/local/tmp";
                _ = await new ManagedDirectMediaProbe().RunAsync(
                    host,
                    filesDir,
                    captureImage,
                    captureVideo,
                    Append);
            }

            SetStatus("Managed-direct C# probe complete.");
            TraceLine("RunManagedDirectFromUiAsync completed.");
        }
        catch (Exception ex)
        {
            Append($"Fatal: {ex.GetType().Name}: {ex.Message}");
            TraceLine($"RunManagedDirectFromUiAsync fatal: {ex}");
            SetStatus("Managed-direct C# probe failed.");
        }
        finally
        {
            SetButtonsEnabled(true);
            SaveReport();
        }
    }

    private async Task RunNativeOracleFromUiAsync(
        bool includeSocketOracle,
        bool includeVariants,
        string? variantCase)
    {
        try
        {
            TraceLine("RunNativeOracleFromUiAsync entered.");
            HideKeyboard();

            SetButtonsEnabled(false);
            _report.Clear();
            _uiReport.Clear();
            _reportText.Text = string.Empty;
            PersistReportSnapshot();
            SetStatus("Running native packet oracle...");

            Append("A9 Vue990 Native Packet Oracle");
            Append($"Timestamp: {DateTimeOffset.Now:O}");
            Append($"Socket oracle: {includeSocketOracle}");
            Append($"Variant oracle: {includeVariants}");
            Append($"Variant case: {variantCase ?? "<all>"}");
            Append("");
            AppendNetworkState();
            Append("");

            var nativeReport = await Task.Run(() => Vue990NativePacketOracle.Run(includeSocketOracle, includeVariants, variantCase));
            foreach (var line in nativeReport.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Append($"  {line}");

            SetStatus("Native packet oracle complete.");
            TraceLine("RunNativeOracleFromUiAsync completed.");
        }
        catch (Exception ex)
        {
            Append($"Fatal: {ex.GetType().Name}: {ex.Message}");
            TraceLine($"RunNativeOracleFromUiAsync fatal: {ex}");
            SetStatus("Native packet oracle failed.");
        }
        finally
        {
            SetButtonsEnabled(true);
            SaveReport();
        }
    }

    private async Task RunProbeAsync(string host)
    {
        Append("A9 Phone Probe");
        Append($"Timestamp: {DateTimeOffset.Now:O}");
        Append($"Host: {host}");
        Append("");
        AppendNetworkState();
        Append("");

        var openPorts = await ProbeTcpPortsAsync(host);
        Append("");

        await ProbeHttpAsync(host, openPorts);
        Append("");

        await ProbeUdpAsync(host);
        Append("");

        await ProbeVue990PpcsAsync(host);
        Append("");

        Append("Conclusion:");
        Append("- A stream is present only if an HTTP response above reports JPEG/multipart data, H.264-like bytes, or UDP video-like packets.");
        Append("- If the PPCS section connects, the stream path is Vue990/VStarcam native P2P rather than direct RTSP/MJPEG.");
        Append("- If only get_status.cgi responds and PPCS fails, the phone sees the same control-only API as the laptop.");
    }

    private async Task ProbeVue990PpcsAsync(
        string host,
        bool captureImage = false,
        bool captureVideo = false,
        string? serverOverride = null,
        bool fakeRelay = false,
        bool nativeChannelOracle = false,
        bool managedLiveCgi = false,
        string? managedLiveCgiMode = null)
    {
        Append("Vue990 PPCS native probe:");
        var endpoint = BuildHttpEndpoint(host, 81, "/get_status.cgi?loginuse=admin&loginpas=888888");
        string body;
        try
        {
            using var http = new HttpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await http.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var bytes = await ReadPrefixAsync(response.Content, 512 * 1024, timeout.Token);
            body = Encoding.UTF8.GetString(bytes);
            Append($"- status: {(int)response.StatusCode} {endpoint} bytes={bytes.Length}");
        }
        catch (Exception ex)
        {
            Append($"- status fetch failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var clientId = ExtractJavaScriptStringVar(body, "deviceid") ?? "BKGD00000100FMQLN";
        var vuid = ExtractJavaScriptStringVar(body, "realdeviceid") ?? string.Empty;
        var serverParam = ExtractJavaScriptStringVar(body, "server") ?? string.Empty;
        Append($"- identity: clientId={clientId} vuid={vuid} alias={ExtractJavaScriptStringVar(body, "alias") ?? "unknown"}");
        Append($"- server param: len={serverParam.Length} prefix={Prefix(serverParam, 16)}");

        if (!string.IsNullOrWhiteSpace(serverOverride))
        {
            serverParam = serverOverride.Trim();
            Append($"- server override: applied len={serverParam.Length} prefix={Prefix(serverParam, 16)}");
        }

        if (string.IsNullOrWhiteSpace(serverParam))
        {
            Append("- skipped: status response did not include a server parameter.");
            return;
        }

        Append(nativeChannelOracle
            ? "- runner: C# generated-binding session with native channel oracle"
            : captureImage || captureVideo
            ? "- runner: C# generated-binding session with capture"
            : "- runner: C# generated-binding session");
        Append($"- managed live CGI: {managedLiveCgi}");
        Append($"- managed live CGI mode: {managedLiveCgiMode ?? "<none>"}");

        using var fakeRelayRecorder = fakeRelay ? FakeRelayRecorder.Start(65527) : null;
        if (fakeRelayRecorder is not null)
            Append($"- fake relay: listening on {fakeRelayRecorder.BoundEndpoint}");

        try
        {
            var nativeReport = new Vue990PpcsSession().Run(
                clientId,
                vuid,
                serverParam,
                "admin",
                "888888",
                CacheDir?.AbsolutePath ?? FilesDir?.AbsolutePath ?? string.Empty,
                FilesDir?.AbsolutePath ?? CacheDir?.AbsolutePath ?? string.Empty,
                captureImage,
                captureVideo,
                nativeChannelOracle,
                managedLiveCgi,
                managedLiveCgiMode);
            foreach (var line in nativeReport.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Append($"  {line}");
        }
        finally
        {
            if (fakeRelayRecorder is not null)
            {
                fakeRelayRecorder.Stop();
                foreach (var line in fakeRelayRecorder.BuildReportLines())
                    Append($"  {line}");
            }
        }
    }

    private void AppendNetworkState()
    {
        Append("Network:");
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                if (IPAddress.IsLoopback(address.Address))
                    continue;

                Append($"- {nic.Name}: {address.Address}/{address.PrefixLength}");
            }
        }
    }

    private async Task<List<int>> ProbeTcpPortsAsync(string host)
    {
        var ports = new[]
        {
            21, 23, 53, 80, 81, 82, 83, 88, 443, 554, 1935,
            5000, 5001, 5002, 5544, 6123, 7070, 7447,
            8000, 8001, 8080, 8081, 8082, 8088, 8090,
            8554, 8555, 8899, 9000, 9999, 10000, 10080,
            10554, 1883, 20190, 32108, 34567, 37777, 49152,
        };
        var open = new List<int>();

        Append("TCP ports:");
        foreach (var port in ports)
        {
            if (await CanConnectTcpAsync(host, port, TimeSpan.FromMilliseconds(700)))
            {
                open.Add(port);
                Append($"- OPEN tcp/{port}");
            }
        }

        if (open.Count == 0)
            Append("- no open TCP ports found in the tested set");

        return open;
    }

    private async Task ProbeHttpAsync(string host, IReadOnlyList<int> openPorts)
    {
        var httpPorts = openPorts
            .Where(p => p is 80 or 81 or 82 or 83 or 88 or 443 or 8000 or 8001 or 8080 or 8081 or 8082 or 8088 or 8090 or 10080)
            .DefaultIfEmpty(81)
            .Distinct()
            .ToArray();

        var paths = new[]
        {
            "/get_status.cgi",
            "/get_status.cgi?user=admin&pwd=admin",
            "/get_status.cgi?loginuse=admin&loginpas=",
            "/",
            "/index.html",
            "/snapshot.cgi",
            "/snapshot.cgi?user=admin&pwd=admin",
            "/snapshot.cgi?user=admin&pwd=",
            "/snapshot.jpg",
            "/snap.jpg",
            "/image.jpg",
            "/tmpfs/auto.jpg",
            "/tmpfs/snap.jpg",
            "/cgi-bin/snapshot.cgi",
            "/onvif/snapshot",
            "/video",
            "/video.cgi",
            "/videostream.cgi",
            "/videostream.cgi?user=admin&pwd=admin",
            "/videostream.cgi?user=admin&pwd=",
            "/mjpeg",
            "/mjpeg.cgi",
            "/mjpg/video.mjpg",
            "/video.mjpg",
            "/video.mjpeg",
            "/live",
            "/live/ch00_0",
            "/stream",
            "/stream.cgi",
            "/stream.mjpg",
            "/live_stream.cgi",
            "/livestream.cgi",
            "/livestream.cgi?user=admin&pwd=",
            "/axis-cgi/mjpg/video.cgi",
            "/axis-cgi/jpg/image.cgi",
            "/nphMotionJpeg",
            "/cgi-bin/CGIStream.cgi?cmd=GetMJStream&usr=admin&pwd=",
        };

        Append("HTTP stream/control probes:");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BodyCam-A9PhoneProbe", "1.0"));

        var loggedFailures = 0;
        foreach (var port in httpPorts)
        {
            foreach (var path in paths)
            {
                var endpoint = BuildHttpEndpoint(host, port, path);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    using var response = await http.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    var bytes = await ReadPrefixAsync(response.Content, 192 * 1024, timeout.Token);
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                    var interesting = response.IsSuccessStatusCode ||
                                      IsStreamContent(contentType) ||
                                      LooksLikeJpeg(bytes) ||
                                      LooksLikeH264(bytes) ||
                                      bytes.Length > 0;

                    if (!interesting)
                        continue;

                    var flags = new List<string>();
                    if (IsStreamContent(contentType)) flags.Add("stream-content-type");
                    if (LooksLikeJpeg(bytes)) flags.Add("jpeg-like");
                    if (LooksLikeH264(bytes)) flags.Add("h264-like");

                    Append($"- {(int)response.StatusCode} {endpoint} bytes={bytes.Length} content-type={contentType} flags={string.Join(",", flags)}");
                    if (path.Contains("get_status.cgi", StringComparison.OrdinalIgnoreCase))
                    {
                        var body = Encoding.UTF8.GetString(bytes);
                        Append($"  device={ExtractJavaScriptStringVar(body, "realdeviceid") ?? ExtractJavaScriptStringVar(body, "deviceid") ?? "unknown"} alias={ExtractJavaScriptStringVar(body, "alias") ?? "unknown"} battery={ExtractJavaScriptVar(body, "batteryRate") ?? "unknown"}");
                    }
                }
                catch (Exception ex)
                {
                    if (path.Contains("get_status.cgi", StringComparison.OrdinalIgnoreCase) || loggedFailures < 3)
                    {
                        loggedFailures++;
                        Append($"- HTTP error {endpoint}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
    }

    private async Task ProbeUdpAsync(string host)
    {
        Append("UDP discovery probes:");
        var probes = new[]
        {
            new UdpProbe("cam-reverse LanSearch", 32108, Convert.FromHexString("F1300000")),
            new UdpProbe("SHIX/A9_PPPP seed", 32108, Convert.FromHexString("2CBA5F5D")),
            new UdpProbe("JSON discover 32108", 32108, Encoding.ASCII.GetBytes("{\"cmd\":\"discover\"}")),
            new UdpProbe("binary 20190 LanSearch", 20190, Convert.FromHexString("F1300000")),
            new UdpProbe("JSON discover 20190", 20190, Encoding.ASCII.GetBytes("{\"cmd\":\"discover\"}")),
        };

        var targets = new[]
        {
            host,
            GetSubnetBroadcast() ?? "192.168.168.255",
            "255.255.255.255",
        }.Distinct().ToArray();

        await ProbeUdpWithSocketAsync(null, probes, targets);
        await ProbeUdpWithSocketAsync(32108, probes, targets);
    }

    private async Task ProbeUdpWithSocketAsync(
        int? localPort,
        IReadOnlyList<UdpProbe> probes,
        IReadOnlyList<string> targets)
    {
        using var udp = localPort is null
            ? new UdpClient(AddressFamily.InterNetwork)
            : new UdpClient(localPort.Value, AddressFamily.InterNetwork);

        udp.EnableBroadcast = true;
        Append(localPort is null
            ? "- UDP socket: ephemeral local port"
            : $"- UDP socket: fixed local port {localPort.Value}");

        foreach (var probe in probes)
        {
            foreach (var target in targets)
            {
                if (!IPAddress.TryParse(target, out var ip))
                    continue;

                try
                {
                    await udp.SendAsync(probe.Payload, new IPEndPoint(ip, probe.Port));
                }
                catch (Exception ex)
                {
                    Append($"  send failed {probe.Name} to {target}:{probe.Port}: {ex.Message}");
                }
            }
        }

        var responses = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var response = await udp.ReceiveAsync(timeout.Token);
                responses++;
                Append($"  response from {response.RemoteEndPoint}: bytes={response.Buffer.Length} first={ToHex(response.Buffer, 48)}");
            }
            catch (System.OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Append($"  receive failed: {ex.Message}");
                break;
            }
        }

        if (responses == 0)
            Append("  no UDP responses");
    }

    private static async Task<bool> CanConnectTcpAsync(string host, int port, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await tcp.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildHttpEndpoint(string host, int port, string path)
    {
        var scheme = port == 443 ? "https" : "http";
        var portSuffix = (scheme == "http" && port == 80) || (scheme == "https" && port == 443)
            ? string.Empty
            : $":{port}";
        return $"{scheme}://{host}{portSuffix}{path}";
    }

    private static async Task<byte[]> ReadPrefixAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];

        while (memory.Length < maxBytes)
        {
            var remaining = Math.Min(buffer.Length, maxBytes - (int)memory.Length);
            var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), ct);
            if (read == 0)
                break;

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private string? GetSubnetBroadcast()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.IPv4Mask is null)
                    continue;

                var ipBytes = address.Address.GetAddressBytes();
                var maskBytes = address.IPv4Mask.GetAddressBytes();
                if (ipBytes.Length != 4 || maskBytes.Length != 4)
                    continue;

                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++)
                    broadcast[i] = (byte)(ipBytes[i] | ~maskBytes[i]);

                var value = new IPAddress(broadcast).ToString();
                if (value.StartsWith("192.168.168.", StringComparison.Ordinal))
                    return value;
            }
        }

        return null;
    }

    private static bool IsStreamContent(string contentType)
    {
        return contentType.Contains("multipart", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("image/jpg", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("video/", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeJpeg(byte[] bytes)
    {
        return IndexOf(bytes, [0xff, 0xd8], 0) >= 0;
    }

    private static bool LooksLikeH264(byte[] bytes)
    {
        return IndexOf(bytes, [0x00, 0x00, 0x01], 0) >= 0 ||
               IndexOf(bytes, [0x00, 0x00, 0x00, 0x01], 0) >= 0;
    }

    private static int IndexOf(byte[] data, byte[] pattern, int start)
    {
        for (var i = Math.Max(0, start); i <= data.Length - pattern.Length; i++)
        {
            var found = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] == pattern[j])
                    continue;

                found = false;
                break;
            }

            if (found)
                return i;
        }

        return -1;
    }

    private static string? ExtractJavaScriptStringVar(string body, string name)
    {
        var pattern = $"var {name}=\"";
        var start = body.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += pattern.Length;
        var end = body.IndexOf('"', start);
        return end > start ? body[start..end] : null;
    }

    private static string? ExtractJavaScriptVar(string body, string name)
    {
        var pattern = $"var {name}=";
        var start = body.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += pattern.Length;
        var end = body.IndexOf(';', start);
        return end > start ? body[start..end].Trim().Trim('"') : null;
    }

    private static string Prefix(string value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return "<empty>";

        var prefix = value[..Math.Min(max, value.Length)];
        return value.Length > max ? $"{prefix}..." : prefix;
    }

    private static string RunPpcsBridge(
        string clientId,
        string vuid,
        string serverParam,
        string user,
        string password,
        string cacheDir)
    {
        IntPtr bridgeClass = IntPtr.Zero;
        try
        {
            bridgeClass = JNIEnv.FindClass("com/bodycam/a9phoneprobe/PpcsProbeBridge");
            if (bridgeClass == IntPtr.Zero)
                return "PpcsProbeBridge class not found.";

            var method = JNIEnv.GetStaticMethodID(
                bridgeClass,
                "runProbe",
                "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;");

            using var jClientId = new JString(clientId);
            using var jVuid = new JString(vuid);
            using var jServerParam = new JString(serverParam);
            using var jUser = new JString(user);
            using var jPassword = new JString(password);
            using var jCacheDir = new JString(cacheDir);

            var result = JNIEnv.CallStaticObjectMethod(
                bridgeClass,
                method,
                new JValue(jClientId),
                new JValue(jVuid),
                new JValue(jServerParam),
                new JValue(jUser),
                new JValue(jPassword),
                new JValue(jCacheDir));

            return JNIEnv.GetString(result, JniHandleOwnership.TransferLocalRef) ?? string.Empty;
        }
        catch (Exception ex)
        {
            return $"PPCS bridge call failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            // JNIEnv.FindClass returns a class reference that Mono may track as global on
            // newer Android runtimes. Deleting it here aborts the process after the probe.
        }
    }

    private static string ToHex(byte[] bytes, int max)
    {
        return string.Join(" ", bytes.Take(max).Select(b => b.ToString("X2")));
    }

    private void Append(string line)
    {
        var elapsed = DateTimeOffset.Now.ToString("HH:mm:ss");
        var formatted = string.IsNullOrEmpty(line) ? string.Empty : $"[{elapsed}] {line}";
        string uiSnapshot;
        lock (_report)
        {
            _report.AppendLine(formatted);
            _uiReport.AppendLine(formatted);
            if (_uiReport.Length > MaxUiReportChars)
                _uiReport.Remove(0, _uiReport.Length - MaxUiReportChars);

            uiSnapshot = _uiReport.ToString();
            PersistReportSnapshot();
        }

        RunOnUiThread(() =>
        {
            _reportText.Text = uiSnapshot;
        });
    }

    private void SetStatus(string text)
    {
        RunOnUiThread(() => _statusText.Text = text);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _runButton.Enabled = enabled;
        _ppcsButton.Enabled = enabled;
        _copyButton.Enabled = enabled;
        _saveButton.Enabled = enabled;
    }

    private void CopyReport()
    {
        var clipboard = GetSystemService(ClipboardService) as Android.Content.ClipboardManager;
        if (clipboard is not null)
            clipboard.Text = GetReportSnapshot();
        Toast.MakeText(this, "Report copied.", ToastLength.Short)?.Show();
    }

    private void SaveReport()
    {
        try
        {
            var file = System.IO.Path.Combine(FilesDir?.AbsolutePath ?? CacheDir!.AbsolutePath, ReportFileName);
            PersistReportSnapshot();
            Toast.MakeText(this, $"Saved: {file}", ToastLength.Short)?.Show();
        }
        catch (Exception ex)
        {
            TraceLine($"SaveReport failed: {ex}");
            Toast.MakeText(this, $"Save failed: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    private void PersistReportSnapshot()
    {
        try
        {
            var directory = FilesDir?.AbsolutePath ?? CacheDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(directory))
                return;

            var file = System.IO.Path.Combine(directory, ReportFileName);
            File.WriteAllText(file, GetReportSnapshot());
        }
        catch (Exception ex)
        {
            TraceLine($"PersistReportSnapshot failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private string GetReportSnapshot()
    {
        lock (_report)
        {
            return _report.ToString();
        }
    }

    private void TraceLine(string message)
    {
        try
        {
            var line = $"[{DateTimeOffset.Now:O}] {message}{System.Environment.NewLine}";
            Log.Info(LogTag, message);

            var directory = FilesDir?.AbsolutePath ?? CacheDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(directory))
                return;

            var file = System.IO.Path.Combine(directory, TraceFileName);
            File.AppendAllText(file, line);
        }
        catch
        {
            // Tracing must never become the reason the app fails.
        }
    }

    private void HideKeyboard()
    {
        var inputMethodManager = (InputMethodManager?)GetSystemService(InputMethodService);
        inputMethodManager?.HideSoftInputFromWindow(_hostInput.WindowToken, HideSoftInputFlags.None);
        _hostInput.ClearFocus();
    }

    private int Dp(int value)
    {
        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        return (int)Math.Round(value * density);
    }

    private sealed record UdpProbe(string Name, int Port, byte[] Payload);
}
