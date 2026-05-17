namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Read-only diagnostics for the negotiated Bluetooth audio codec configuration
/// between the HeyCyan glasses and the phone.
/// </summary>
/// <remarks>
/// <para><b>A2DP codec guarantee:</b> HeyCyan glasses are guaranteed to work over
/// <b>SBC</b> A2DP and <b>CVSD</b> HFP. AAC, aptX, aptX-HD, LDAC, and mSBC may be
/// negotiated but are not promised.</para>
/// <para><b>iOS limitation:</b> iOS does not expose negotiated codecs to third-party apps.
/// On iOS, all codec fields will be <c>null</c>/0.</para>
/// <para><b>Non-blocking:</b> If codec discovery fails or is unsupported, <c>Current</c>
/// will be <c>null</c>. The failure does not block routing or crash the app.</para>
/// </remarks>
public interface IHeyCyanAudioDiagnostics
{
    /// <summary>
    /// The current audio route info, populated when the glasses are connected.
    /// <c>null</c> if disconnected or if codec probe failed.
    /// </summary>
    HeyCyanAudioRouteInfo? Current { get; }

    /// <summary>
    /// Raised whenever <see cref="RefreshAsync"/> produces a non-null result.
    /// </summary>
    event EventHandler<HeyCyanAudioRouteInfo>? Updated;

    /// <summary>
    /// Manually refresh codec diagnostics. Called automatically when the
    /// glasses session transitions to <see cref="HeyCyanState.Connected"/>.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);
}

/// <summary>
/// Snapshot of the negotiated Bluetooth audio configuration for HeyCyan glasses.
/// </summary>
public sealed record HeyCyanAudioRouteInfo(
    string  InputProviderId,
    string  OutputProviderId,
    string? NegotiatedA2dpCodec, // "SBC" | "AAC" | "aptX" | "aptX-HD" | "LDAC" | null
    int     SampleRateHz,        // 0 if unknown
    int     Channels,            // 0 if unknown
    string? HfpCodec);           // "CVSD" | "mSBC" | null
