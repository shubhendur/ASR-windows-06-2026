// ═══════════════════════════════════════════════════════════════════
//  AsrController.cs — Service Orchestration
//  Owns the engine, recorder, keyboard hook, Silero VAD and settings.
//  Shared by the GUI (MainForm) and the headless CLI mode.
//
//  Continuous mode uses Silero VAD (a small neural voice-activity
//  model run via sherpa-onnx) instead of the old RMS-energy VAD.
//  Why the old approach was replaced:
//    • RMS energy cannot distinguish speech from loud non-speech
//      noise — it broke in high-noise environments.
//    • It required 1s of silence at startup to calibrate.
//    • Leading low-energy phonemes were clipped (only above-threshold
//      frames were buffered before speech was "confirmed").
//  Silero VAD is trained to detect *speech*, works at any noise
//  level, needs no calibration, and costs <1% CPU on an i7-1355U.
// ═══════════════════════════════════════════════════════════════════

using SherpaOnnx;

namespace AsrService;

/// <summary>Coordinates recording, VAD, transcription and text injection.</summary>
public sealed class AsrController : IDisposable
{
    // ── Components ───────────────────────────────────────────────
    private readonly AsrEngine _engine = new();
    private readonly AudioRecorder _recorder = new();
    private readonly KeyboardHook _hook = new();

    // ── State ────────────────────────────────────────────────────
    private VoiceActivityDetector? _vad;
    private bool _isProcessing = false;
    private bool _isContinuousMode = false;
    private CancellationTokenSource? _continuousCts;
    private bool _disposed = false;

    private const int FlushIntervalMs = 100;   // how often we drain the audio buffer into the VAD
    private static readonly bool _debugText =
        Environment.GetEnvironmentVariable("ASR_DEBUG_TEXT") == "1";
    private const int SampleRate = 16000;

    public AppSettings Settings { get; } = AppSettings.Load();

    public bool IsContinuousMode => _isContinuousMode;
    public bool IsModelLoaded => _engine.IsReady;

    /// <summary>Fired when continuous mode starts/stops (for UI updates).</summary>
    public event Action<bool>? ContinuousModeChanged;

    // ── Setup ────────────────────────────────────────────────────

    /// <summary>Install the push-to-talk keyboard hook (requires a message pump).</summary>
    public void InstallHotkeys()
    {
        _hook.OnRecordStart += OnPushToTalkStart;
        _hook.OnRecordStop  += OnPushToTalkStop;
        _hook.OnDoubleTap   += ToggleContinuousMode;
        _hook.Install();
    }

    /// <summary>
    /// Downloads (if needed) and loads the selected model + Silero VAD.
    /// </summary>
    public async Task PrepareModelAsync(CancellationToken token = default)
    {
        var model = ModelRegistry.GetById(Settings.ModelId);

        if (model.Loader == ModelLoader.Unsupported)
            throw new NotSupportedException(
                $"\"{model.DisplayName}\" cannot be run locally. {model.Notes}");

        if (!ModelRegistry.IsModelPresent(model))
            await ModelDownloader.DownloadModelAsync(model, token);

        await EnsureVadDownloadedAsync(token);

        _engine.Language = Settings.Language;
        await Task.Run(() => _engine.LoadModel(model), token);
    }

    /// <summary>Ensures the currently selected VAD model is on disk.</summary>
    private Task EnsureVadDownloadedAsync(CancellationToken token) =>
        Settings.VadEngine == "ten"
            ? ModelDownloader.DownloadTenVadAsync(token)
            : ModelDownloader.DownloadSileroVadAsync(token);

    private AudioDeviceInfo SelectedDevice => new()
    {
        Id = Settings.AudioDeviceId,
        Name = Settings.AudioDeviceName,
        IsLoopback = Settings.AudioDeviceIsLoopback,
    };

    // ── File Transcription ───────────────────────────────────────

    /// <summary>
    /// Transcribes a media file (video or audio — any FFmpeg-readable
    /// format). Returns the plain transcript text; a timestamped
    /// .transcript.txt is saved next to the input file.
    /// </summary>
    public async Task<string> TranscribeFileAsync(string path, CancellationToken token = default)
    {
        if (!_engine.IsReady)
            await PrepareModelAsync(token);

        await EnsureVadDownloadedAsync(token);

        return await FileTranscriber.TranscribeAsync(path, _engine, Settings, token);
    }

    // ── Push-to-Talk ─────────────────────────────────────────────

    private void OnPushToTalkStart()
    {
        if (_isContinuousMode) return;    // continuous mode owns the recorder
        if (_isProcessing) return;
        if (!_engine.IsReady)
        {
            Console.WriteLine("[Service] Model not loaded yet — ignoring push-to-talk.");
            return;
        }

        try
        {
            _recorder.StartRecording(SelectedDevice);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to start recording: {ex.Message}");
        }
    }

    private void OnPushToTalkStop()
    {
        if (_isContinuousMode) return;
        if (!_recorder.IsRecording) return;
        if (_isProcessing) return;

        _isProcessing = true;

        // Run transcription on a background thread to avoid blocking
        // the keyboard hook's message processing thread.
        Task.Run(() =>
        {
            try
            {
                float[]? samples = _recorder.StopRecording();
                if (samples == null || samples.Length < 1600) // < 100ms
                    return;

                string text = _engine.Transcribe(samples);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                // Small delay to ensure the target window is focused
                Thread.Sleep(150);
                TextInjector.TypeText(text);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Transcription failed: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        });
    }

    // ── Continuous Mode (Silero VAD) ─────────────────────────────

    /// <summary>Toggle continuous recording mode (double-tap hotkey or GUI button).</summary>
    public void ToggleContinuousMode()
    {
        if (_isContinuousMode) StopContinuous();
        else StartContinuous();
    }

    public void StartContinuous()
    {
        if (_isContinuousMode) return;
        if (!_engine.IsReady)
        {
            Console.WriteLine("[Service] Load a model before starting continuous mode.");
            return;
        }

        try
        {
            // Recreate each start so a VAD-engine change in the GUI takes effect.
            _vad?.Dispose();
            _vad = CreateVad();
            _recorder.StartRecording(SelectedDevice);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to start continuous mode: {ex.Message}");
            return;
        }

        _isContinuousMode = true;
        _continuousCts = new CancellationTokenSource();

        Console.WriteLine($"● Continuous recording started — source: {Settings.AudioDeviceName}" +
                          (Settings.AudioDeviceIsLoopback ? " (speaker loopback)" : ""));

        ContinuousModeChanged?.Invoke(true);
        Task.Run(() => ContinuousLoop(_continuousCts.Token));
    }

    public void StopContinuous()
    {
        if (!_isContinuousMode) return;
        Console.WriteLine("■ Continuous recording stopped");
        _isContinuousMode = false;
        _continuousCts?.Cancel();
        // The background loop handles recorder cleanup and final flush.
    }

    // ── VAD tuning for natural, irregular speech ─────────────────
    // These are quality-critical (not user-facing knobs). Rationale:
    //
    //   MinSilenceDuration = 1.4s — a sentence is only finalized after a
    //     genuine end-of-utterance pause. Shorter mid-sentence pauses
    //     (thinking, breathing, uneven rhythm) are ABSORBED into the same
    //     segment, so the model sees the whole sentence with its natural
    //     gaps. This is what fixes the two reported symptoms:
    //       • wrong punctuation/capitalization — fragments were being
    //         transcribed independently, each getting its own capital
    //         letter and trailing period. Whole sentences punctuate right.
    //       • dropped words at boundaries — no mid-word cut points.
    //     (At 1.0s, any pause >1s split the sentence — the bug.)
    //
    //   MinSpeechDuration = 0.10s — keep short words ("Yes", "No", "OK").
    //     At 0.25s the VAD silently discarded them.
    private const float VadMinSilenceSec = 1.4f;
    private const float VadMinSpeechSec  = 0.10f;

    private VoiceActivityDetector CreateVad()
    {
        var config = new VadModelConfig();

        if (Settings.VadEngine == "ten")
        {
            // TEN VAD: faster onset trigger, ~32% lower RTF than Silero.
            config.TenVad.Model = ModelRegistry.TenVadPath;
            config.TenVad.Threshold = Settings.VadThreshold;
            config.TenVad.MinSilenceDuration = VadMinSilenceSec;
            config.TenVad.MinSpeechDuration = VadMinSpeechSec;
            config.TenVad.MaxSpeechDuration = Settings.VadMaxSpeechSeconds;
            config.TenVad.WindowSize = 256;     // required by TEN VAD at 16kHz
            Console.WriteLine("[VAD] Using TEN VAD.");
        }
        else
        {
            config.SileroVad.Model = ModelRegistry.SileroVadPath;
            config.SileroVad.Threshold = Settings.VadThreshold;
            config.SileroVad.MinSilenceDuration = VadMinSilenceSec;
            config.SileroVad.MinSpeechDuration = VadMinSpeechSec;
            config.SileroVad.MaxSpeechDuration = Settings.VadMaxSpeechSeconds;
            config.SileroVad.WindowSize = 512;  // required by Silero at 16kHz
            Console.WriteLine("[VAD] Using Silero VAD.");
        }

        config.SampleRate = SampleRate;
        config.NumThreads = 1;                  // tiny model — 1 thread is plenty
        config.Provider = "cpu";
        config.Debug = 0;

        return new VoiceActivityDetector(config, 120f /* buffer seconds */);
    }

    /// <summary>
    /// Background loop: drain the recorder every 100ms into Silero VAD;
    /// whenever the VAD emits a completed speech segment, transcribe it.
    /// The VAD handles onset detection (with internal lookback, so no
    /// leading phonemes are lost) and end-of-utterance silence detection.
    /// </summary>
    // ── Pre-speech padding (look-back) ───────────────────────────
    // sherpa-onnx's VAD has NO speech_pad_ms option, and Silero VAD is
    // "slow to trigger" — it fires a beat AFTER speech starts, so the
    // first word after every pause is clipped and lost. We fix this by
    // keeping a rolling history of the exact audio fed to the VAD and
    // prepending ~300ms of it to each segment (the audio just before the
    // VAD's detected onset). See sherpa-onnx issue #3035.
    private static readonly float PreRollSeconds =
        float.TryParse(Environment.GetEnvironmentVariable("ASR_PREROLL_SEC"), out var v) ? v : 0.30f;
    private readonly List<float> _audioHistory = new();
    private long _historyBase;   // absolute sample index of _audioHistory[0]
    private long _fedTotal;      // total samples fed to the VAD since Reset

    private async Task ContinuousLoop(CancellationToken token)
    {
        var vad = _vad!;
        vad.Reset();

        _audioHistory.Clear();
        _historyBase = 0;
        _fedTotal = 0;

        // ── Decoupled pipeline ────────────────────────────────
        // The audio pump below must NEVER block: while a segment is
        // being transcribed (seconds of heavy CPU), the recorder keeps
        // being drained and the VAD keeps segmenting. Completed speech
        // segments are queued to a separate transcription worker.
        // (The old design transcribed inline, freezing the pump for
        // 1–4s per sentence; combined with WASAPI's small capture
        // buffer this dropped whole sentences under CPU load.)
        var segments = System.Threading.Channels.Channel.CreateUnbounded<float[]>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        Task worker = Task.Run(() => TranscriptionWorker(segments.Reader), CancellationToken.None);

        try
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(FlushIntervalMs, token).ConfigureAwait(false); }
                catch (TaskCanceledException) { break; }

                float[]? chunk = _recorder.Flush();
                if (chunk != null && chunk.Length > 0)
                {
                    if (chunk.Length > SampleRate) // should no longer happen — pump was blocked
                        Console.WriteLine($"[Audio] WARNING: backlog flush of {chunk.Length / (float)SampleRate:F1}s");
                    vad.AcceptWaveform(chunk);
                    RecordHistory(chunk);
                }

                DrainVadToQueue(vad, segments.Writer);
            }
        }
        finally
        {
            // Flush any trailing speech still inside the VAD
            vad.Flush();
            DrainVadToQueue(vad, segments.Writer);
            vad.Reset();

            if (_recorder.IsRecording)
                _recorder.StopRecording();

            // Let the worker finish transcribing whatever is queued
            segments.Writer.Complete();
            await worker.ConfigureAwait(false);

            _isContinuousMode = false;
            ContinuousModeChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// Append audio to the rolling history and trim it to a bounded window
    /// (long enough to always cover the longest segment + its pre-roll).
    /// </summary>
    private void RecordHistory(float[] chunk)
    {
        _audioHistory.AddRange(chunk);
        _fedTotal += chunk.Length;

        // Keep MaxSpeechDuration + a couple of seconds of slack.
        int keep = (int)((Settings.VadMaxSpeechSeconds + 3f) * SampleRate);
        int excess = _audioHistory.Count - keep;
        if (excess > 0)
        {
            _audioHistory.RemoveRange(0, excess);
            _historyBase += excess;
        }
    }

    /// <summary>
    /// Returns the segment audio with ~300ms of pre-onset audio prepended
    /// from the history, so the VAD's late trigger doesn't clip the first
    /// word. Falls back to the raw segment if the history no longer covers it.
    /// </summary>
    private float[] AddPreRoll(SpeechSegment segment)
    {
        long segStart = segment.Start;                       // absolute sample index of onset
        int prerollLen = (int)(PreRollSeconds * SampleRate);
        long from = Math.Max(_historyBase, segStart - prerollLen);
        int count = (int)(segStart - from);

        if (count <= 0) return segment.Samples;

        int offset = (int)(from - _historyBase);
        if (offset < 0 || offset + count > _audioHistory.Count)
            return segment.Samples; // history rolled past it — use raw segment

        var padded = new float[count + segment.Samples.Length];
        _audioHistory.CopyTo(offset, padded, 0, count);
        Array.Copy(segment.Samples, 0, padded, count, segment.Samples.Length);
        return padded;
    }

    /// <summary>Move completed VAD segments into the transcription queue (non-blocking).</summary>
    private void DrainVadToQueue(
        VoiceActivityDetector vad,
        System.Threading.Channels.ChannelWriter<float[]> writer)
    {
        while (!vad.IsEmpty())
        {
            var segment = vad.Front();
            vad.Pop();

            // Only skip near-empty blips (< 60ms). The old 200ms threshold
            // double-dropped short real words on top of the VAD's own
            // MinSpeechDuration filter.
            if (segment.Samples.Length < SampleRate / 16) continue; // < ~60ms

            writer.TryWrite(AddPreRoll(segment)); // unbounded channel — never fails
        }
    }

    /// <summary>
    /// Consumes speech segments and transcribes + injects them, in order,
    /// independently of the audio pump.
    /// </summary>
    private async Task TranscriptionWorker(System.Threading.Channels.ChannelReader<float[]> reader)
    {
        await foreach (float[] samples in reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                _isProcessing = true;
                string text = _engine.Transcribe(samples);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Opt-in debug of recognized text (off by default → privacy).
                    if (_debugText)
                        Console.WriteLine($"[DEBUG] seg {samples.Length / (float)SampleRate:F1}s -> {text}");

                    await Task.Delay(100).ConfigureAwait(false); // window focus stability
                    TextInjector.TypeText(text + " ");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Continuous transcription failed: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }
    }

    // ── Cleanup ──────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _continuousCts?.Cancel();
        _hook.Dispose();
        _recorder.Dispose();
        _vad?.Dispose();
        _engine.Dispose();
        Settings.Save();
        _disposed = true;
    }
}
