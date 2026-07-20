// ═══════════════════════════════════════════════════════════════════
//  AppSettings.cs — Persisted user settings
//  Stored as JSON at %LOCALAPPDATA%\AsrService\settings.json
// ═══════════════════════════════════════════════════════════════════

using System.Text.Json;

namespace AsrService;

/// <summary>
/// User-configurable settings selected in the GUI, persisted to disk.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Audio device id ("" = default microphone).</summary>
    public string AudioDeviceId { get; set; } = "";

    /// <summary>True when the selected source is a render device captured via WASAPI loopback (speaker audio).</summary>
    public bool AudioDeviceIsLoopback { get; set; } = false;

    /// <summary>Friendly name of the selected device (display only).</summary>
    public string AudioDeviceName { get; set; } = "Default Microphone";

    /// <summary>Model id from ModelRegistry.</summary>
    public string ModelId { get; set; } = ModelRegistry.DefaultModelId;

    /// <summary>Language code: "auto", "en", "hi", "es", "fr", "de", "it", "pt", "ja", "ko".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Voice-activity detector engine: "silero" (default) or "ten".</summary>
    public string VadEngine { get; set; } = "silero";

    // ── Silero VAD tuning ────────────────────────────────────────
    public float VadThreshold          { get; set; } = 0.5f;
    public float VadMinSilenceSeconds  { get; set; } = 1.0f;
    public float VadMinSpeechSeconds   { get; set; } = 0.25f;
    public float VadMaxSpeechSeconds   { get; set; } = 20f;

    // ── Persistence ──────────────────────────────────────────────

    private static string SettingsPath
    {
        get
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "AsrService", "settings.json");
        }
    }

    public static AppSettings Load()
    {
        AppSettings settings = new();
        try
        {
            if (File.Exists(SettingsPath))
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? settings;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Settings] Failed to load settings, using defaults: {ex.Message}");
        }

        // The audio source is a per-session choice: every start begins with
        // the default microphone. (Persisting e.g. a speaker-loopback source
        // across restarts caused silent "why is my mic not working" states.)
        settings.AudioDeviceId = "";
        settings.AudioDeviceIsLoopback = false;
        settings.AudioDeviceName = "Default Microphone";

        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Settings] Failed to save settings: {ex.Message}");
        }
    }
}

/// <summary>Voice-activity detectors offered in the GUI dropdown.</summary>
public static class VadCatalog
{
    public static readonly (string Code, string Display)[] Engines =
    {
        ("silero", "Silero VAD (accurate, default)"),
        ("ten",    "TEN VAD (faster onset, lower CPU)"),
    };
}

/// <summary>Languages offered in the GUI dropdown.</summary>
public static class LanguageCatalog
{
    public static readonly (string Code, string Display)[] Languages =
    {
        ("auto", "Auto-detect"),
        ("en",   "English"),
        ("hi",   "Hindi"),
        ("es",   "Spanish"),
        ("fr",   "French"),
        ("de",   "German"),
        ("it",   "Italian"),
        ("pt",   "Portuguese"),
        ("ja",   "Japanese"),
        ("ko",   "Korean"),
    };
}
