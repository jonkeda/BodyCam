using System.Buffers.Binary;
using System.Text;

namespace BodyCam.Services.Glasses.HeyCyan;

internal static class HeyCyanDirectBleProtocol
{
    public const int HeaderLength = 6;
    public const int DefaultWriteChunkLength = 244;

    public static bool TryDecodeFrame(
        ReadOnlySpan<byte> frame,
        out byte action,
        out ReadOnlySpan<byte> payload)
    {
        action = 0;
        payload = default;

        if (frame.Length < HeaderLength || frame[0] != 0xBC)
            return false;

        var declaredLength = frame[2] | (frame[3] << 8);
        if (frame.Length - HeaderLength < declaredLength)
            return false;

        action = frame[1];
        payload = frame.Slice(HeaderLength, declaredLength);
        return true;
    }

    public static byte DecodeOutgoingAction(ReadOnlySpan<byte> request)
    {
        if (TryDecodeFrame(request, out var action, out _))
            return action;

        return HeyCyanCommands.ActionGlassesControl;
    }

    public static byte[] BuildSessionPayload(ReadOnlySpan<byte> frame)
    {
        if (!TryDecodeFrame(frame, out var action, out var payload))
            return frame.ToArray();

        return action switch
        {
            HeyCyanCommands.ActionBattery => BuildBatteryPayload(frame),
            HeyCyanCommands.ActionDeviceInfo => BuildDeviceInfoPayload(frame),
            HeyCyanCommands.ActionGlassesControl => BuildGlassesControlPayload(frame, payload),
            _ => payload.ToArray()
        };
    }

    public static bool TryBuildRawNotify(ReadOnlySpan<byte> frame, out byte[] loadData)
    {
        loadData = [];

        if (!TryDecodeFrame(frame, out var action, out var payload))
            return false;

        if (action == 0x73)
        {
            loadData = frame.ToArray();
            return true;
        }

        if (action == HeyCyanCommands.ActionGlassesControl
            && payload.Length >= 5
            && payload[1] == 0x03)
        {
            var ip = TryFindIpv4(payload);
            if (ip is null && payload.Length >= 6)
            {
                ip = [192, 168, payload[4], payload[5]];
            }

            if (ip is not null)
            {
                loadData = new byte[11];
                loadData[6] = 0x08;
                ip.CopyTo(loadData.AsSpan(7));
                return true;
            }
        }

        if (action == HeyCyanCommands.ActionGlassesControl
            && payload.Length >= 2
            && payload[1] == 0x09)
        {
            loadData = new byte[8];
            loadData[6] = 0x09;
            loadData[7] = payload.Length >= 3 ? payload[2] : (byte)0;
            return true;
        }

        return false;
    }

    public static bool TryBuildButtonEvent(ReadOnlySpan<byte> loadData, out HeyCyanButtonGesture gesture)
    {
        gesture = default;
        if (loadData.Length < 8)
            return false;

        switch (loadData[6])
        {
            case 0x02:
                gesture = HeyCyanButtonGesture.Tap;
                return true;
            case 0x03:
                gesture = HeyCyanButtonGesture.DoubleTap;
                return true;
            default:
                return false;
        }
    }

    public static bool TryExtractCompleteFrame(
        List<byte> buffer,
        out byte[] frame,
        DateTimeOffset now,
        ref DateTimeOffset? startedAt)
    {
        frame = [];

        if (buffer.Count == 0)
        {
            startedAt = null;
            return false;
        }

        var start = buffer.IndexOf(0xBC);
        if (start < 0)
        {
            buffer.Clear();
            startedAt = null;
            return false;
        }

        if (start > 0)
            buffer.RemoveRange(0, start);

        startedAt ??= now;
        if (now - startedAt.Value > TimeSpan.FromSeconds(5) || buffer.Count > 2048)
        {
            buffer.Clear();
            startedAt = null;
            return false;
        }

        if (buffer.Count < HeaderLength)
            return false;

        var length = buffer[2] | (buffer[3] << 8);
        var total = HeaderLength + length;
        if (total > 2048)
        {
            buffer.Clear();
            startedAt = null;
            return false;
        }

        if (buffer.Count < total)
            return false;

        frame = buffer.GetRange(0, total).ToArray();
        buffer.RemoveRange(0, total);
        startedAt = buffer.Count == 0 ? null : now;
        return true;
    }

    private static byte[] BuildBatteryPayload(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 8)
            return [];

        return [frame[6], frame[7]];
    }

    private static byte[] BuildDeviceInfoPayload(ReadOnlySpan<byte> frame)
    {
        try
        {
            if (frame.Length < 15)
                return [];

            var firmwareLength = ReadUInt16(frame, 7);
            var hardwareLength = ReadUInt16(frame, 9);
            var wifiFirmwareLength = ReadUInt16(frame, 11);
            var wifiHardwareLength = ReadUInt16(frame, 13);
            var offset = 15;

            var firmware = ReadUtf8(frame, ref offset, firmwareLength);
            var hardware = ReadUtf8(frame, ref offset, hardwareLength);
            var wifiFirmware = ReadUtf8(frame, ref offset, wifiFirmwareLength);
            var wifiHardware = ReadUtf8(frame, ref offset, wifiHardwareLength);

            return Encoding.UTF8.GetBytes(string.Join(',',
                hardware,
                firmware,
                wifiHardware,
                wifiFirmware,
                "00:00:00:00:00:00"));
        }
        catch
        {
            return [];
        }
    }

    private static byte[] BuildGlassesControlPayload(ReadOnlySpan<byte> frame, ReadOnlySpan<byte> payload)
    {
        if (payload.Length >= 8 && payload[1] == 0x04)
        {
            var response = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(0, 4), ReadUInt16(frame, 8));
            BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(4, 4), ReadUInt16(frame, 10));
            BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(8, 4), ReadUInt16(frame, 12));
            return response;
        }

        return payload.ToArray();
    }

    private static byte[]? TryFindIpv4(ReadOnlySpan<byte> payload)
    {
        for (var i = 0; i <= payload.Length - 4; i++)
        {
            if (payload[i] == 192 && payload[i + 1] == 168)
                return payload.Slice(i, 4).ToArray();
        }

        return null;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        if (data.Length < offset + 2)
            return 0;

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    }

    private static string ReadUtf8(ReadOnlySpan<byte> data, ref int offset, int length)
    {
        if (length <= 0 || data.Length < offset + length)
            return "unknown";

        var value = Encoding.UTF8.GetString(data.Slice(offset, length)).TrimEnd('\0');
        offset += length;
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }
}
