using System.Text;

namespace BodyCam.Services.Audio;

/// <summary>
/// Phase 6.3: Records a sliding window of microphone audio for A/B testing and regression analysis.
/// Keeps the last N seconds in memory, saves to WAV on demand.
/// </summary>
public sealed class MicCaptureRecorder
{
    private readonly int _sampleRate;
    private readonly int _maxSamples;
    private readonly Queue<byte[]> _buffer = new();
    private long _bufferedSamples;
    private readonly object _lock = new();

    public MicCaptureRecorder(int sampleRate, int seconds = 10)
    {
        _sampleRate = sampleRate;
        _maxSamples = sampleRate * seconds;
    }

    public void RecordChunk(byte[] pcm16)
    {
        lock (_lock)
        {
            _buffer.Enqueue(pcm16);
            _bufferedSamples += pcm16.Length / 2;
            while (_bufferedSamples > _maxSamples && _buffer.Count > 0)
                _bufferedSamples -= _buffer.Dequeue().Length / 2;
        }
    }

    public void SaveToWav(string path)
    {
        lock (_lock)
        {
            if (_buffer.Count == 0)
                throw new InvalidOperationException("No audio data to save.");

            // Flatten buffer
            int totalBytes = _buffer.Sum(c => c.Length);
            byte[] data = new byte[totalBytes];
            int offset = 0;
            foreach (var chunk in _buffer)
            {
                Array.Copy(chunk, 0, data, offset, chunk.Length);
                offset += chunk.Length;
            }

            // Write RIFF/WAVE header
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs, Encoding.ASCII);

            // RIFF header
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + totalBytes); // ChunkSize
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt subchunk
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16); // Subchunk1Size (PCM)
            bw.Write((short)1); // AudioFormat (PCM)
            bw.Write((short)1); // NumChannels (mono)
            bw.Write(_sampleRate);
            bw.Write(_sampleRate * 2); // ByteRate (SampleRate * NumChannels * BitsPerSample/8)
            bw.Write((short)2); // BlockAlign (NumChannels * BitsPerSample/8)
            bw.Write((short)16); // BitsPerSample

            // data subchunk
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(totalBytes);
            bw.Write(data);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _buffer.Clear();
            _bufferedSamples = 0;
        }
    }
}
