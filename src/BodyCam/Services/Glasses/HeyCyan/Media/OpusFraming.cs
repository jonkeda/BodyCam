namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Framing format of raw OPUS byte streams from HeyCyan glasses recordings.
/// </summary>
public enum OpusFraming
{
    /// <summary>
    /// Already a valid Ogg/Opus container (starts with "OggS" magic).
    /// </summary>
    OggContainer,

    /// <summary>
    /// Fixed 40-byte raw OPUS packets (HeyCyan firmware default).
    /// </summary>
    FixedPacket40,

    /// <summary>
    /// Length-prefixed packets: u16 little-endian length followed by packet data.
    /// </summary>
    LengthPrefixedU16Le,

    /// <summary>
    /// Length-prefixed packets: u16 big-endian length followed by packet data.
    /// </summary>
    LengthPrefixedU16Be,

    /// <summary>
    /// Length-prefixed packets: u8 length followed by packet data.
    /// </summary>
    LengthPrefixedU8,

    /// <summary>
    /// Cannot determine framing; will default to <see cref="FixedPacket40"/>.
    /// </summary>
    Unknown
}
