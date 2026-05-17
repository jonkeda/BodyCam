using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== BLE SCAN DIAGNOSTIC ===");
Console.WriteLine($"Time: {DateTime.Now}");

// 1. Check Bluetooth radio
Console.WriteLine("\n--- Bluetooth Radio ---");
try
{
    var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
    foreach (var r in radios)
        Console.WriteLine($"  {r.Name}: Kind={r.Kind} State={r.State}");
}
catch (Exception ex)
{
    Console.WriteLine($"  Radio enumeration failed: {ex.Message}");
}

// 2. BLE scan
Console.WriteLine("\n--- BLE Advertisement Scan (15 seconds) ---");
var devices = new ConcurrentDictionary<ulong, (string Name, short Rssi)>();
int totalAds = 0;

var watcher = new BluetoothLEAdvertisementWatcher
{
    ScanningMode = BluetoothLEScanningMode.Active,
};

watcher.Received += (_, args) =>
{
    var count = Interlocked.Increment(ref totalAds);
    var name = args.Advertisement.LocalName;
    if (!string.IsNullOrEmpty(name))
        devices[args.BluetoothAddress] = (name, args.RawSignalStrengthInDBm);

    // Print first few advertisements as proof of life
    if (count <= 5)
    {
        var mac = FormatMac(args.BluetoothAddress);
        Console.WriteLine($"  [Ad #{count}] {mac} RSSI={args.RawSignalStrengthInDBm}dBm Name=\"{name}\"");
    }
};

watcher.Stopped += (_, args) =>
{
    Console.WriteLine($"  Watcher stopped: Error={args.Error}");
};

watcher.Start();
Console.WriteLine($"  Watcher status after Start(): {watcher.Status}");

await Task.Delay(TimeSpan.FromSeconds(15));
watcher.Stop();

Console.WriteLine($"\n  Total advertisements received: {totalAds}");
Console.WriteLine($"  Unique named devices: {devices.Count}");
foreach (var (addr, info) in devices)
{
    var mac = FormatMac(addr);
    Console.WriteLine($"    {info.Name} ({mac}) RSSI={info.Rssi}dBm");
}

// 3. Try direct connection by address (skip scan)
Console.WriteLine("\n--- Direct BLE Connection by Address ---");
const ulong m01ProAddress = 0xD879B87FE6C9; // D8:79:B8:7F:E6:C9
Console.WriteLine($"  Trying BluetoothLEDevice.FromBluetoothAddressAsync(0x{m01ProAddress:X12})...");
try
{
    var bleDev = await BluetoothLEDevice.FromBluetoothAddressAsync(m01ProAddress);
    if (bleDev is not null)
    {
        Console.WriteLine($"  SUCCESS! Name={bleDev.Name} ConnectionStatus={bleDev.ConnectionStatus}");
        Console.WriteLine($"  DeviceId={bleDev.DeviceId}");

        // Try getting GATT services
        var gattResult = await bleDev.GetGattServicesAsync();
        Console.WriteLine($"  GATT Status={gattResult.Status} Services={gattResult.Services.Count}");
        foreach (var svc in gattResult.Services)
            Console.WriteLine($"    Service: {svc.Uuid}");
    }
    else
    {
        Console.WriteLine("  Returned null - device not reachable via BLE");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  Failed: {ex.GetType().Name}: {ex.Message}");
}

// 4. Try Classic BT connection
Console.WriteLine("\n--- Direct Classic BT Connection ---");
Console.WriteLine($"  Trying BluetoothDevice.FromBluetoothAddressAsync(0x{m01ProAddress:X12})...");
try
{
    var btDev = await BluetoothDevice.FromBluetoothAddressAsync(m01ProAddress);
    if (btDev is not null)
    {
        Console.WriteLine($"  SUCCESS! Name={btDev.Name} ConnectionStatus={btDev.ConnectionStatus}");
        Console.WriteLine($"  IsPaired={btDev.DeviceInformation.Pairing.IsPaired}");
    }
    else
    {
        Console.WriteLine("  Returned null");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  Failed: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine("\nDone.");

static string FormatMac(ulong address)
{
    var bytes = BitConverter.GetBytes(address);
    return $"{bytes[5]:X2}:{bytes[4]:X2}:{bytes[3]:X2}:{bytes[2]:X2}:{bytes[1]:X2}:{bytes[0]:X2}";
}
