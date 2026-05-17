using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Networking.Sockets;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// ═══════════════════════════════════════════════════════════════════════════
// 1. Enumerate all audio endpoints (MMDevice) — capture & render
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("══════════════════════════════════════════════════════════");
Console.WriteLine("  ALL AUDIO ENDPOINTS (MMDevice API / NAudio)");
Console.WriteLine("══════════════════════════════════════════════════════════");

using var enumerator = new MMDeviceEnumerator();

foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
{
    Console.WriteLine($"\n── {flow} ──");
    var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active | DeviceState.Unplugged);
    foreach (var dev in devices)
    {
        var isBt = dev.ID.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
                || dev.ID.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

        Console.WriteLine($"  [{(isBt ? "BT" : "  ")}] {dev.FriendlyName}");
        Console.WriteLine($"       ID: {dev.ID}");
        Console.WriteLine($"       State: {dev.State}");

        // Try to extract MAC-like patterns from the MMDevice ID
        var macMatch = Regex.Match(dev.ID, @"([0-9A-Fa-f]{2}[:\-_]){5}[0-9A-Fa-f]{2}");
        if (macMatch.Success)
            Console.WriteLine($"       Extracted MAC (colon/dash/underscore): {macMatch.Value}");

        // Also try contiguous 12-hex-digit patterns
        var hexMatch = Regex.Match(dev.ID, @"(?<![0-9A-Fa-f])([0-9A-Fa-f]{12})(?![0-9A-Fa-f])");
        if (hexMatch.Success)
        {
            var hex = hexMatch.Groups[1].Value;
            var mac = string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
            Console.WriteLine($"       Possible MAC (12-hex): {mac}");
        }

        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. Enumerate paired Classic BT devices (WinRT)
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("\n══════════════════════════════════════════════════════════");
Console.WriteLine("  PAIRED CLASSIC BLUETOOTH DEVICES (WinRT)");
Console.WriteLine("══════════════════════════════════════════════════════════\n");

var btSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
var btDevices = await DeviceInformation.FindAllAsync(btSelector);
foreach (var di in btDevices)
{
    Console.WriteLine($"  {di.Name}");
    Console.WriteLine($"       DeviceId: {di.Id}");
    Console.WriteLine($"       Paired: {di.Pairing?.IsPaired}");

    try
    {
        var btDev = await BluetoothDevice.FromIdAsync(di.Id);
        if (btDev is not null)
        {
            var mac = FormatMac(btDev.BluetoothAddress);
            Console.WriteLine($"       BT Address: {mac} (raw: 0x{btDev.BluetoothAddress:X12})");
            Console.WriteLine($"       ClassOfDevice: {btDev.ClassOfDevice?.MajorClass}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"       (could not get BT device: {ex.Message})");
    }
    Console.WriteLine();
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. Enumerate paired BLE devices (WinRT)
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("══════════════════════════════════════════════════════════");
Console.WriteLine("  PAIRED BLE DEVICES (WinRT)");
Console.WriteLine("══════════════════════════════════════════════════════════\n");

var bleSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
var bleDevices = await DeviceInformation.FindAllAsync(bleSelector);
foreach (var di in bleDevices)
{
    Console.WriteLine($"  {di.Name}");
    Console.WriteLine($"       DeviceId: {di.Id}");

    try
    {
        var bleDev = await BluetoothLEDevice.FromIdAsync(di.Id);
        if (bleDev is not null)
        {
            var mac = FormatMac(bleDev.BluetoothAddress);
            Console.WriteLine($"       BLE Address: {mac} (raw: 0x{bleDev.BluetoothAddress:X12})");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"       (could not get BLE device: {ex.Message})");
    }
    Console.WriteLine();
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. Dump PropertyStore for ALL non-Realtek/Intel devices (to find BT identifiers)
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("══════════════════════════════════════════════════════════");
Console.WriteLine("  MMDEVICE PROPERTY STORES (non-built-in devices)");
Console.WriteLine("══════════════════════════════════════════════════════════\n");

// Well-known property keys
var PKEY_Device_FriendlyName = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
var PKEY_Device_ContainerId = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"); // #2
var PKEY_Device_InstanceId = new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"); // #256
var PKEY_DeviceInterface_Enabled = new Guid("233164c8-1b2c-4c7d-bc68-b671687a2567");

foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
{
    var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active | DeviceState.Unplugged);
    foreach (var dev in devices)
    {
        // Skip built-in audio (Realtek, Intel) - focus on BT/external
        var name = dev.FriendlyName ?? "";
        if (name.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            continue;

        Console.WriteLine($"  [{flow}] {dev.FriendlyName}");
        Console.WriteLine($"    MMDevice.ID = {dev.ID}");
        Console.WriteLine($"    State = {dev.State}");

        try
        {
            var store = dev.Properties;
            for (int i = 0; i < store.Count; i++)
            {
                var key = store[i].Key;
                object? val;
                try { val = store[i].Value; }
                catch { val = "(error reading)"; }

                var valStr = val switch
                {
                    byte[] bytes => BitConverter.ToString(bytes),
                    Guid g => g.ToString(),
                    _ => val?.ToString() ?? "(null)"
                };

                // Highlight interesting properties
                var marker = "";
                if (key.formatId == PKEY_Device_ContainerId && key.propertyId == 2) marker = " ◄ ContainerId";
                if (key.formatId == PKEY_Device_InstanceId && key.propertyId == 256) marker = " ◄ InstanceId";
                if (valStr.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)) marker = " ◄◄ BTHENUM!";
                if (valStr.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)) marker = " ◄◄ Bluetooth!";

                Console.WriteLine($"    [{i:D3}] {{{key.formatId}}}#{key.propertyId} = {valStr}{marker}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    (property enumeration failed: {ex.Message})");
        }

        Console.WriteLine();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. SDP Service Records for Classic BT devices (WinRT RFCOMM)
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("══════════════════════════════════════════════════════════");
Console.WriteLine("  SDP SERVICE RECORDS (Classic BT)");
Console.WriteLine("══════════════════════════════════════════════════════════\n");

// Well-known BT profile UUIDs
var profileNames = new Dictionary<Guid, string>
{
    [Guid.Parse("00001108-0000-1000-8000-00805f9b34fb")] = "Headset (HSP)",
    [Guid.Parse("0000110b-0000-1000-8000-00805f9b34fb")] = "A2DP AudioSink",
    [Guid.Parse("0000110a-0000-1000-8000-00805f9b34fb")] = "A2DP AudioSource",
    [Guid.Parse("0000110c-0000-1000-8000-00805f9b34fb")] = "AVRCP Remote",
    [Guid.Parse("0000110e-0000-1000-8000-00805f9b34fb")] = "AVRCP Target",
    [Guid.Parse("0000111e-0000-1000-8000-00805f9b34fb")] = "HFP Hands-Free",
    [Guid.Parse("0000111f-0000-1000-8000-00805f9b34fb")] = "HFP Audio Gateway",
    [Guid.Parse("00001112-0000-1000-8000-00805f9b34fb")] = "Headset Audio Gateway",
    [Guid.Parse("00001101-0000-1000-8000-00805f9b34fb")] = "Serial Port (SPP)",
    [Guid.Parse("00001105-0000-1000-8000-00805f9b34fb")] = "OBEX Object Push",
    [Guid.Parse("00001116-0000-1000-8000-00805f9b34fb")] = "NAP",
    [Guid.Parse("00001115-0000-1000-8000-00805f9b34fb")] = "PANU",
    [Guid.Parse("0000112f-0000-1000-8000-00805f9b34fb")] = "Phonebook Access (PBAP)",
    [Guid.Parse("00001132-0000-1000-8000-00805f9b34fb")] = "MAP",
};

// Probe each paired Classic BT device for known profiles
foreach (var di in btDevices)
{
    BluetoothDevice? btDev = null;
    try { btDev = await BluetoothDevice.FromIdAsync(di.Id); }
    catch { continue; }
    if (btDev is null) continue;

    var mac = FormatMac(btDev.BluetoothAddress);
    Console.WriteLine($"  {di.Name} ({mac})");

    // Try each known profile UUID
    foreach (var (uuid, name) in profileNames)
    {
        try
        {
            var rfcommId = RfcommServiceId.FromUuid(uuid);
            var services = await btDev.GetRfcommServicesForIdAsync(rfcommId);
            if (services.Services.Count > 0)
            {
                Console.WriteLine($"    ✓ {name} ({uuid})");
                foreach (var svc in services.Services)
                    Console.WriteLine($"        ConnectionHostName={svc.ConnectionHostName}, ServiceName={svc.ConnectionServiceName}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ✗ {name} — error: {ex.Message}");
        }
    }

    // Also try generic SDP browse
    try
    {
        var allServices = await btDev.GetRfcommServicesAsync();
        Console.WriteLine($"    Total RFCOMM services: {allServices.Services.Count}");
        foreach (var svc in allServices.Services)
        {
            var svcUuid = svc.ServiceId.Uuid;
            var label = profileNames.TryGetValue(svcUuid, out var n) ? n : "Unknown";
            Console.WriteLine($"      SVC: {svcUuid} ({label}) Host={svc.ConnectionHostName} Name={svc.ConnectionServiceName}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"    (RFCOMM browse failed: {ex.Message})");
    }

    Console.WriteLine();
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. Programmatic Classic BT Pairing for M01 Pro
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("══════════════════════════════════════════════════════════");
Console.WriteLine("  PROGRAMMATIC CLASSIC BT PAIRING (M01 Pro)");
Console.WriteLine("══════════════════════════════════════════════════════════\n");

const ulong m01ProAddress = 0xD879B87FE6C9;

// Snapshot capture endpoints BEFORE pairing
var captureBeforePairing = new HashSet<string>();
foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active | DeviceState.Unplugged))
    captureBeforePairing.Add(dev.ID);
Console.WriteLine($"  Capture endpoints before: {captureBeforePairing.Count}");

var m01Device = await BluetoothDevice.FromBluetoothAddressAsync(m01ProAddress);
if (m01Device is null)
{
    Console.WriteLine("  ✗ Could not get BluetoothDevice for M01 Pro — is it powered on?");
}
else
{
    Console.WriteLine($"  Device: {m01Device.Name}");
    Console.WriteLine($"  DeviceId: {m01Device.DeviceId}");
    Console.WriteLine($"  ConnectionStatus: {m01Device.ConnectionStatus}");
    Console.WriteLine($"  IsPaired: {m01Device.DeviceInformation.Pairing.IsPaired}");
    Console.WriteLine($"  CanPair: {m01Device.DeviceInformation.Pairing.CanPair}");
    Console.WriteLine($"  ProtectionLevel: {m01Device.DeviceInformation.Pairing.ProtectionLevel}");

    if (m01Device.DeviceInformation.Pairing.IsPaired)
    {
        Console.WriteLine("  Already paired — skipping pairing step");
    }
    else if (!m01Device.DeviceInformation.Pairing.CanPair)
    {
        Console.WriteLine("  ✗ Device reports CanPair=false");
    }
    else
    {
        Console.WriteLine("\n  Attempting Custom pairing...");
        var customPairing = m01Device.DeviceInformation.Pairing.Custom;
        customPairing.PairingRequested += (sender, args) =>
        {
            Console.WriteLine($"    [PairingRequested] Kind={args.PairingKind} Pin={args.Pin}");
            if (args.PairingKind == DevicePairingKinds.ConfirmOnly)
            {
                Console.WriteLine("    Accepting ConfirmOnly...");
                args.Accept();
            }
            else if (args.PairingKind == DevicePairingKinds.ConfirmPinMatch)
            {
                Console.WriteLine($"    Accepting ConfirmPinMatch (Pin={args.Pin})...");
                args.Accept();
            }
            else if (args.PairingKind == DevicePairingKinds.ProvidePin)
            {
                Console.WriteLine("    Providing PIN '0000'...");
                args.Accept("0000");
            }
            else
            {
                Console.WriteLine($"    Unknown pairing kind {args.PairingKind} — accepting anyway");
                args.Accept();
            }
        };

        var pairingKinds = DevicePairingKinds.ConfirmOnly
            | DevicePairingKinds.ConfirmPinMatch
            | DevicePairingKinds.ProvidePin;

        Console.WriteLine($"  Calling PairAsync with kinds: {pairingKinds}...");

        DevicePairingResult pairResult = null!;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            Console.WriteLine($"\n  --- Attempt {attempt}/5 ---");

            // Re-acquire device each attempt (fresh state)
            if (attempt > 1)
            {
                m01Device = await BluetoothDevice.FromBluetoothAddressAsync(m01ProAddress);
                if (m01Device is null)
                {
                    Console.WriteLine("  ✗ Device disappeared");
                    break;
                }
                Console.WriteLine($"  IsPaired={m01Device.DeviceInformation.Pairing.IsPaired} CanPair={m01Device.DeviceInformation.Pairing.CanPair}");
                if (m01Device.DeviceInformation.Pairing.IsPaired)
                {
                    Console.WriteLine("  ✓ Already paired on retry!");
                    pairResult = null!; // will check IsPaired below
                    break;
                }
                customPairing = m01Device.DeviceInformation.Pairing.Custom;
                customPairing.PairingRequested += (sender, args) =>
                {
                    Console.WriteLine($"    [PairingRequested] Kind={args.PairingKind} Pin={args.Pin}");
                    args.Accept();
                };
            }

            pairResult = await customPairing.PairAsync(pairingKinds);
            Console.WriteLine($"  PairAsync result: {pairResult.Status}");
            Console.WriteLine($"  ProtectionLevelUsed: {pairResult.ProtectionLevelUsed}");

            if (pairResult.Status == DevicePairingResultStatus.Paired
                || pairResult.Status == DevicePairingResultStatus.AlreadyPaired)
            {
                Console.WriteLine("  ✓ Pairing succeeded!");
                break;
            }

            Console.WriteLine($"  Pairing failed ({pairResult.Status}) — retrying in 3s...");
            await Task.Delay(3000);
        }

        // Re-check pairing state
        m01Device = await BluetoothDevice.FromBluetoothAddressAsync(m01ProAddress);
        var isPaired = m01Device?.DeviceInformation.Pairing.IsPaired == true;

        if (isPaired)
        {
            Console.WriteLine("  ✓ Pairing succeeded!");
            Console.WriteLine("  Waiting 10s for Windows to set up audio drivers...");
            await Task.Delay(10000);

            // Check for new capture endpoints
            var captureAfterPairing = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active | DeviceState.Unplugged);
            Console.WriteLine($"\n  Capture endpoints after pairing: {captureAfterPairing.Count}");
            foreach (var ep in captureAfterPairing)
            {
                var isNew = !captureBeforePairing.Contains(ep.ID);
                Console.WriteLine($"    [{(isNew ? "NEW" : "   ")}] {ep.FriendlyName} — {ep.State}");
                Console.WriteLine($"          ID: {ep.ID}");
            }

            // Also check render
            var renderAfterPairing = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged);
            Console.WriteLine($"\n  Render endpoints after pairing: {renderAfterPairing.Count}");
            foreach (var ep in renderAfterPairing)
                Console.WriteLine($"    {ep.FriendlyName} — {ep.State}");

            // Wait more if no new capture appeared
            var newCapture = captureAfterPairing.Where(d => !captureBeforePairing.Contains(d.ID)).ToList();
            if (newCapture.Count == 0)
            {
                Console.WriteLine("\n  No new capture yet — waiting 20 more seconds...");
                await Task.Delay(20000);
                captureAfterPairing = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active | DeviceState.Unplugged);
                Console.WriteLine($"  Capture endpoints after extended wait: {captureAfterPairing.Count}");
                newCapture = captureAfterPairing.Where(d => !captureBeforePairing.Contains(d.ID)).ToList();
                if (newCapture.Count > 0)
                {
                    Console.WriteLine("  ✓ NEW capture endpoints appeared:");
                    foreach (var ep in newCapture)
                        Console.WriteLine($"      {ep.FriendlyName} — {ep.ID}");
                }
                else
                {
                    Console.WriteLine("  ✗ Still no new capture endpoints after 30s total wait");
                }
            }
        }
        else
        {
            Console.WriteLine("  ✗ Pairing never succeeded after all attempts");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. Property store dump for M01 Pro endpoints (post-pairing diagnostic)
// ═══════════════════════════════════════════════════════════════════════════
Console.WriteLine("══════════════════════════════════════════════════════════");
Console.WriteLine("  PROPERTY STORE — M01 Pro ENDPOINTS");
Console.WriteLine("══════════════════════════════════════════════════════════\n");

var enumeratorNameKey = new NAudio.CoreAudioApi.PropertyKey(
    new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);
var instancePathKey = new NAudio.CoreAudioApi.PropertyKey(
    new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);

foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
{
    var allDevices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active | DeviceState.Unplugged);
    foreach (var dev in allDevices)
    {
        if (!dev.FriendlyName.Contains("M01", StringComparison.OrdinalIgnoreCase))
            continue;

        Console.WriteLine($"  [{flow}] {dev.FriendlyName}");
        Console.WriteLine($"    MMDevice.ID = {dev.ID}");
        Console.WriteLine($"    State = {dev.State}");

        // Key property: EnumeratorName
        try
        {
            var enumeratorName = dev.Properties[enumeratorNameKey].Value?.ToString();
            Console.WriteLine($"    DEVPKEY_Device_EnumeratorName (#24) = {enumeratorName ?? "(null)"}");
            Console.WriteLine($"    → IsBluetoothDevice = {string.Equals(enumeratorName, "BTHENUM", StringComparison.OrdinalIgnoreCase)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    DEVPKEY_Device_EnumeratorName — FAILED: {ex.Message}");
        }

        // Key property: Instance path (for MAC extraction)
        try
        {
            var instancePath = dev.Properties[instancePathKey].Value?.ToString();
            Console.WriteLine($"    Instance path (#2) = {instancePath ?? "(null)"}");

            if (instancePath is not null)
            {
                var macMatch = System.Text.RegularExpressions.Regex.Match(instancePath, @"&([0-9A-Fa-f]{12})_C");
                if (macMatch.Success)
                {
                    var hex = macMatch.Groups[1].Value.ToUpperInvariant();
                    var mac = $"{hex[..2]}:{hex[2..4]}:{hex[4..6]}:{hex[6..8]}:{hex[8..10]}:{hex[10..12]}";
                    Console.WriteLine($"    → Extracted MAC = {mac}");
                }
                else
                {
                    Console.WriteLine($"    → MAC regex did NOT match!");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Instance path — FAILED: {ex.Message}");
        }

        Console.WriteLine();
    }
}

Console.WriteLine("\nDone. Press Enter to exit.");
Console.ReadLine();

static string FormatMac(ulong address)
{
    var bytes = BitConverter.GetBytes(address);
    return $"{bytes[5]:X2}:{bytes[4]:X2}:{bytes[3]:X2}:{bytes[2]:X2}:{bytes[1]:X2}:{bytes[0]:X2}";
}
