// ═══════════════════════════════════════════════════════════════════
//  Program.cs — ASR Service Entry Point
//  Headless push-to-talk speech-to-text service for Windows.
//
//  Usage:
//    AsrService.exe                   Run the service (default)
//    AsrService.exe --download-model  Download the ASR model
//    AsrService.exe --install         Register for Windows startup
//    AsrService.exe --uninstall       Remove from Windows startup
//    AsrService.exe --status          Show current status
// ═══════════════════════════════════════════════════════════════════

namespace AsrService;

static class Program
{
    // ── Shared State ─────────────────────────────────────────────
    private static readonly AsrEngine _engine = new();
    private static readonly AudioRecorder _recorder = new();
    private static readonly KeyboardHook _hook = new();
    private static bool _isProcessing = false;

    // ── Continuous Mode State ────────────────────────────────────
    private static bool _isContinuousMode = false;
    private static CancellationTokenSource? _continuousCts = null;

    // ── Adaptive Voice Activity Detection (VAD) constants ────────
    // Instead of a fixed RMS threshold, we calibrate the noise floor
    // from the first ~1 second of ambient audio when continuous mode
    // starts. Speech threshold is set relative to that noise floor.
    private const int CalibrationDurationMs  = 1000;   // 1s of ambient sampling at start
    private const float NoiseFloorMultiplier = 3.5f;   // speech must be this many × louder than noise
    private const float MinSpeechThreshold   = 0.003f; // absolute minimum (for very quiet rooms)
    private const float NoiseFloorAlpha      = 0.05f;  // exponential moving average weight for noise floor updates
    private const int SpeechOnsetFrames      = 2;      // consecutive speech frames required to confirm speech start (debounce)
    private const int SilenceTimeoutMs       = 2000;   // 2 seconds of silence triggers transcription
    private const int FlushIntervalMs        = 200;    // how often we check the audio buffer

    // ── Entry Point ──────────────────────────────────────────────

    [STAThread]
    static async Task<int> Main(string[] args)
    {
        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "--run";

        try
        {
            return command switch
            {
                "--download-model" or "-d" => await RunDownloadModel(),
                "--install" or "-i"        => RunInstall(),
                "--uninstall" or "-u"      => RunUninstall(),
                "--status" or "-s"         => RunStatus(),
                "--help" or "-h" or "/?"   => RunHelp(),
                "--run" or _               => RunService(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FATAL] {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // ── Commands ─────────────────────────────────────────────────

    /// <summary>Run the main push-to-talk service.</summary>
    private static int RunService()
    {
        PrintBanner();

        string modelDir = ModelDownloader.GetDefaultModelDir();

        // ── Check model ──────────────────────────────────────
        if (!ModelDownloader.IsModelPresent(modelDir))
        {
            Console.Error.WriteLine("[ERROR] Model not found!");
            Console.Error.WriteLine($"[ERROR] Expected at: {modelDir}");
            Console.Error.WriteLine("[ERROR] Run with --download-model first.");
            return 1;
        }

        // ── Set mic sensitivity to 100% ──────────────────────
        MicController.SetMicVolume(100);

        // ── Load ASR model ───────────────────────────────────
        Console.WriteLine();
        _engine.LoadModel(modelDir);

        // ── Wire up push-to-talk ─────────────────────────────
        _hook.OnRecordStart += OnPushToTalkStart;
        _hook.OnRecordStop  += OnPushToTalkStop;
        _hook.OnDoubleTap   += OnDoubleTap;
        _hook.Install();

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine("  ✓ ASR Service is READY");
        Console.WriteLine("  ─────────────────────────────────────────────────────");
        Console.WriteLine("  Hold RIGHT ALT to record speech.");
        Console.WriteLine("  Release to transcribe and type into active window.");
        Console.WriteLine("  Double-tap RIGHT ALT to toggle continuous recording.");
        Console.WriteLine("  Press Ctrl+C to exit.");
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine();

        // ── Setup graceful shutdown ──────────────────────────
        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Prevent immediate termination
            Console.WriteLine("\n[Service] Shutting down...");
            cts.Cancel();
        };

        // ── Run message loop (required for keyboard hooks) ───
        // WH_KEYBOARD_LL hooks require a message pump on the
        // thread that installed them. Application.Run() provides this.
        // We run it on the main thread and use CancelKeyPress to exit.

        // Register a Windows message filter to detect our cancellation
        var exitTimer = new System.Windows.Forms.Timer { Interval = 500 };
        exitTimer.Tick += (_, _) =>
        {
            if (cts.IsCancellationRequested)
            {
                exitTimer.Stop();
                Cleanup();
                Application.ExitThread();
            }
        };
        exitTimer.Start();

        // This blocks until Application.ExitThread() is called
        Application.Run();

        return 0;
    }

    /// <summary>Download the ASR model.</summary>
    private static async Task<int> RunDownloadModel()
    {
        PrintBanner();
        await ModelDownloader.DownloadModelAsync();
        return 0;
    }

    /// <summary>Register for Windows startup.</summary>
    private static int RunInstall()
    {
        StartupManager.Install();
        return 0;
    }

    /// <summary>Remove from Windows startup.</summary>
    private static int RunUninstall()
    {
        StartupManager.Uninstall();
        return 0;
    }

    /// <summary>Show current status.</summary>
    private static int RunStatus()
    {
        string modelDir = ModelDownloader.GetDefaultModelDir();
        bool modelPresent = ModelDownloader.IsModelPresent(modelDir);
        bool startupInstalled = StartupManager.IsInstalled();

        Console.WriteLine("ASR Service Status");
        Console.WriteLine("──────────────────────────────────────");
        Console.WriteLine($"  Model:    {(modelPresent ? "✓ Installed" : "✗ Not found")}");
        Console.WriteLine($"  Path:     {modelDir}");
        Console.WriteLine($"  Startup:  {(startupInstalled ? "✓ Registered" : "✗ Not registered")}");
        Console.WriteLine($"  Exe:      {Environment.ProcessPath}");
        Console.WriteLine("──────────────────────────────────────");

        return 0;
    }

    /// <summary>Show help text.</summary>
    private static int RunHelp()
    {
        Console.WriteLine("ASR Service — Push-to-Talk Speech-to-Text for Windows");
        Console.WriteLine();
        Console.WriteLine("Usage: AsrService.exe [command]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  (no args)          Run the push-to-talk service");
        Console.WriteLine("  --download-model   Download the Parakeet TDT 0.6B v2 INT8 model");
        Console.WriteLine("  --install          Register for Windows auto-startup");
        Console.WriteLine("  --uninstall        Remove from Windows auto-startup");
        Console.WriteLine("  --status           Show model and startup status");
        Console.WriteLine("  --help             Show this help message");
        Console.WriteLine();
        Console.WriteLine("Push-to-Talk:");
        Console.WriteLine("  Hold RIGHT ALT to record speech.");
        Console.WriteLine("  Release RIGHT ALT to transcribe and inject text.");
        Console.WriteLine("  Double-tap RIGHT ALT to toggle continuous recording.");
        Console.WriteLine();
        return 0;
    }

    // ── Push-to-Talk Handlers ────────────────────────────────────

    private static void OnPushToTalkStart()
    {
        if (_isContinuousMode) return;    // continuous mode owns the recorder
        if (_isProcessing) return;        // don't interrupt ongoing transcription

        try
        {
            _recorder.StartRecording();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to start recording: {ex.Message}");
        }
    }

    private static void OnPushToTalkStop()
    {
        if (_isContinuousMode) return;    // continuous mode owns the recorder
        if (!_recorder.IsRecording) return;
        if (_isProcessing) return;

        _isProcessing = true;

        // Run transcription on a background thread to avoid blocking
        // the keyboard hook's message processing thread.
        Task.Run(() =>
        {
            try
            {
                // Stop recording and get samples
                float[]? samples = _recorder.StopRecording();
                if (samples == null || samples.Length < 1600) // < 100ms
                {
                    Console.WriteLine("[Service] Recording too short, ignoring.");
                    return;
                }

                // Transcribe
                string text = _engine.Transcribe(samples);
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("[Service] No speech detected.");
                    return;
                }

                // Small delay to ensure the target window is focused
                // (Right Alt release may trigger a menu — give it time to dismiss)
                Thread.Sleep(150);

                // Inject text into the active window
                TextInjector.TypeText(text);
                Console.WriteLine($"[Service] Injected {text.Length} characters into active window.");
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

    // ── Continuous Mode ──────────────────────────────────────────

    private static void OnDoubleTap()
    {
        if (_isContinuousMode)
        {
            // ── Stop continuous mode ─────────────────────────
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("  ■ Continuous recording STOPPED");
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine();
            _isContinuousMode = false;
            _continuousCts?.Cancel();
            // The background loop will handle cleanup
        }
        else
        {
            // ── Start continuous mode ────────────────────────
            if (_isProcessing)
            {
                Console.WriteLine("[Service] Cannot start continuous mode while processing.");
                return;
            }

            _isContinuousMode = true;
            _continuousCts = new CancellationTokenSource();

            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("  ● Continuous recording STARTED");
            Console.WriteLine("  Calibrating noise floor — stay quiet for 1 second...");
            Console.WriteLine("  Double-tap RIGHT ALT again to stop.");
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine();

            // Start recording if not already active
            if (!_recorder.IsRecording)
            {
                try
                {
                    _recorder.StartRecording();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ERROR] Failed to start recording: {ex.Message}");
                    _isContinuousMode = false;
                    _continuousCts = null;
                    return;
                }
            }

            Task.Run(() => ContinuousRecordingLoop(_continuousCts.Token));
        }
    }

    /// <summary>
    /// Background loop for continuous recording mode.
    ///
    /// Design:
    ///   Phase 1 — Calibration (~1 second):
    ///     Record ambient audio and compute the average RMS as the
    ///     noise floor. This automatically adapts to any environment
    ///     (fan noise, AC, traffic, etc.)
    ///
    ///   Phase 2 — Main loop:
    ///     - Every FlushIntervalMs, pull audio and compute RMS.
    ///     - Speech threshold = max(noiseFloor × 3.5, MinSpeechThreshold).
    ///     - Require SpeechOnsetFrames consecutive frames above threshold
    ///       to confirm speech onset (debounces transient noises).
    ///     - During confirmed silence, slowly update the noise floor
    ///       with an exponential moving average so the system adapts
    ///       to gradual environment changes.
    ///     - After 2 seconds of post-speech silence, transcribe.
    /// </summary>
    private static async Task ContinuousRecordingLoop(CancellationToken token)
    {
        // ── Phase 1: Noise floor calibration ─────────────────
        float noiseFloor = await CalibrateNoiseFloorAsync(token);
        if (token.IsCancellationRequested) return;

        float speechThreshold = Math.Max(noiseFloor * NoiseFloorMultiplier, MinSpeechThreshold);

        Console.WriteLine($"[VAD] Noise floor calibrated: RMS={noiseFloor:F5}");
        Console.WriteLine($"[VAD] Speech threshold: RMS={speechThreshold:F5} ({NoiseFloorMultiplier}× noise floor)");
        Console.WriteLine("[Continuous] ✓ Calibration complete — speak naturally.");
        Console.WriteLine();

        // ── Phase 2: Main loop ───────────────────────────────
        List<float> segmentBuffer = new();
        bool hasSpeech = false;              // did we confirm speech in this segment?
        int consecutiveSilenceMs = 0;        // how long silence has lasted after speech
        int consecutiveSpeechFrames = 0;     // for debounced speech onset detection

        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(FlushIntervalMs, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                // Pull whatever audio has accumulated since last flush
                float[]? chunk = _recorder.Flush();
                if (chunk == null || chunk.Length == 0) continue;

                // Compute RMS energy for this chunk
                float rms = ComputeRms(chunk);
                bool isSpeech = rms >= speechThreshold;

                if (!hasSpeech)
                {
                    // ── Waiting for speech onset ─────────────
                    if (isSpeech)
                    {
                        consecutiveSpeechFrames++;
                        // Buffer this chunk in case it's real speech
                        segmentBuffer.AddRange(chunk);

                        if (consecutiveSpeechFrames >= SpeechOnsetFrames)
                        {
                            // Confirmed speech onset!
                            hasSpeech = true;
                            consecutiveSilenceMs = 0;
                            Console.WriteLine($"[VAD] Speech confirmed (RMS={rms:F4}, threshold={speechThreshold:F4})");
                        }
                    }
                    else
                    {
                        consecutiveSpeechFrames = 0;

                        // Discard any tentative speech frames that weren't confirmed
                        if (segmentBuffer.Count > 0)
                        {
                            segmentBuffer.Clear();
                        }

                        // Adapt noise floor during silence (exponential moving average)
                        noiseFloor = noiseFloor * (1f - NoiseFloorAlpha) + rms * NoiseFloorAlpha;
                        speechThreshold = Math.Max(noiseFloor * NoiseFloorMultiplier, MinSpeechThreshold);
                    }
                }
                else
                {
                    // ── In the middle of a speech segment ────
                    // Always accumulate (including silence — the model needs
                    // the audio context to transcribe trailing words correctly).
                    segmentBuffer.AddRange(chunk);

                    if (!isSpeech)
                    {
                        consecutiveSilenceMs += FlushIntervalMs;
                        consecutiveSpeechFrames = 0;
                    }
                    else
                    {
                        consecutiveSilenceMs = 0;
                    }

                    // Check if silence has lasted long enough to trigger transcription
                    if (consecutiveSilenceMs >= SilenceTimeoutMs)
                    {
                        await TranscribeAndInjectAsync(segmentBuffer);

                        // Reset for next speech segment
                        segmentBuffer.Clear();
                        hasSpeech = false;
                        consecutiveSilenceMs = 0;
                        consecutiveSpeechFrames = 0;

                        // Re-adapt noise floor from the trailing silence
                        noiseFloor = noiseFloor * (1f - NoiseFloorAlpha) + rms * NoiseFloorAlpha;
                        speechThreshold = Math.Max(noiseFloor * NoiseFloorMultiplier, MinSpeechThreshold);
                    }
                }
            }
        }
        finally
        {
            // ── Cleanup when continuous mode stops ───────────
            // Transcribe any remaining audio that had confirmed speech
            if (hasSpeech && segmentBuffer.Count > 0)
            {
                await TranscribeAndInjectAsync(segmentBuffer);
            }
            segmentBuffer.Clear();

            // Stop the recorder
            if (_recorder.IsRecording)
            {
                _recorder.StopRecording();
            }

            _isContinuousMode = false;
            Console.WriteLine("[Continuous] Background loop exited.");
        }
    }

    /// <summary>
    /// Spend ~1 second recording ambient audio to measure the noise floor.
    /// Returns the average RMS across all calibration chunks.
    /// </summary>
    private static async Task<float> CalibrateNoiseFloorAsync(CancellationToken token)
    {
        List<float> rmsValues = new();
        int elapsed = 0;

        while (elapsed < CalibrationDurationMs && !token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushIntervalMs, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            elapsed += FlushIntervalMs;

            float[]? chunk = _recorder.Flush();
            if (chunk != null && chunk.Length > 0)
            {
                rmsValues.Add(ComputeRms(chunk));
            }
        }

        if (rmsValues.Count == 0)
            return MinSpeechThreshold; // fallback if no audio captured

        // Use the average RMS as the noise floor
        float sum = 0;
        foreach (float v in rmsValues) sum += v;
        return sum / rmsValues.Count;
    }

    /// <summary>
    /// Transcribe accumulated audio and inject the result as keystrokes.
    /// </summary>
    private static async Task TranscribeAndInjectAsync(List<float> audioBuffer)
    {
        if (audioBuffer.Count < 3200) // < 200ms — too short
        {
            Console.WriteLine("[Continuous] Segment too short, skipping.");
            return;
        }

        float[] samples = audioBuffer.ToArray();
        float durationSec = (float)samples.Length / 16000;
        Console.WriteLine($"[Continuous] Transcribing {durationSec:F1}s segment...");

        try
        {
            _isProcessing = true;
            string text = _engine.Transcribe(samples);

            if (!string.IsNullOrWhiteSpace(text))
            {
                // Small delay for window focus stability
                await Task.Delay(100).ConfigureAwait(false);
                TextInjector.TypeText(text + " ");
                Console.WriteLine($"[Continuous] Injected: \"{text}\"");
            }
            else
            {
                Console.WriteLine("[Continuous] No speech recognized in segment.");
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

    /// <summary>Compute the Root Mean Square energy of audio samples.</summary>
    private static float ComputeRms(float[] samples)
    {
        if (samples.Length == 0) return 0f;

        double sumSquares = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sumSquares += (double)samples[i] * samples[i];
        }
        return (float)Math.Sqrt(sumSquares / samples.Length);
    }

    // ── Cleanup ──────────────────────────────────────────────────

    private static void Cleanup()
    {
        Console.WriteLine("[Service] Cleaning up...");
        _continuousCts?.Cancel();
        _hook.Dispose();
        _recorder.Dispose();
        _engine.Dispose();
        Console.WriteLine("[Service] Goodbye!");
    }

    // ── UI ───────────────────────────────────────────────────────

    private static void PrintBanner()
    {
        Console.WriteLine();
        Console.WriteLine("  ╔═══════════════════════════════════════╗");
        Console.WriteLine("  ║   🎙️  ASR Service for Windows 11     ║");
        Console.WriteLine("  ║   Parakeet TDT 0.6B v2 (INT8 ONNX)  ║");
        Console.WriteLine("  ║   Push-to-Talk: Hold Right Alt       ║");
        Console.WriteLine("  ║   Continuous: Double-tap Right Alt   ║");
        Console.WriteLine("  ╚═══════════════════════════════════════╝");
        Console.WriteLine();
    }
}
