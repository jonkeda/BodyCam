using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace BodyCam.Services.Camera.A9.Vue990;

public sealed class A9Vue990DasServerParameter
{
    public const string DasPrefix = "DAS-";

    public const string KnownMagicHex = "8ED76A3380D998ECDA94D6D805A36877";

    private static readonly HashSet<int> KnownEndpointPorts =
    [
        80,
        81,
        443,
        3478,
        15203,
        20190,
        32108,
    ];

    private static readonly int[] NativeKeyZeroUpdateLengths = [13, 19, 5, 14, 10];

    private static readonly int[] NativeIvZeroUpdateLengths = [3, 14, 17, 20, 24];

    public required string Original { get; init; }

    public string Prefix => DasPrefix.TrimEnd('-');

    public required string HexPayload { get; init; }

    [JsonIgnore]
    public byte[] Bytes { get; init; } = [];

    public int ByteLength => Bytes.Length;

    public string MagicHex => ByteLength >= 16
        ? Convert.ToHexString(Bytes.AsSpan(0, 16))
        : Convert.ToHexString(Bytes);

    public bool HasKnownMagic => ByteLength >= 16 &&
                                 MagicHex.Equals(KnownMagicHex, StringComparison.OrdinalIgnoreCase);

    public bool IsKnownLength => ByteLength is 80 or 96;

    public bool LooksPlainText { get; init; }

    public double EntropyBitsPerByte { get; init; }

    public required string PrintableAsciiPreview { get; init; }

    public required string PayloadHexPreview { get; init; }

    public IReadOnlyList<A9Vue990DasEndpointCandidate> CandidateIpv4Endpoints { get; init; } = [];

    public A9Vue990DasDecodedPayload DecodedPayload { get; init; } =
        A9Vue990DasDecodedPayload.NotAttempted();

    public bool HasDecodedPayload => DecodedPayload.Success;

    public static bool TryParse(
        string? value,
        out A9Vue990DasServerParameter? parameter,
        out string? error)
    {
        parameter = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "DAS server parameter is empty.";
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith(DasPrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "DAS server parameter must start with DAS-.";
            return false;
        }

        var hex = trimmed[DasPrefix.Length..].Trim();
        if (hex.Length == 0)
        {
            error = "DAS server parameter has no hex payload.";
            return false;
        }

        if (hex.Length % 2 != 0)
        {
            error = "DAS server parameter hex payload has an odd length.";
            return false;
        }

        if (!hex.All(IsHexDigit))
        {
            error = "DAS server parameter contains non-hex payload characters.";
            return false;
        }

        var bytes = Convert.FromHexString(hex);
        var normalizedHex = Convert.ToHexString(bytes);
        parameter = new A9Vue990DasServerParameter
        {
            Original = trimmed,
            HexPayload = normalizedHex,
            Bytes = bytes,
            LooksPlainText = LooksLikePlainText(bytes),
            EntropyBitsPerByte = CalculateEntropy(bytes),
            PrintableAsciiPreview = BuildAsciiPreview(bytes),
            PayloadHexPreview = PreviewHex(normalizedHex, 96),
            CandidateIpv4Endpoints = FindEndpointCandidates(bytes),
            DecodedPayload = DecodeNativeDasPayload(bytes),
        };
        return true;
    }

    public static string EncodeDecodedPayload(ReadOnlySpan<byte> decodedPayload)
    {
        if (decodedPayload.IsEmpty)
            throw new ArgumentException("Decoded DAS payload is required.", nameof(decodedPayload));

        try
        {
            var plainBytes = PadTrailingZerosToAesBlock(decodedPayload);
            var keyAscii = DeriveNativeCryptoAsciiBytes(NativeKeyZeroUpdateLengths);
            var ivAscii = DeriveNativeCryptoAsciiBytes(NativeIvZeroUpdateLengths);

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = keyAscii;
            aes.IV = ivAscii;

            using var encryptor = aes.CreateEncryptor();
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            return DasPrefix + Convert.ToHexString(cipherBytes);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Native DAS encode failed.", ex);
        }
    }

    public string ToReadableString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("A9 Vue990 DAS server parameter");
        sb.AppendLine($"Prefix: {Prefix}");
        sb.AppendLine($"Payload: hexChars={HexPayload.Length} bytes={ByteLength} knownLength={IsKnownLength}");
        sb.AppendLine($"Magic: {MagicHex} knownMagic={HasKnownMagic}");
        sb.AppendLine($"Entropy: {EntropyBitsPerByte:F2} bits/byte");
        sb.AppendLine($"Plaintext-looking: {LooksPlainText}");
        sb.AppendLine($"ASCII preview: {PrintableAsciiPreview}");
        sb.AppendLine($"Hex preview: {PayloadHexPreview}");
        sb.AppendLine($"Decoded payload: success={DecodedPayload.Success}");
        if (DecodedPayload.Success)
        {
            sb.AppendLine($"Decoded ASCII: {DecodedPayload.PrintableAscii}");
            sb.AppendLine($"Decoded relay hosts: {string.Join(", ", DecodedPayload.RelayHosts.DefaultIfEmpty("<none>"))}");
            sb.AppendLine($"Decoded tokens: {string.Join(" | ", DecodedPayload.Tokens)}");
            if (DecodedPayload.ConnectDescriptor is { } descriptor)
            {
                sb.AppendLine(
                    "Decoded connect descriptor: " +
                    $"nativeShape={descriptor.HasNativeConnectByServerShape} " +
                    $"mode={descriptor.ModeToken?.EscapedAscii ?? "<none>"} " +
                    $"relayName={descriptor.RelayName} selector={descriptor.Selector}");
                foreach (var token in descriptor.Tokens)
                {
                    sb.AppendLine(
                        $"  token[{token.Index}]: bytes={token.ByteLength} " +
                        $"ascii={token.EscapedAscii} hex={token.Hex}");
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(DecodedPayload.Error))
        {
            sb.AppendLine($"Decoded error: {DecodedPayload.Error}");
        }

        if (CandidateIpv4Endpoints.Count == 0)
        {
            sb.AppendLine("Endpoint candidates: none from common-port heuristic");
        }
        else
        {
            sb.AppendLine("Endpoint candidates: heuristic only");
            foreach (var candidate in CandidateIpv4Endpoints)
            {
                sb.AppendLine(
                    $"- offset={candidate.Offset} address={candidate.Address} " +
                    $"bePort={candidate.BigEndianPortAfter} lePort={candidate.LittleEndianPortAfter} " +
                    $"match={candidate.Match}");
            }
        }

        return sb.ToString();
    }

    private static A9Vue990DasDecodedPayload DecodeNativeDasPayload(byte[] bytes)
    {
        if (bytes.Length == 0)
            return A9Vue990DasDecodedPayload.NotAttempted("DAS payload is empty.");

        if (bytes.Length % 16 != 0)
            return A9Vue990DasDecodedPayload.NotAttempted("DAS payload is not aligned to AES blocks.");

        try
        {
            var keyAscii = DeriveNativeCryptoAsciiBytes(NativeKeyZeroUpdateLengths);
            var ivAscii = DeriveNativeCryptoAsciiBytes(NativeIvZeroUpdateLengths);

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = keyAscii;
            aes.IV = ivAscii;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(bytes, 0, bytes.Length);
            var trimmedBytes = TrimTrailingZeros(plainBytes);
            var tokenBytes = SplitByteTokens(trimmedBytes, (byte)',');
            var tokens = tokenBytes
                .Select(token => EscapeAscii(token))
                .ToArray();

            return new A9Vue990DasDecodedPayload
            {
                Success = true,
                NativeCrypto = "AES-CBC/no-padding; key=ASCII first 16 chars of uppercase MD5(61 zero bytes); iv=ASCII first 16 chars of uppercase MD5(78 zero bytes)",
                KeyAscii = Encoding.ASCII.GetString(keyAscii),
                IvAscii = Encoding.ASCII.GetString(ivAscii),
                PlainByteLength = trimmedBytes.Length,
                PlainBytes = trimmedBytes,
                PrintableAscii = EscapeAscii(trimmedBytes),
                PlainHexPreview = PreviewHex(Convert.ToHexString(trimmedBytes), 128),
                Tokens = tokens,
                ConnectDescriptor = BuildConnectDescriptor(tokenBytes),
                RelayHosts = FindDecodedRelayHosts(tokenBytes),
            };
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return A9Vue990DasDecodedPayload.NotAttempted(
                $"Native DAS decrypt failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static byte[] DeriveNativeCryptoAsciiBytes(IReadOnlyCollection<int> zeroUpdateLengths)
    {
        var zeroBytes = new byte[zeroUpdateLengths.Sum()];
        var md5Hex = Convert.ToHexString(MD5.HashData(zeroBytes));
        return Encoding.ASCII.GetBytes(md5Hex[..16]);
    }

    private static byte[] TrimTrailingZeros(byte[] bytes)
    {
        var length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
            length--;

        return bytes[..length];
    }

    private static byte[] PadTrailingZerosToAesBlock(ReadOnlySpan<byte> bytes)
    {
        var length = bytes.Length;
        var paddedLength = ((length + 15) / 16) * 16;
        var padded = new byte[paddedLength];
        bytes.CopyTo(padded);
        return padded;
    }

    private static IReadOnlyList<byte[]> SplitByteTokens(byte[] bytes, byte delimiter)
    {
        var tokens = new List<byte[]>();
        var start = 0;
        for (var i = 0; i <= bytes.Length; i++)
        {
            if (i != bytes.Length && bytes[i] != delimiter)
                continue;

            tokens.Add(bytes[start..i]);
            start = i + 1;
        }

        return tokens;
    }

    private static A9Vue990DasConnectDescriptor BuildConnectDescriptor(IReadOnlyList<byte[]> tokenBytes)
    {
        var tokens = tokenBytes
            .Select((token, index) => new A9Vue990DasToken
            {
                Index = index,
                Bytes = token,
                EscapedAscii = EscapeAscii(token),
                Hex = Convert.ToHexString(token),
                IsPrintableAscii = LooksLikePlainText(token),
            })
            .ToArray();
        IReadOnlyList<string> relayHosts = tokenBytes.Count > 2
            ? FindDecodedRelayHosts([tokenBytes[2]])
            : [];
        IReadOnlyList<string> modeParts = tokenBytes.Count > 1
            ? Encoding.ASCII.GetString(tokenBytes[1])
                .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        return new A9Vue990DasConnectDescriptor
        {
            Tokens = tokens,
            OpaqueToken = tokens.ElementAtOrDefault(0),
            ModeToken = tokens.ElementAtOrDefault(1),
            RelayHostToken = tokens.ElementAtOrDefault(2),
            RelayNameToken = tokens.ElementAtOrDefault(3),
            SelectorToken = tokens.ElementAtOrDefault(4),
            ModeParts = modeParts,
            RelayHosts = relayHosts,
        };
    }

    private static IReadOnlyList<string> FindDecodedRelayHosts(IReadOnlyList<byte[]> tokenBytes)
    {
        var hosts = new List<string>();
        foreach (var token in tokenBytes)
        {
            var text = Encoding.ASCII.GetString(token);
            foreach (var part in text.Split(['-', '+', '|', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!IPAddress.TryParse(part.Trim(), out var address) ||
                    address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                hosts.Add(address.ToString());
            }
        }

        return hosts.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string EscapeAscii(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var value in bytes)
        {
            switch (value)
            {
                case 0:
                    sb.Append(@"\0");
                    break;
                case 9:
                    sb.Append(@"\t");
                    break;
                case 10:
                    sb.Append(@"\n");
                    break;
                case 13:
                    sb.Append(@"\r");
                    break;
                case >= 32 and <= 126:
                    sb.Append((char)value);
                    break;
                default:
                    sb.Append(@"\x");
                    sb.Append(value.ToString("X2"));
                    break;
            }
        }

        return sb.ToString();
    }

    private static bool IsHexDigit(char value)
    {
        return value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static bool LooksLikePlainText(byte[] bytes)
    {
        if (bytes.Length == 0)
            return false;

        var printable = bytes.Count(value =>
            value is 9 or 10 or 13 ||
            value is >= 32 and <= 126);
        return printable >= bytes.Length * 0.85;
    }

    private static double CalculateEntropy(byte[] bytes)
    {
        if (bytes.Length == 0)
            return 0;

        var counts = new int[256];
        foreach (var value in bytes)
            counts[value]++;

        double entropy = 0;
        foreach (var count in counts)
        {
            if (count == 0)
                continue;

            var probability = count / (double)bytes.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    private static string BuildAsciiPreview(byte[] bytes)
    {
        if (bytes.Length == 0)
            return "<empty>";

        var sb = new StringBuilder();
        foreach (var value in bytes.AsSpan(0, Math.Min(bytes.Length, 96)))
        {
            sb.Append(value is >= 32 and <= 126 ? (char)value : '.');
        }

        if (bytes.Length > 96)
            sb.Append("...");

        return sb.ToString();
    }

    private static string PreviewHex(string hex, int maxChars)
    {
        if (hex.Length <= maxChars)
            return hex;

        return hex[..maxChars] + "...";
    }

    private static IReadOnlyList<A9Vue990DasEndpointCandidate> FindEndpointCandidates(byte[] bytes)
    {
        var candidates = new List<A9Vue990DasEndpointCandidate>();
        for (var offset = 0; offset + 5 < bytes.Length; offset++)
        {
            var addressBytes = bytes.AsSpan(offset, 4);
            if (!LooksLikeIpv4Address(addressBytes))
                continue;

            var bigEndianPort = ReadUInt16BigEndian(bytes.AsSpan(offset + 4, 2));
            var littleEndianPort = ReadUInt16LittleEndian(bytes.AsSpan(offset + 4, 2));
            var bigEndianHit = KnownEndpointPorts.Contains(bigEndianPort);
            var littleEndianHit = KnownEndpointPorts.Contains(littleEndianPort);
            if (!bigEndianHit && !littleEndianHit)
                continue;

            candidates.Add(new A9Vue990DasEndpointCandidate
            {
                Offset = offset,
                Address = new IPAddress(addressBytes).ToString(),
                BigEndianPortAfter = bigEndianPort,
                LittleEndianPortAfter = littleEndianPort,
                Match = string.Join(", ", new[]
                {
                    bigEndianHit ? $"big-endian:{bigEndianPort}" : null,
                    littleEndianHit ? $"little-endian:{littleEndianPort}" : null,
                }.Where(item => item is not null)),
            });
        }

        return candidates;
    }

    private static bool LooksLikeIpv4Address(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
            return false;

        var first = bytes[0];
        if (first is 0 or 127 or >= 224)
            return false;

        if (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255)
            return false;

        if (bytes[0] == 169 && bytes[1] == 254)
            return false;

        return true;
    }

    private static int ReadUInt16BigEndian(ReadOnlySpan<byte> bytes)
    {
        return (bytes[0] << 8) | bytes[1];
    }

    private static int ReadUInt16LittleEndian(ReadOnlySpan<byte> bytes)
    {
        return bytes[0] | (bytes[1] << 8);
    }
}

public sealed class A9Vue990DasEndpointCandidate
{
    public int Offset { get; init; }

    public required string Address { get; init; }

    public int BigEndianPortAfter { get; init; }

    public int LittleEndianPortAfter { get; init; }

    public required string Match { get; init; }
}

public sealed class A9Vue990DasDecodedPayload
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string NativeCrypto { get; init; } = string.Empty;

    public string KeyAscii { get; init; } = string.Empty;

    public string IvAscii { get; init; } = string.Empty;

    public int PlainByteLength { get; init; }

    [JsonIgnore]
    public byte[] PlainBytes { get; init; } = [];

    public string PrintableAscii { get; init; } = string.Empty;

    public string PlainHexPreview { get; init; } = string.Empty;

    public IReadOnlyList<string> Tokens { get; init; } = [];

    public A9Vue990DasConnectDescriptor? ConnectDescriptor { get; init; }

    public IReadOnlyList<string> RelayHosts { get; init; } = [];

    public static A9Vue990DasDecodedPayload NotAttempted(string? error = null)
    {
        return new A9Vue990DasDecodedPayload
        {
            Success = false,
            Error = error ?? "Not attempted.",
        };
    }
}

public sealed class A9Vue990DasConnectDescriptor
{
    public IReadOnlyList<A9Vue990DasToken> Tokens { get; init; } = [];

    public A9Vue990DasToken? OpaqueToken { get; init; }

    public A9Vue990DasToken? ModeToken { get; init; }

    public A9Vue990DasToken? RelayHostToken { get; init; }

    public A9Vue990DasToken? RelayNameToken { get; init; }

    public A9Vue990DasToken? SelectorToken { get; init; }

    public IReadOnlyList<string> ModeParts { get; init; } = [];

    public IReadOnlyList<string> RelayHosts { get; init; } = [];

    public string RelayName => RelayNameToken?.EscapedAscii ?? string.Empty;

    public string Selector => SelectorToken?.EscapedAscii ?? string.Empty;

    public bool HasNativeConnectByServerShape =>
        Tokens.Count >= 5 &&
        OpaqueToken is not null &&
        ModeToken is not null &&
        RelayHosts.Count > 0 &&
        !string.IsNullOrWhiteSpace(RelayName) &&
        !string.IsNullOrWhiteSpace(Selector);
}

public sealed class A9Vue990DasToken
{
    public int Index { get; init; }

    [JsonIgnore]
    public byte[] Bytes { get; init; } = [];

    public int ByteLength => Bytes.Length;

    public string EscapedAscii { get; init; } = string.Empty;

    public string Hex { get; init; } = string.Empty;

    public bool IsPrintableAscii { get; init; }
}
