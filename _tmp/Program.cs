using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

var mac = "D8:79:B8:7F:E6:C9";
var address = Convert.ToUInt64(mac.Replace(":", ""), 16);
Console.WriteLine("=== BLE Serial Port Protocol (0xBC framing) Test ===\n");

// CRC-16/ARC: initial=0xFFFF, poly=0xA001 (reflected)
static ushort Crc16(byte[] data)
{
    ushort crc = 0xFFFF;
    foreach (var b in data)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++)
        {
            if ((crc & 1) != 0)
                crc = (ushort)((crc >> 1) ^ 0xA001);
            else
                crc >>= 1;
        }
    }
    return crc;
}

// Build Serial Port frame: [0xBC][action][len_lo][len_hi][crc16_lo][crc16_hi][payload...]
// ALL multi-byte fields are LITTLE-ENDIAN (confirmed by device response analysis)
static byte[] BuildFrame(byte action, byte[] payload)
{
    var frame = new byte[6 + payload.Length];
    frame[0] = 0xBC;
    frame[1] = action;
    frame[2] = (byte)(payload.Length & 0xFF);        // len low
    frame[3] = (byte)((payload.Length >> 8) & 0xFF); // len high
    if (payload.Length > 0)
    {
        var crc = Crc16(payload);
        frame[4] = (byte)(crc & 0xFF);        // crc low
        frame[5] = (byte)((crc >> 8) & 0xFF); // crc high
        Array.Copy(payload, 0, frame, 6, payload.Length);
    }
    else
    {
        frame[4] = 0xFF;
        frame[5] = 0xFF;
    }
    return frame;
}

// Build NUS 16-byte frame: [key][subdata(14B)][crc]
static byte[] BuildNus(byte key, byte[] subData)
{
    var frame = new byte[16];
    frame[0] = key;
    if (subData != null)
        Array.Copy(subData, 0, frame, 1, Math.Min(subData.Length, 14));
    int sum = 0;
    for (int i = 0; i < 15; i++) sum += frame[i];
    frame[15] = (byte)(sum & 0xFF);
    return frame;
}

// Connect
Console.WriteLine("[1] Connecting to BLE device...");
var bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask();
Console.WriteLine($"  Name: {bleDevice.Name}, Connected: {bleDevice.ConnectionStatus}");

bleDevice.ConnectionStatusChanged += (s, e) =>
    Console.WriteLine($"  *** Connection status: {s.ConnectionStatus}");

// Get Serial Port service (de5bf728)
var spSvcResult = await bleDevice.GetGattServicesForUuidAsync(
    Guid.Parse("de5bf728-d711-4e47-af26-65e3012a5dc7")).AsTask();
var spSvc = spSvcResult.Services[0];

// Create GattSession for stable connection
Console.WriteLine("  Creating GattSession with MaintainConnection...");
var session = await GattSession.FromDeviceIdAsync(bleDevice.BluetoothDeviceId).AsTask();
session.MaintainConnection = true;
Console.WriteLine($"  MaxPduSize: {session.MaxPduSize}, CanMaintain: {session.CanMaintainConnection}");
await Task.Delay(2000); // Let connection stabilize

Console.WriteLine($"  Connection after session: {bleDevice.ConnectionStatus}");

var spCharsResult = await spSvc.GetCharacteristicsAsync().AsTask();

GattCharacteristic? spWrite = null, spNotify = null;
foreach (var c in spCharsResult.Characteristics)
{
    if (c.Uuid == Guid.Parse("de5bf72a-d711-4e47-af26-65e3012a5dc7")) spWrite = c;
    if (c.Uuid == Guid.Parse("de5bf729-d711-4e47-af26-65e3012a5dc7")) spNotify = c;
}
Console.WriteLine($"  Serial Port: Write={spWrite != null}, Notify={spNotify != null}");

// Get NUS service (6e40fff0)
var nusSvcResult = await bleDevice.GetGattServicesForUuidAsync(
    Guid.Parse("6e40fff0-b5a3-f393-e0a9-e50e24dcca9e")).AsTask();
var nusSvc = nusSvcResult.Services[0];
var nusCharsResult = await nusSvc.GetCharacteristicsAsync().AsTask();

GattCharacteristic? nusWrite = null, nusNotify = null;
foreach (var c in nusCharsResult.Characteristics)
{
    if (c.Uuid == Guid.Parse("6e400002-b5a3-f393-e0a9-e50e24dcca9e")) nusWrite = c;
    if (c.Uuid == Guid.Parse("6e400003-b5a3-f393-e0a9-e50e24dcca9e")) nusNotify = c;
}
Console.WriteLine($"  NUS: Write={nusWrite != null}, Notify={nusNotify != null}");

// Subscribe to notifications
int spRxCount = 0, nusRxCount = 0;
spNotify!.ValueChanged += (s, e) =>
{
    spRxCount++;
    var reader = DataReader.FromBuffer(e.CharacteristicValue);
    var bytes = new byte[reader.UnconsumedBufferLength];
    reader.ReadBytes(bytes);
    Console.WriteLine($"  >>> SP RX #{spRxCount} ({bytes.Length}B): {BitConverter.ToString(bytes)}");
};
nusNotify!.ValueChanged += (s, e) =>
{
    nusRxCount++;
    var reader = DataReader.FromBuffer(e.CharacteristicValue);
    var bytes = new byte[reader.UnconsumedBufferLength];
    reader.ReadBytes(bytes);
    Console.WriteLine($"  >>> NUS RX #{nusRxCount} ({bytes.Length}B): {BitConverter.ToString(bytes)}");
};

// Enable notifications (retry up to 5 times for Serial Port)
Console.WriteLine("\n[2] Enabling notifications...");
GattCommunicationStatus spCccd = GattCommunicationStatus.Unreachable;
for (int attempt = 1; attempt <= 5 && spCccd != GattCommunicationStatus.Success; attempt++)
{
    spCccd = await spNotify.WriteClientCharacteristicConfigurationDescriptorAsync(
        GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask();
    Console.WriteLine($"  Serial Port notify (attempt {attempt}): {spCccd}");
    if (spCccd != GattCommunicationStatus.Success) await Task.Delay(1000);
}
var nusCccd = await nusNotify.WriteClientCharacteristicConfigurationDescriptorAsync(
    GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask();
Console.WriteLine($"  NUS notify: {nusCccd}");

await Task.Delay(1000); // Let device settle

// Helper to write to a characteristic
async Task WriteCmd(GattCharacteristic ch, byte[] data, string label, bool withResponse = false)
{
    Console.WriteLine($"\n[TX] {label}: {BitConverter.ToString(data)}");
    var writer = new DataWriter();
    writer.WriteBytes(data);
    var opt = withResponse ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
    var result = await ch.WriteValueAsync(writer.DetachBuffer(), opt).AsTask();
    Console.WriteLine($"  Result: {result}");
}

// === Serial Port Protocol (0xBC framing) on de5bf72a ===
Console.WriteLine("\n=== TEST: LE-length + WriteWithResponse ===");

// Heartbeat
await WriteCmd(spWrite!, BuildFrame(0x45, Array.Empty<byte>()), "Heartbeat", withResponse: true);
await Task.Delay(3000);
Console.WriteLine($"  SP RX={spRxCount}, NUS RX={nusRxCount}");

// Battery (action=0x42, payload=[0x00, 0x00]) — now LE length
await WriteCmd(spWrite!, BuildFrame(0x42, new byte[] { 0x00, 0x00 }), "Battery (LE len)", withResponse: true);
await Task.Delay(5000);
Console.WriteLine($"  SP RX={spRxCount}, NUS RX={nusRxCount}");

// Battery with single byte payload
await WriteCmd(spWrite!, BuildFrame(0x42, new byte[] { 0x00 }), "Battery (1B payload)", withResponse: true);
await Task.Delay(5000);
Console.WriteLine($"  SP RX={spRxCount}, NUS RX={nusRxCount}");

// Battery with no payload
await WriteCmd(spWrite!, BuildFrame(0x42, Array.Empty<byte>()), "Battery (no payload)", withResponse: true);
await Task.Delay(5000);
Console.WriteLine($"  SP RX={spRxCount}, NUS RX={nusRxCount}");

// SyncTime (action=0x40, 4-byte unix timestamp)
var nowUtc = DateTimeOffset.UtcNow;
var timePayload = new byte[4];
System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(timePayload, (int)nowUtc.ToUnixTimeSeconds());
await WriteCmd(spWrite!, BuildFrame(0x40, timePayload), "SyncTime", withResponse: true);
await Task.Delay(5000);
Console.WriteLine($"  SP RX={spRxCount}, NUS RX={nusRxCount}");

// BT Connect (action=0x49)
await WriteCmd(spWrite!, BuildFrame(0x49, Array.Empty<byte>()), "BT Connect", withResponse: true);
await Task.Delay(5000);
Console.WriteLine($"  SP RX={spRxCount}, NUS RX={nusRxCount}");

// DeviceInfo again to confirm still alive
await WriteCmd(spWrite!, BuildFrame(0x43, Array.Empty<byte>()), "DeviceInfo", withResponse: true);
await Task.Delay(5000);
Console.WriteLine($"  SP RX={spRxCount}, NUS RX={nusRxCount}");

// Final
Console.WriteLine($"\n=== FINAL: SP RX={spRxCount}, NUS RX={nusRxCount} ===");

bleDevice.Dispose();
