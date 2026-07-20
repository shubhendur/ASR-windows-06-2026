// ═══════════════════════════════════════════════════════════════════
//  Program.cs — ASR Service Entry Point
//  Push-to-talk / continuous speech-to-text service for Windows.
//
//  Usage:
//    AsrService.exe                   Run with the settings GUI (default)
//    AsrService.exe --headless        Run without GUI (console only)
//    AsrService.exe --download-model  Download the default ASR model
//    AsrService.exe --install         Register for Windows startup
//    AsrService.exe --uninstall       Remove from Windows startup
//    AsrService.exe --status          Show current status
// ═══════════════════════════════════════════════════════════════════

namespace AsrService;

static class Program
{
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
                "--headless"               => RunHeadless(),
                "--continuous"             => RunHeadless(startContinuous: true),
                "--transcribe" or "-t"     => await RunTranscribeFile(args),
                "--run" or _               => RunGui(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FATAL] {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // ── GUI Mode (default) ───────────────────────────────────────

    private static int RunGui()
    {
        ApplicationConfiguration.Initialize();

        var controller = new AsrController();
        var form = new MainForm(controller);

        // Keyboard hook needs a message pump — the WinForms loop provides it.
        controller.InstallHotkeys();

        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine("  ASR Service — GUI mode");
        Console.WriteLine("  1. Pick an audio source (mic, or System Audio to");
        Console.WriteLine("     record only the other person on a call).");
        Console.WriteLine("  2. Pick a model and language, click Download & Load.");
        Console.WriteLine("  3. Hold RIGHT ALT to talk, double-tap for continuous.");
        Console.WriteLine("═══════════════════════════════════════════════════════");

        // Auto-load the model if it's already on disk (no download prompt needed)
        var model = ModelRegistry.GetById(controller.Settings.ModelId);
        if (model.Loader != ModelLoader.Unsupported && ModelRegistry.IsModelPresent(model))
        {
            form.Shown += async (_, _) => await form.AutoLoadModelAsync();
        }

        Application.Run(form);
        controller.Dispose();
        return 0;
    }

    // ── Headless Mode ────────────────────────────────────────────

    private static int RunHeadless(bool startContinuous = false)
    {
        var controller = new AsrController();

        var model = ModelRegistry.GetById(controller.Settings.ModelId);
        if (!ModelRegistry.IsModelPresent(model))
        {
            Console.Error.WriteLine("[ERROR] Model not found! Run with --download-model first.");
            return 1;
        }

        // Set mic sensitivity to 100% (only meaningful for mic sources)
        if (!controller.Settings.AudioDeviceIsLoopback)
            MicController.SetMicVolume(100);

        controller.PrepareModelAsync().GetAwaiter().GetResult();
        controller.InstallHotkeys();

        if (startContinuous)
            controller.StartContinuous();

        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine("  ✓ ASR Service is READY (headless)");
        Console.WriteLine("  Hold RIGHT ALT to record speech.");
        Console.WriteLine("  Double-tap RIGHT ALT to toggle continuous recording.");
        Console.WriteLine("  Press Ctrl+C to exit.");
        Console.WriteLine("═══════════════════════════════════════════════════════");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[Service] Shutting down...");
            cts.Cancel();
        };

        // WH_KEYBOARD_LL hooks require a message pump on the installing thread.
        var exitTimer = new System.Windows.Forms.Timer { Interval = 500 };
        exitTimer.Tick += (_, _) =>
        {
            if (cts.IsCancellationRequested)
            {
                exitTimer.Stop();
                controller.Dispose();
                Application.ExitThread();
            }
        };
        exitTimer.Start();

        Application.Run();
        return 0;
    }

    // ── CLI Commands ─────────────────────────────────────────────

    /// <summary>Transcribe a media file from the command line.</summary>
    private static async Task<int> RunTranscribeFile(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: AsrService.exe --transcribe <path-to-video-or-audio-file>");
            return 1;
        }

        using var controller = new AsrController();
        string text = await controller.TranscribeFileAsync(args[1]);

        if (!string.IsNullOrWhiteSpace(text))
        {
            Console.WriteLine();
            Console.WriteLine(text);
        }
        return 0;
    }

    private static async Task<int> RunDownloadModel()
    {
        await ModelDownloader.DownloadModelAsync();
        await ModelDownloader.DownloadSileroVadAsync();
        return 0;
    }

    private static int RunInstall()
    {
        StartupManager.Install();
        return 0;
    }

    private static int RunUninstall()
    {
        StartupManager.Uninstall();
        return 0;
    }

    private static int RunStatus()
    {
        var settings = AppSettings.Load();
        var model = ModelRegistry.GetById(settings.ModelId);
        bool startupInstalled = StartupManager.IsInstalled();

        Console.WriteLine("ASR Service Status");
        Console.WriteLine("──────────────────────────────────────");
        Console.WriteLine($"  Model:     {model.DisplayName}");
        Console.WriteLine($"             {(ModelRegistry.IsModelPresent(model) ? "✓ Installed" : "✗ Not found")}");
        Console.WriteLine($"  Path:      {ModelRegistry.GetModelDir(model)}");
        Console.WriteLine($"  VAD:       {(ModelRegistry.IsSileroVadPresent() ? "✓ Silero VAD installed" : "✗ Not found")}");
        Console.WriteLine($"  Source:    {settings.AudioDeviceName}{(settings.AudioDeviceIsLoopback ? " (loopback)" : "")}");
        Console.WriteLine($"  Language:  {settings.Language}");
        Console.WriteLine($"  Startup:   {(startupInstalled ? "✓ Registered" : "✗ Not registered")}");
        Console.WriteLine($"  Exe:       {Environment.ProcessPath}");
        Console.WriteLine("──────────────────────────────────────");

        return 0;
    }

    private static int RunHelp()
    {
        Console.WriteLine("ASR Service — Speech-to-Text for Windows");
        Console.WriteLine();
        Console.WriteLine("Usage: AsrService.exe [command]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  (no args)          Run with the settings GUI");
        Console.WriteLine("  --headless         Run without GUI (console only)");
        Console.WriteLine("  --transcribe <f>   Transcribe a video/audio file to text (uses FFmpeg)");
        Console.WriteLine("  --download-model   Download the selected ASR model + Silero VAD");
        Console.WriteLine("  --install          Register for Windows auto-startup");
        Console.WriteLine("  --uninstall        Remove from Windows auto-startup");
        Console.WriteLine("  --status           Show model and startup status");
        Console.WriteLine("  --help             Show this help message");
        Console.WriteLine();
        Console.WriteLine("Hotkeys:");
        Console.WriteLine("  Hold RIGHT ALT     Push-to-talk (release to transcribe & type)");
        Console.WriteLine("  Double-tap R-ALT   Toggle continuous recording (Silero VAD)");
        Console.WriteLine();
        return 0;
    }
}
