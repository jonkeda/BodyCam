#if ANDROID
using Android.Bluetooth;
using Android.Content;
using Android.Util;
using BodyCam.Services;
using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using System.Text.Json;

namespace BodyCam.Platforms.Android.HeyCyan;

internal sealed class HeyCyanAndroidProbe
{
    public const string Action = "com.companyname.bodycam.HEYCYAN_PROBE";

    private const string Tag = "BodyCamHeyCyanProbe";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly Intent _intent;
    private readonly Context _context;
    private readonly ProbeResult _result = new();

    private HeyCyanAndroidProbe(Intent intent)
    {
        _intent = intent;
        _context = Platform.AppContext;
    }

    public static async Task RunFromIntentAsync(Intent intent)
    {
        if (!await Gate.WaitAsync(0).ConfigureAwait(false))
        {
            Log.Warn(Tag, "Probe already running; ignoring duplicate intent.");
            return;
        }

        try
        {
            await new HeyCyanAndroidProbe(intent).RunAsync().ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task RunAsync()
    {
        _result.StartedAt = DateTimeOffset.Now;
        var outputDir = PrepareOutputDirectory();
        _result.OutputDirectory = outputDir;
        AddStep($"Probe output: {outputDir}");
        var holdOnFailureSeconds = Math.Clamp(_intent.GetIntExtra("holdOnFailureSeconds", 0), 0, 300);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var ct = cts.Token;
            var services = IPlatformApplication.Current?.Services
                ?? throw new InvalidOperationException("MAUI service provider is not available.");

            var settings = services.GetRequiredService<ISettingsService>();
            var session = services.GetRequiredService<IHeyCyanGlassesSession>();
            var transfer = services.GetRequiredService<IHeyCyanMediaTransfer>();

            var requestedAddress = _intent.GetStringExtra("address");
            var requestedName = _intent.GetStringExtra("name");
            var scanTimeoutSeconds = _intent.GetIntExtra("scanTimeoutSeconds", 8);
            var capturePhotoBeforeTransfer = _intent.GetBooleanExtra("capturePhotoBeforeTransfer", false);
            var recordVideoBeforeTransfer = _intent.GetBooleanExtra("recordVideoBeforeTransfer", false);
            var videoSeconds = Math.Clamp(_intent.GetIntExtra("videoSeconds", 4), 2, 20);

            var device = session.State is HeyCyanState.Connected or HeyCyanState.TransferMode
                ? session.Device
                : await FindDeviceAsync(
                    session,
                    requestedAddress,
                    requestedName,
                    settings.LastHeyCyanDeviceAddress,
                    TimeSpan.FromSeconds(scanTimeoutSeconds),
                    ct).ConfigureAwait(false);

            if (device is null)
                throw new InvalidOperationException("No HeyCyan/M01/QC BLE device found by scan or bonded-device fallback.");

            _result.DeviceName = device.Name;
            _result.DeviceAddress = device.Address;
            AddStep($"Selected device: {device.Name} ({device.Address})");

            if (session.State is not HeyCyanState.Connected and not HeyCyanState.TransferMode)
            {
                AddStep("Connecting BLE session.");
                await session.ConnectAsync(device, ct).ConfigureAwait(false);
            }
            else
            {
                AddStep($"Reusing existing session state: {session.State}.");
            }

            await TryReadStatusAsync(session, ct).ConfigureAwait(false);
            await TryCreateFreshMediaAsync(
                session,
                capturePhotoBeforeTransfer,
                recordVideoBeforeTransfer,
                TimeSpan.FromSeconds(videoSeconds),
                ct).ConfigureAwait(false);

            AddStep("Listing glasses media over C# transfer path.");
            var entries = await transfer.ListAsync(ct).ConfigureAwait(false);
            _result.MediaEntries = entries
                .Select(e => $"{e.Kind}: {e.Name} ({e.Timestamp:O})")
                .ToList();
            AddStep($"Listed {entries.Count} media entries.");

            var photo = await DownloadNewestAsync(
                transfer,
                entries,
                HeyCyanMediaKind.Photo,
                outputDir,
                "latest-photo.jpg",
                ct).ConfigureAwait(false);
            if (photo is not null)
                _result.Files.Add(photo);

            var video = await DownloadNewestAsync(
                transfer,
                entries,
                HeyCyanMediaKind.Video,
                outputDir,
                "latest-video.mp4",
                ct).ConfigureAwait(false);
            if (video is not null)
                _result.Files.Add(video);

            if (photo is null)
                AddStep("No photo entry was available to download.");
            if (video is null)
                AddStep("No video entry was available to download.");

            _result.Success = photo is not null && video is not null;
        }
        catch (Exception ex)
        {
            _result.Success = false;
            _result.Error = ex.ToString();
            AddStep($"ERROR: {ex.Message}");
            Log.Error(Tag, ex.ToString());
            await HoldAfterFailureAsync(holdOnFailureSeconds).ConfigureAwait(false);
        }
        finally
        {
            _result.FinishedAt = DateTimeOffset.Now;
            await WriteResultAsync(outputDir).ConfigureAwait(false);
            AddLogcatSummary();
        }
    }

    private async Task<HeyCyanDeviceInfo?> FindDeviceAsync(
        IHeyCyanGlassesSession session,
        string? requestedAddress,
        string? requestedName,
        string? savedAddress,
        TimeSpan scanTimeout,
        CancellationToken ct)
    {
        var scanned = Array.Empty<HeyCyanDeviceInfo>();
        try
        {
            AddStep($"Scanning BLE for {scanTimeout.TotalSeconds:N0}s.");
            scanned = (await session.ScanAsync(scanTimeout, ct).ConfigureAwait(false)).ToArray();
            _result.ScanResults = scanned.Select(d => $"{d.Name} ({d.Address}) RSSI {d.Rssi}").ToList();
            AddStep($"Scan found {scanned.Length} candidate(s).");
        }
        catch (Exception ex)
        {
            AddStep($"Scan failed, trying bonded-device fallback: {ex.Message}");
        }

        var selected = SelectDevice(scanned, requestedAddress, requestedName, savedAddress);
        if (selected is not null)
            return selected;

        selected = FindBondedDevice(requestedAddress, requestedName, savedAddress);
        if (selected is not null)
        {
            _result.BondedFallback = $"{selected.Name} ({selected.Address})";
            AddStep($"Bonded fallback selected {selected.Name} ({selected.Address}).");
        }

        return selected;
    }

    private static HeyCyanDeviceInfo? SelectDevice(
        IEnumerable<HeyCyanDeviceInfo> devices,
        string? requestedAddress,
        string? requestedName,
        string? savedAddress)
    {
        var ordered = devices
            .Where(d => IsLikelyHeyCyanName(d.Name))
            .OrderByDescending(d => d.Rssi)
            .ToArray();

        return MatchDevice(ordered, requestedAddress, requestedName, savedAddress) ?? ordered.FirstOrDefault();
    }

    private HeyCyanDeviceInfo? FindBondedDevice(
        string? requestedAddress,
        string? requestedName,
        string? savedAddress)
    {
        try
        {
            var manager = (BluetoothManager?)_context.GetSystemService(Context.BluetoothService);
            var devices = manager?.Adapter?.BondedDevices;
            if (devices is null)
                return null;

            var bonded = devices
                .Select(d => new HeyCyanDeviceInfo(d.Name ?? d.Address ?? "HeyCyan", d.Address ?? string.Empty, -127))
                .Where(d => !string.IsNullOrWhiteSpace(d.Address))
                .Where(d => IsLikelyHeyCyanName(d.Name))
                .ToArray();

            return MatchDevice(bonded, requestedAddress, requestedName, savedAddress) ?? bonded.FirstOrDefault();
        }
        catch (Exception ex)
        {
            AddStep($"Bonded-device fallback failed: {ex.Message}");
            return null;
        }
    }

    private static HeyCyanDeviceInfo? MatchDevice(
        IEnumerable<HeyCyanDeviceInfo> devices,
        string? requestedAddress,
        string? requestedName,
        string? savedAddress)
    {
        if (!string.IsNullOrWhiteSpace(requestedAddress))
        {
            var match = devices.FirstOrDefault(d =>
                string.Equals(d.Address, requestedAddress, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        if (!string.IsNullOrWhiteSpace(savedAddress))
        {
            var match = devices.FirstOrDefault(d =>
                string.Equals(d.Address, savedAddress, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            var match = devices.FirstOrDefault(d =>
                d.Name.Contains(requestedName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return null;
    }

    private async Task TryReadStatusAsync(IHeyCyanGlassesSession session, CancellationToken ct)
    {
        try
        {
            var version = await session.GetVersionAsync(ct).ConfigureAwait(false);
            _result.Version = $"{version.Hardware}/{version.Firmware} wifi {version.WifiHardware}/{version.WifiFirmware} mac {version.MacAddress}";
            AddStep($"Version: {_result.Version}");
        }
        catch (Exception ex)
        {
            AddStep($"Version read failed: {ex.Message}");
        }

        try
        {
            var battery = await session.GetBatteryAsync(ct).ConfigureAwait(false);
            _result.Battery = $"{battery.Percentage}% charging={battery.IsCharging}";
            AddStep($"Battery: {_result.Battery}");
        }
        catch (Exception ex)
        {
            AddStep($"Battery read failed: {ex.Message}");
        }
    }

    private async Task TryCreateFreshMediaAsync(
        IHeyCyanGlassesSession session,
        bool capturePhoto,
        bool recordVideo,
        TimeSpan videoDuration,
        CancellationToken ct)
    {
        if (capturePhoto)
        {
            AddStep("Capturing a fresh photo before transfer.");
            await session.TakePhotoAsync(ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }

        if (recordVideo)
        {
            AddStep($"Recording a fresh video for {videoDuration.TotalSeconds:N0}s before transfer.");
            await session.StartVideoAsync(ct).ConfigureAwait(false);
            await Task.Delay(videoDuration, ct).ConfigureAwait(false);
            await session.StopVideoAsync(ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }
    }

    private async Task<ProbeMediaFile?> DownloadNewestAsync(
        IHeyCyanMediaTransfer transfer,
        IReadOnlyList<HeyCyanMediaEntry> entries,
        HeyCyanMediaKind kind,
        string outputDir,
        string aliasName,
        CancellationToken ct)
    {
        var entry = entries
            .Where(e => e.Kind == kind)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();

        if (entry is null)
            return null;

        var fileName = Path.GetFileName(entry.Name);
        var targetPath = Path.Combine(outputDir, fileName);
        AddStep($"Downloading {kind}: {entry.Name}");

        await using (var input = await transfer.OpenAsync(entry.Name, ct).ConfigureAwait(false))
        await using (var output = System.IO.File.Create(targetPath))
        {
            await input.CopyToAsync(output, ct).ConfigureAwait(false);
        }

        var aliasPath = Path.Combine(outputDir, aliasName);
        System.IO.File.Copy(targetPath, aliasPath, overwrite: true);

        var info = new FileInfo(targetPath);
        var signature = await ReadSignatureAsync(targetPath, ct).ConfigureAwait(false);
        var mediaFile = new ProbeMediaFile
        {
            Kind = kind.ToString(),
            Name = entry.Name,
            Path = targetPath,
            AliasPath = aliasPath,
            Length = info.Length,
            FirstBytesHex = Convert.ToHexString(signature),
            IsJpeg = signature.Length >= 2 && signature[0] == 0xFF && signature[1] == 0xD8,
            IsMp4 = signature.Length >= 8
                && signature[4] == (byte)'f'
                && signature[5] == (byte)'t'
                && signature[6] == (byte)'y'
                && signature[7] == (byte)'p'
        };

        AddStep($"Downloaded {entry.Name} to {targetPath} ({mediaFile.Length:N0} bytes, {mediaFile.FirstBytesHex}).");
        return mediaFile;
    }

    private static async Task<byte[]> ReadSignatureAsync(string path, CancellationToken ct)
    {
        var bytes = new byte[16];
        await using var stream = System.IO.File.OpenRead(path);
        var count = await stream.ReadAsync(bytes.AsMemory(0, bytes.Length), ct).ConfigureAwait(false);
        return bytes[..count];
    }

    private string PrepareOutputDirectory()
    {
        var baseDir = _context.GetExternalFilesDir("heycyan-probe")?.AbsolutePath
            ?? _context.FilesDir?.AbsolutePath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var outputDir = Path.Combine(baseDir, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(outputDir);
        return outputDir;
    }

    private async Task WriteResultAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var json = JsonSerializer.Serialize(_result, new JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(Path.Combine(outputDir, "probe-result.json"), json).ConfigureAwait(false);
        await System.IO.File.WriteAllLinesAsync(Path.Combine(outputDir, "probe-log.txt"), _result.Steps).ConfigureAwait(false);
    }

    private async Task HoldAfterFailureAsync(int seconds)
    {
        if (seconds <= 0)
            return;

        AddStep($"Holding failed probe state for {seconds}s for shell inspection.");
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AddStep($"Failure hold interrupted: {ex.Message}");
        }
    }

    private void AddStep(string step)
    {
        var line = $"{DateTimeOffset.Now:O} {step}";
        _result.Steps.Add(line);
        Log.Info(Tag, step);
    }

    private void AddLogcatSummary()
    {
        Log.Info(Tag, $"Probe finished success={_result.Success} output={_result.OutputDirectory}");
        foreach (var file in _result.Files)
            Log.Info(Tag, $"{file.Kind}: {file.Path} ({file.Length} bytes)");
    }

    private static bool IsLikelyHeyCyanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.StartsWith("M01", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("QC", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("O_", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cyan", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProbeResult
    {
        public bool Success { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset FinishedAt { get; set; }
        public string? OutputDirectory { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceAddress { get; set; }
        public string? Version { get; set; }
        public string? Battery { get; set; }
        public string? BondedFallback { get; set; }
        public string? Error { get; set; }
        public List<string> ScanResults { get; set; } = [];
        public List<string> MediaEntries { get; set; } = [];
        public List<ProbeMediaFile> Files { get; set; } = [];
        public List<string> Steps { get; set; } = [];
    }

    private sealed class ProbeMediaFile
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? AliasPath { get; set; }
        public long Length { get; set; }
        public string? FirstBytesHex { get; set; }
        public bool IsJpeg { get; set; }
        public bool IsMp4 { get; set; }
    }
}
#endif
