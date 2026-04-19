using System.Runtime.InteropServices;

namespace BodyCam.Services.Audio.WebRtcApm;

/// <summary>
/// Minimal P/Invoke wrapper for the WebRTC Audio Processing Module native library.
/// Only includes functions needed for AEC, noise suppression, and gain control.
/// Native binaries sourced from SoundFlow.Extensions.WebRtc.Apm v1.4.0 (MIT + BSD-3-Clause).
/// </summary>
internal static class WebRtcApmInterop
{
    private const string Lib = "webrtc-apm";

    // ── APM lifecycle ──────────────────────────────────────────────────

    [DllImport(Lib, EntryPoint = "webrtc_apm_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Create();

    [DllImport(Lib, EntryPoint = "webrtc_apm_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Destroy(IntPtr apm);

    [DllImport(Lib, EntryPoint = "webrtc_apm_initialize", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Initialize(IntPtr apm);

    // ── Config lifecycle ───────────────────────────────────────────────

    [DllImport(Lib, EntryPoint = "webrtc_apm_config_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ConfigCreate();

    [DllImport(Lib, EntryPoint = "webrtc_apm_config_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ConfigDestroy(IntPtr config);

    [DllImport(Lib, EntryPoint = "webrtc_apm_apply_config", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ConfigApply(IntPtr apm, IntPtr config);

    // ── Config setters ─────────────────────────────────────────────────

    [DllImport(Lib, EntryPoint = "webrtc_apm_config_set_echo_canceller", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ConfigSetEchoCanceller(IntPtr config, int enabled, int mobileMode);

    [DllImport(Lib, EntryPoint = "webrtc_apm_config_set_noise_suppression", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ConfigSetNoiseSuppression(IntPtr config, int enabled, int level);

    [DllImport(Lib, EntryPoint = "webrtc_apm_config_set_gain_controller1", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ConfigSetGainController1(
        IntPtr config, int enabled, int mode,
        int targetLevelDbfs, int compressionGainDb, int enableLimiter);

    [DllImport(Lib, EntryPoint = "webrtc_apm_config_set_high_pass_filter", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ConfigSetHighPassFilter(IntPtr config, int enabled);

    // ── StreamConfig lifecycle ─────────────────────────────────────────

    [DllImport(Lib, EntryPoint = "webrtc_apm_stream_config_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr StreamConfigCreate(int sampleRateHz, nuint numChannels);

    [DllImport(Lib, EntryPoint = "webrtc_apm_stream_config_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void StreamConfigDestroy(IntPtr config);

    // ── Processing ─────────────────────────────────────────────────────

    /// <summary>
    /// Process a capture (microphone) audio frame.
    /// src/dest: pointer to array of float* (one per channel). For mono: float*[1].
    /// </summary>
    [DllImport(Lib, EntryPoint = "webrtc_apm_process_stream", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ProcessStream(
        IntPtr apm, IntPtr src, IntPtr inputConfig, IntPtr outputConfig, IntPtr dest);

    /// <summary>
    /// Process a reverse (speaker/render) audio frame — feeds the AEC reference signal.
    /// </summary>
    [DllImport(Lib, EntryPoint = "webrtc_apm_process_reverse_stream", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ProcessReverseStream(
        IntPtr apm, IntPtr src, IntPtr inputConfig, IntPtr outputConfig, IntPtr dest);

    // ── Stream delay ───────────────────────────────────────────────────

    [DllImport(Lib, EntryPoint = "webrtc_apm_set_stream_delay_ms", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SetStreamDelayMs(IntPtr apm, int delayMs);

    // ── Utilities ──────────────────────────────────────────────────────

    [DllImport(Lib, EntryPoint = "webrtc_apm_get_frame_size", CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint GetFrameSize(int sampleRateHz);
}
