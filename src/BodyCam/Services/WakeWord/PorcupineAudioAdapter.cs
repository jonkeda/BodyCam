namespace BodyCam.Services.WakeWord;

/// <summary>
/// Resamples PCM16 audio from 24kHz to 16kHz and buffers into
/// exact frame-length chunks required by Porcupine.
/// </summary>
public sealed class PorcupineAudioAdapter
{
    private readonly int _frameLength;
    private readonly short[] _frameBuffer;
    private int _bufferOffset;

    // Resample state: 24kHz → 16kHz = 3:2 ratio.
    // We pick every 2 out of 3 samples (linear interpolation).
    private int _srcOffset; // tracks position within the 3-sample cycle

    public PorcupineAudioAdapter(int frameLength)
    {
        _frameLength = frameLength;
        _frameBuffer = new short[frameLength];
    }

    /// <summary>
    /// Feed PCM16 24kHz mono audio bytes. Yields complete 16kHz frames
    /// of exactly <see cref="_frameLength"/> samples for Porcupine.
    /// </summary>
    public IEnumerable<short[]> Process(byte[] pcm24kHz)
    {
        // Convert bytes to shorts (2 bytes per sample, little-endian)
        int sampleCount = pcm24kHz.Length / 2;

        for (int i = 0; i < sampleCount; i++)
        {
            int byteIdx = i * 2;
            short sample = (short)(pcm24kHz[byteIdx] | (pcm24kHz[byteIdx + 1] << 8));

            // 3:2 decimation — keep samples at positions 0 and 1, skip position 2
            if (_srcOffset < 2)
            {
                _frameBuffer[_bufferOffset++] = sample;

                if (_bufferOffset >= _frameLength)
                {
                    var frame = new short[_frameLength];
                    Array.Copy(_frameBuffer, frame, _frameLength);
                    _bufferOffset = 0;
                    yield return frame;
                }
            }

            _srcOffset = (_srcOffset + 1) % 3;
        }
    }

    /// <summary>
    /// Resets internal buffers. Call when stopping/restarting detection.
    /// </summary>
    public void Reset()
    {
        _bufferOffset = 0;
        _srcOffset = 0;
        Array.Clear(_frameBuffer);
    }
}
