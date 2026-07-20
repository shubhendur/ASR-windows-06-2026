// ═══════════════════════════════════════════════════════════════════
//  ModelRegistry.cs — Catalog of supported ASR models
//  Each entry knows where to download from, what files to expect,
//  and how the engine should load it.
// ═══════════════════════════════════════════════════════════════════

namespace AsrService;

/// <summary>How the engine should load a model.</summary>
public enum ModelLoader
{
    /// <summary>sherpa-onnx OfflineRecognizer, nemo_transducer (encoder/decoder/joiner + tokens.txt).</summary>
    SherpaNemoTransducerInt8,

    /// <summary>sherpa-onnx OnlineRecognizer — cache-aware streaming transducer
    /// (official sherpa-onnx Nemotron 3.5 export, multilingual via per-stream language option).</summary>
    SherpaNemotronStreaming,

    /// <summary>Cannot be run by this app (e.g. safetensors / NeMo checkpoint).</summary>
    Unsupported,
}

/// <summary>Static description of one downloadable ASR model.</summary>
public sealed class ModelInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string DirName { get; init; }
    public required ModelLoader Loader { get; init; }

    /// <summary>tar.bz2 archive URL (sherpa-onnx releases) — mutually exclusive with Files.</summary>
    public string? ArchiveUrl { get; init; }

    /// <summary>Individual files to download (Hugging Face), relative to DirName.</summary>
    public (string Url, string FileName)[]? Files { get; init; }

    /// <summary>File whose presence means the model is installed.</summary>
    public required string CheckFile { get; init; }

    public bool Multilingual { get; init; }
    public bool Experimental { get; init; }
    public string Notes { get; init; } = "";

    public override string ToString() => DisplayName;
}

/// <summary>The catalog of models offered in the GUI dropdown.</summary>
public static class ModelRegistry
{
    public const string DefaultModelId = "parakeet-tdt-0.6b-v2-int8";

    public static readonly ModelInfo[] Models =
    {
        new ModelInfo
        {
            Id          = DefaultModelId,
            DisplayName = "NVIDIA Parakeet TDT 0.6B v2 (INT8) — English",
            DirName     = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8",
            Loader      = ModelLoader.SherpaNemoTransducerInt8,
            ArchiveUrl  = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" +
                          "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8.tar.bz2",
            CheckFile   = "encoder.int8.onnx",
            Multilingual = false,
            Notes       = "Default, fully supported. English only — the language selector is ignored.",
        },
        new ModelInfo
        {
            Id          = "nemotron-3.5-asr-streaming-0.6b-int8",
            DisplayName = "NVIDIA Nemotron 3.5 ASR Streaming 0.6B (INT8) — 40 languages",
            DirName     = "sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-320ms-int8-2026-06-11",
            Loader      = ModelLoader.SherpaNemotronStreaming,
            ArchiveUrl  = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" +
                          "sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-320ms-int8-2026-06-11.tar.bz2",
            CheckFile   = "tokens.txt",
            Multilingual = true,
            Notes       = "Official sherpa-onnx export of nvidia/nemotron-3.5-asr-streaming-0.6b " +
                          "(streaming, 320ms chunks). Supports auto language detection or a fixed " +
                          "language from the dropdown, incl. English & Hindi. Requires sherpa-onnx ≥ 1.13.4.",
        },
    };

    public static ModelInfo GetById(string id) =>
        Models.FirstOrDefault(m => m.Id == id) ?? Models[0];

    /// <summary>Root directory holding all models.</summary>
    public static string ModelsRoot
    {
        get
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "AsrService", "models");
        }
    }

    public static string GetModelDir(ModelInfo model) => Path.Combine(ModelsRoot, model.DirName);

    public static bool IsModelPresent(ModelInfo model) =>
        File.Exists(Path.Combine(GetModelDir(model), model.CheckFile));

    // ── Silero VAD model ─────────────────────────────────────────

    public const string SileroVadUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx";

    public static string SileroVadPath => Path.Combine(ModelsRoot, "silero_vad.onnx");

    public static bool IsSileroVadPresent() => File.Exists(SileroVadPath);

    // ── TEN VAD model ────────────────────────────────────────────
    // TEN VAD (TEN-framework) — ~32% lower RTF than Silero and a faster
    // speech-onset trigger. Use the sherpa-onnx-exported model (the raw
    // ten-vad.onnx from the TEN repo errors out inside sherpa-onnx).

    public const string TenVadUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/ten-vad.onnx";

    public static string TenVadPath => Path.Combine(ModelsRoot, "ten-vad.onnx");

    public static bool IsTenVadPresent() => File.Exists(TenVadPath);
}
