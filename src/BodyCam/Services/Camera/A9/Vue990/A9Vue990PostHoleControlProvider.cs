namespace BodyCam.Services.Camera.A9.Vue990;

public enum A9Vue990PostHoleControlKind
{
    InitialShortRequest = 0,
    InitialLongRequest = 1,
    MediaShortRequest = 2,
    MediaLongRequest = 3,
}

public sealed class A9Vue990PostHoleControl
{
    private readonly byte[] _bytes;

    internal A9Vue990PostHoleControl(
        int index,
        A9Vue990PostHoleControlKind kind,
        string name,
        string observedRole,
        string expectedResponse,
        byte[] bytes)
    {
        Index = index;
        Kind = kind;
        Name = name;
        ObservedRole = observedRole;
        ExpectedResponse = expectedResponse;
        _bytes = bytes;
    }

    public int Index { get; }
    public A9Vue990PostHoleControlKind Kind { get; }
    public string Name { get; }
    public string ObservedRole { get; }
    public string ExpectedResponse { get; }
    public byte[] Bytes => _bytes.ToArray();
    public int Length => _bytes.Length;

    public byte[] ToArray()
    {
        return _bytes.ToArray();
    }
}

public static class A9Vue990PostHoleControlProvider
{
    public const string Scope =
        "Scoped native-observed Vue990/BK7252N post-hole control vectors; runtime transport is C#, payload derivation is pending.";

    private static readonly A9Vue990PostHoleControl[] Controls =
    [
        new(
            0,
            A9Vue990PostHoleControlKind.InitialShortRequest,
            "initial-short-request",
            "First short direct command after compact alive.",
            "Small direct ACK/control packets before the large command response window.",
            Convert.FromHexString("0D00000000000000190000000101D849513379247067C33D93F1C5F7E7F1")),
        new(
            1,
            A9Vue990PostHoleControlKind.InitialLongRequest,
            "initial-long-request",
            "Long direct command sent early and repeated before the large response.",
            "830-byte direct command response after native-paced repeat.",
            Convert.FromHexString("0D000100000001006900000001013ADC34EF824B1EBCFD0F62894A437648F7CD098E9965DA5D4DEC28F0E7F5BC8039FECBD150AAFF645A740B75E28A27E96896DF074390ECFA8A6BDA8A8F34AFC0F40A5E6F970D9147F92B13BFBDC7995C508AC4C53D637F4ACF7158331D131500")),
        new(
            2,
            A9Vue990PostHoleControlKind.MediaShortRequest,
            "media-short-request",
            "Second short direct command in the media-open window.",
            "Direct ACK packet during media-open pacing.",
            Convert.FromHexString("0D00020000000200190000000101F3ED2B6770B6C8D1D56BCADCB230C185")),
        new(
            3,
            A9Vue990PostHoleControlKind.MediaLongRequest,
            "media-long-request",
            "Long media-open direct command sent before and after the 830-byte response.",
            "62-byte direct command response followed by the 55 AA 15 A8 media header.",
            Convert.FromHexString("0D00030000000300790000000101FE591DDEB1F29D527E8B8C089A154BEE39A4016299EE573F2E9016EFD24CAA8A3D613CE972546F392154E1B214F0008CC9848D6A2AA5A37BDECE4121284A44EF8F1A2DAC8CB1953C16EEE3565F7A7926EA958B7D290CCEF47C6E4078A6CE1C2E4178AB54DA0392B04351CDAB90B876BA")),
    ];

    public static IReadOnlyList<A9Vue990PostHoleControl> GetControls()
    {
        return Controls;
    }

    public static A9Vue990PostHoleControl GetControl(A9Vue990PostHoleControlKind kind)
    {
        var index = (int)kind;
        if (index < 0 || index >= Controls.Length || Controls[index].Kind != kind)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown post-hole control kind.");

        return Controls[index];
    }
}
