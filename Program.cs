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
                "--run" or _               => await RunService(),
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
    private static async Task<int> RunService()
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
        _hook.Install();

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine("  ✓ ASR Service is READY");
        Console.WriteLine("  ─────────────────────────────────────────────────────");
        Console.WriteLine("  Hold RIGHT ALT to record speech.");
        Console.WriteLine("  Release to transcribe and type into active window.");
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
        Console.WriteLine("  --download-model   Download the Parakeet TDT 0.6B v3 INT8 model");
        Console.WriteLine("  --install          Register for Windows auto-startup");
        Console.WriteLine("  --uninstall        Remove from Windows auto-startup");
        Console.WriteLine("  --status           Show model and startup status");
        Console.WriteLine("  --help             Show this help message");
        Console.WriteLine();
        Console.WriteLine("Push-to-Talk:");
        Console.WriteLine("  Hold RIGHT ALT to record speech.");
        Console.WriteLine("  Release RIGHT ALT to transcribe and inject text.");
        Console.WriteLine();
        return 0;
    }

    // ── Push-to-Talk Handlers ────────────────────────────────────

    private static void OnPushToTalkStart()
    {
        if (_isProcessing) return; // Don't interrupt ongoing transcription

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

    // ── Cleanup ──────────────────────────────────────────────────

    private static void Cleanup()
    {
        Console.WriteLine("[Service] Cleaning up...");
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
        Console.WriteLine("  ║   Parakeet TDT 0.6B v3 (INT8 ONNX)  ║");
        Console.WriteLine("  ║   Push-to-Talk: Hold Right Alt       ║");
        Console.WriteLine("  ╚═══════════════════════════════════════╝");
        Console.WriteLine();
    }
}
