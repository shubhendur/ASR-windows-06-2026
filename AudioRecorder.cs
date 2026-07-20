// ═══════════════════════════════════════════════════════════════════
//  AudioRecorder.cs — WASAPI Capture with Device Selection & Loopback
//  Supports microphones (WasapiCapture) and speakers/system audio
//  (WasapiLoopbackCapture — records only what the machine plays, i.e.
//  the other party on a call, never the local mic).
//
//  Audio is converted to 16kHz mono float *incrementally* as it
//  arrives, using a streaming FIR low-pass + linear resampler. This
//  replaces the old per-Flush() MediaFoundationResampler, which was
//  expensive (new COM resampler every 200ms) and produced chunk-
//  boundary artifacts that degraded ASR accuracy.
// ═══════════════════════════════════════════════════════════════════

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AsrService;

/// <summary>Describes a selectable audio source.</summary>
public sealed class AudioDeviceInfo
{
    /// <summary>WASAPI device id, or "" for the default device.</summary>
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>True = render device captured via loopback (speaker/system audio).</summary>
    public bool IsLoopback { get; init; }

    public override string ToString() =>
        IsLoopback ? $"🔊 System Audio: {Name}" : $"🎙 Microphone: {Name}";
}

/// <summary>
/// Captures audio from a selected device using WASAPI and streams
/// 16kHz mono float samples suitable for sherpa-onnx.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    // ── Constants ────────────────────────────────────────────────
    private const int TargetSampleRate = 16000;

    // ── State ────────────────────────────────────────────────────
    private WasapiCapture? _capture;
    private readonly List<float> _buffer = new();      // resampled 16k mono samples
    private readonly object _bufferLock = new();
    private StreamingResampler? _resampler;
    private WaveFormat? _captureFormat;
    private volatile bool _isRecording = false;
    private bool _disposed = false;

    /// <summary>True while actively recording audio.</summary>
    public bool IsRecording => _isRecording;

    // ── Device Enumeration ───────────────────────────────────────

    /// <summary>
    /// Lists all active audio sources: microphones first, then render
    /// devices (speakers/headphones) offered as loopback sources.
    /// </summary>
    public static List<AudioDeviceInfo> ListDevices()
    {
        var devices = new List<AudioDeviceInfo>
        {
            new AudioDeviceInfo { Id = "", Name = "Default Microphone", IsLoopback = false },
        };

        using var enumerator = new MMDeviceEnumerator();

        foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add(new AudioDeviceInfo { Id = dev.ID, Name = dev.FriendlyName, IsLoopback = false });
            dev.Dispose();
        }

        // Render devices → loopback capture: records only what the PC
        // plays through that device (e.g. the far end of a call).
        devices.Add(new AudioDeviceInfo { Id = "", Name = "Default Speakers", IsLoopback = true });
        foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            devices.Add(new AudioDeviceInfo { Id = dev.ID, Name = dev.FriendlyName, IsLoopback = true });
            dev.Dispose();
        }

        return devices;
    }

    // ── Public API ───────────────────────────────────────────────

    /// <summary>Start capturing audio from the given device (null = default microphone).</summary>
    public void StartRecording(AudioDeviceInfo? device = null)
    {
        if (_isRecording) return;

        device ??= new AudioDeviceInfo();

        _capture = CreateCapture(device);
        _captureFormat = _capture.WaveFormat;
        _resampler = new StreamingResampler(_captureFormat.SampleRate, TargetSampleRate);

        lock (_bufferLock) { _buffer.Clear(); }

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        _isRecording = true;
    }

    // WASAPI capture buffer. NAudio's default is 100ms — under heavy CPU
    // (ONNX inference saturating cores) the capture thread can miss its
    // deadline and Windows silently DROPS microphone audio. 500ms gives
    // the thread plenty of scheduling slack at negligible latency cost.
    private const int CaptureBufferMs = 500;

    private static WasapiCapture CreateCapture(AudioDeviceInfo device)
    {
        using var enumerator = new MMDeviceEnumerator();

        if (device.IsLoopback)
        {
            var mmDevice = string.IsNullOrEmpty(device.Id)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(device.Id);
            return new WasapiLoopbackCapture(mmDevice);
        }
        else
        {
            var mmDevice = string.IsNullOrEmpty(device.Id)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                : enumerator.GetDevice(device.Id);
            return new WasapiCapture(mmDevice, useEventSync: true, audioBufferMillisecondsLength: CaptureBufferMs);
        }
    }

    /// <summary>
    /// Stop recording and return all captured audio as 16kHz mono float samples.
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
            samples = _buffer.Count > 0 ? _buffer.ToArray() : null;
            _buffer.Clear();
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
        _capture = null;
        _resampler = null;

        return samples;
    }

    /// <summary>
    /// Returns and clears the samples accumulated since the last flush.
    /// Cheap — audio is already converted; no resampling happens here.
    /// </summary>
    public float[]? Flush()
    {
        lock (_bufferLock)
        {
            if (_buffer.Count == 0) return null;
            float[] samples = _buffer.ToArray();
            _buffer.Clear();
            return samples;
        }
    }

    // ── Audio Processing ─────────────────────────────────────────

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _captureFormat == null || _resampler == null) return;

        // Convert raw bytes → float mono at capture rate
        float[] mono = ConvertToMonoFloat(e.Buffer, e.BytesRecorded, _captureFormat);

        // Stream-resample to 16kHz and append to the shared buffer
        lock (_bufferLock)
        {
            _resampler.Process(mono, _buffer);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Console.Error.WriteLine($"[Recorder] Recording error: {e.Exception.Message}");
        }
    }

    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT — WASAPI shared mode reports
    // extensible formats with this subtype for 32-bit float audio.
    private static readonly Guid KsDataFormatSubTypeIeeeFloat =
        new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>Converts an interleaved capture buffer to mono float [-1, 1].</summary>
    private static float[] ConvertToMonoFloat(byte[] buffer, int bytes, WaveFormat format)
    {
        int channels = format.Channels;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format is WaveFormatExtensible ext && ext.SubFormat == KsDataFormatSubTypeIeeeFloat))
        {
            int frames = bytes / (4 * channels);
            float[] mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += BitConverter.ToSingle(buffer, (f * channels + c) * 4);
                mono[f] = sum / channels;
            }
            return mono;
        }
        else if (format.BitsPerSample == 16)
        {
            int frames = bytes / (2 * channels);
            float[] mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                int sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += BitConverter.ToInt16(buffer, (f * channels + c) * 2);
                mono[f] = sum / (32768f * channels);
            }
            return mono;
        }
        else if (format.BitsPerSample == 32)
        {
            int frames = bytes / (4 * channels);
            float[] mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                long sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += BitConverter.ToInt32(buffer, (f * channels + c) * 4);
                mono[f] = sum / (2147483648f * channels);
            }
            return mono;
        }

        throw new NotSupportedException(
            $"Unsupported capture format: {format.Encoding} {format.BitsPerSample}bit");
    }

    // ── IDisposable ──────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_isRecording) StopRecording();
            _capture?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Streaming sample-rate converter: windowed-sinc FIR low-pass
/// (anti-aliasing) followed by linear interpolation. State carries
/// across chunks, so there are no boundary artifacts. CPU cost is
/// negligible (~3M MAC/s at 48kHz input) on an i7-1355U.
/// </summary>
internal sealed class StreamingResampler
{
    private readonly int _srcRate;
    private readonly int _dstRate;
    private readonly double _step;        // source samples per output sample
    private readonly float[] _fir;        // low-pass kernel (null-op if src <= dst)
    private readonly float[] _history;    // FIR delay line carry-over between chunks
    private double _pos;                  // fractional read position into the filtered stream
    private float _prevFiltered;          // last filtered sample of the previous chunk
    private bool _hasPrev;

    public StreamingResampler(int srcRate, int dstRate)
    {
        _srcRate = srcRate;
        _dstRate = dstRate;
        _step = (double)srcRate / dstRate;

        // Anti-aliasing FIR (only needed when downsampling):
        // cutoff at 45% of the target rate, 63-tap Blackman-windowed sinc.
        int taps = srcRate > dstRate ? 63 : 1;
        _fir = DesignLowPass(taps, srcRate, 0.45 * dstRate);
        _history = new float[_fir.Length - 1];
        _pos = 0;
    }

    /// <summary>Filter + resample one chunk, appending output samples to dst.</summary>
    public void Process(float[] src, List<float> dst)
    {
        if (src.Length == 0) return;

        // ── 1. FIR low-pass with history carry ───────────────
        float[] filtered;
        if (_fir.Length == 1)
        {
            filtered = src;
        }
        else
        {
            int h = _history.Length;
            float[] work = new float[h + src.Length];
            Array.Copy(_history, work, h);
            Array.Copy(src, 0, work, h, src.Length);

            filtered = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float acc = 0;
                for (int t = 0; t < _fir.Length; t++)
                    acc += _fir[t] * work[i + t];
                filtered[i] = acc;
            }

            // Save tail for next chunk
            Array.Copy(work, work.Length - h, _history, 0, h);
        }

        // ── 2. Linear interpolation with fractional carry ────
        // Virtual stream: [prevFiltered] + filtered  (index 0 = prev)
        int offset = _hasPrev ? 1 : 0;
        int total = filtered.Length + offset;

        while (_pos + 1 < total)
        {
            int i = (int)_pos;
            double frac = _pos - i;
            float s0 = i == 0 && _hasPrev ? _prevFiltered : filtered[i - offset];
            float s1 = filtered[i + 1 - offset];
            dst.Add((float)(s0 * (1 - frac) + s1 * frac));
            _pos += _step;
        }

        // Rebase position so index 0 refers to the last filtered sample
        _pos -= total - 1;
        _prevFiltered = filtered[^1];
        _hasPrev = true;
    }

    private static float[] DesignLowPass(int taps, int sampleRate, double cutoffHz)
    {
        if (taps <= 1) return new float[] { 1f };

        var kernel = new float[taps];
        double fc = cutoffHz / sampleRate; // normalized cutoff (0..0.5)
        int mid = taps / 2;
        double sum = 0;

        for (int n = 0; n < taps; n++)
        {
            int k = n - mid;
            double sinc = k == 0 ? 2 * Math.PI * fc : Math.Sin(2 * Math.PI * fc * k) / k;
            // Blackman window
            double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * n / (taps - 1))
                            + 0.08 * Math.Cos(4 * Math.PI * n / (taps - 1));
            kernel[n] = (float)(sinc * w);
            sum += kernel[n];
        }

        // Normalize for unity DC gain
        for (int n = 0; n < taps; n++) kernel[n] /= (float)sum;
        return kernel;
    }
}
