// ═══════════════════════════════════════════════════════════════════
//  AudioRecorder.cs — WASAPI Microphone Capture with Resampling
//  Records audio while push-to-talk key is held, then returns
//  16kHz mono float samples ready for the ASR model.
// ═══════════════════════════════════════════════════════════════════

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AsrService;

/// <summary>
/// Captures audio from the default microphone using WASAPI.
/// On stop, resamples the captured audio to 16kHz 16-bit mono
/// and returns float[] samples suitable for sherpa-onnx.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    // ── Constants ────────────────────────────────────────────────
    private const int TargetSampleRate = 16000;
    private const int TargetChannels   = 1;
    private const int TargetBitsPerSample = 16;

    // ── State ────────────────────────────────────────────────────
    private WasapiCapture? _capture;
    private MemoryStream? _rawBuffer;
    private readonly object _bufferLock = new();
    private WaveFormat? _captureFormat;
    private bool _isRecording = false;
    private bool _disposed = false;

    /// <summary>True while actively recording audio.</summary>
    public bool IsRecording => _isRecording;

    // ── Public API ───────────────────────────────────────────────

    /// <summary>Start capturing audio from the default microphone.</summary>
    public void StartRecording()
    {
        if (_isRecording) return;

        lock (_bufferLock)
        {
            _rawBuffer = new MemoryStream();
        }
        _capture = new WasapiCapture();
        _captureFormat = _capture.WaveFormat;

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        _isRecording = true;

        Console.WriteLine($"[Recorder] Recording started ({_captureFormat.SampleRate}Hz, " +
                          $"{_captureFormat.Channels}ch, {_captureFormat.BitsPerSample}bit)");
    }

    /// <summary>
    /// Stop recording and return the captured audio as 16kHz mono float samples.
    /// Returns null if no audio was captured.
    /// </summary>
    public float[]? StopRecording()
    {
        if (!_isRecording || _capture == null) return null;

        _capture.StopRecording();
        _isRecording = false;

        // Wait a moment for any remaining buffers to flush
        Thread.Sleep(50);

        float[]? samples;
        lock (_bufferLock)
        {
            samples = ProcessCapturedAudio();
            _rawBuffer?.Dispose();
            _rawBuffer = null;
        }

        // Cleanup capture resources
        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }
        if (samples != null)
        {
            float durationSec = (float)samples.Length / TargetSampleRate;
            Console.WriteLine($"[Recorder] Recording stopped. Duration: {durationSec:F2}s ({samples.Length} samples)");
        }
        else
        {
            Console.WriteLine("[Recorder] Recording stopped. No audio captured.");
        }

        return samples;
    }

    /// <summary>
    /// Resamples the current buffer, clears it, and returns the samples.
    /// Can be called while recording is active.
    /// </summary>
    public float[]? Flush()
    {
        lock (_bufferLock)
        {
            if (_rawBuffer == null || _rawBuffer.Length == 0) return null;

            var samples = ProcessCapturedAudio();
            _rawBuffer.SetLength(0); // clear the buffer
            return samples;
        }
    }

    // ── Audio Processing ─────────────────────────────────────────

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded > 0)
        {
            lock (_bufferLock)
            {
                if (_rawBuffer != null)
                {
                    _rawBuffer.Write(e.Buffer, 0, e.BytesRecorded);
                }
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Console.Error.WriteLine($"[Recorder] Recording error: {e.Exception.Message}");
        }
    }

    /// <summary>
    /// Takes the raw captured audio bytes, resamples to 16kHz mono,
    /// and converts to float[] in the range [-1.0, 1.0].
    /// </summary>
    private float[]? ProcessCapturedAudio()
    {
        if (_rawBuffer == null || _rawBuffer.Length == 0 || _captureFormat == null)
            return null;

        byte[] rawBytes = _rawBuffer.ToArray();

        // Create a wave provider from the raw captured bytes
        using var rawStream = new RawSourceWaveStream(
            new MemoryStream(rawBytes), _captureFormat);

        // Convert to the target format (16kHz, 16-bit, mono) using
        // the Media Foundation resampler (built into Windows).
        var targetFormat = new WaveFormat(TargetSampleRate, TargetBitsPerSample, TargetChannels);

        using var resampler = new MediaFoundationResampler(rawStream, targetFormat);
        resampler.ResamplerQuality = 60; // Highest quality

        // Read all resampled bytes
        using var resampledStream = new MemoryStream();
        byte[] readBuffer = new byte[4096];
        int bytesRead;
        while ((bytesRead = resampler.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            resampledStream.Write(readBuffer, 0, bytesRead);
        }

        byte[] resampledBytes = resampledStream.ToArray();
        if (resampledBytes.Length == 0) return null;

        // Convert 16-bit PCM bytes to float[] in [-1.0, 1.0]
        int sampleCount = resampledBytes.Length / 2; // 16-bit = 2 bytes per sample
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short pcm16 = BitConverter.ToInt16(resampledBytes, i * 2);
            samples[i] = pcm16 / 32768f;
        }

        return samples;
    }

    // ── IDisposable ──────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_isRecording) StopRecording();
            _capture?.Dispose();
            lock (_bufferLock)
            {
                _rawBuffer?.Dispose();
            }
            _disposed = true;
        }
    }
}
