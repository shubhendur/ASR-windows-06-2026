// ═══════════════════════════════════════════════════════════════════
//  AsrEngine.cs — Speech Recognition Engine
//  Wraps sherpa-onnx OfflineRecognizer. Loads any model described
//  by ModelRegistry (Parakeet TDT INT8, Nemotron ONNX INT4).
// ═══════════════════════════════════════════════════════════════════

using SherpaOnnx;

namespace AsrService;

/// <summary>
/// Core ASR engine using sherpa-onnx OfflineRecognizer.
/// </summary>
public sealed class AsrEngine : IDisposable
{
    // ── Constants ────────────────────────────────────────────────
    private const int SampleRate = 16000;
    // i7-1355U: 2 P-cores + 8 E-cores (12 hardware threads). 4 threads
    // keeps latency low without starving the UI/audio threads.
    private const int NumThreads = 4;

    // ── State ────────────────────────────────────────────────────
    private OfflineRecognizer? _recognizer;        // batch models (Parakeet)
    private OnlineRecognizer? _onlineRecognizer;   // streaming models (Nemotron 3.5)
    private ModelInfo? _loadedModel;
    private bool _disposed = false;

    /// <summary>True if a model is loaded and ready for inference.</summary>
    public bool IsReady => _recognizer != null || _onlineRecognizer != null;

    /// <summary>The currently loaded model, or null.</summary>
    public ModelInfo? LoadedModel => _loadedModel;

    /// <summary>Selected language code ("auto", "en", "hi", ...).</summary>
    public string Language { get; set; } = "auto";

    // ── Public API ───────────────────────────────────────────────

    /// <summary>Load (or switch to) the given model.</summary>
    public void LoadModel(ModelInfo model)
    {
        if (_loadedModel?.Id == model.Id && _recognizer != null)
        {
            Console.WriteLine("[ASR] Model already loaded, skipping.");
            return;
        }

        // Release the previous model before loading a new one
        _recognizer?.Dispose();
        _recognizer = null;
        _onlineRecognizer?.Dispose();
        _onlineRecognizer = null;
        _loadedModel = null;

        string modelDir = ModelRegistry.GetModelDir(model);
        Console.WriteLine($"[ASR] Loading model \"{model.DisplayName}\"");
        Console.WriteLine($"[ASR] From: {modelDir}");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        switch (model.Loader)
        {
            case ModelLoader.SherpaNemoTransducerInt8:
                _recognizer = new OfflineRecognizer(BuildParakeetConfig(modelDir));
                break;

            case ModelLoader.SherpaNemotronStreaming:
                _onlineRecognizer = new OnlineRecognizer(BuildNemotronStreamingConfig(modelDir));
                break;

            default:
                throw new NotSupportedException(
                    $"\"{model.DisplayName}\" cannot be run by this app. {model.Notes}");
        }

        _loadedModel = model;
        sw.Stop();
        Console.WriteLine($"[ASR] Model loaded in {sw.Elapsed.TotalSeconds:F1}s (threads={NumThreads})");

        if (!model.Multilingual && Language != "auto" && Language != "en")
            Console.WriteLine($"[ASR] Note: this model is English-only; language \"{Language}\" is ignored.");
    }

    /// <summary>Legacy overload — loads the default Parakeet model from a directory.</summary>
    public void LoadModel(string modelDir)
    {
        LoadModel(ModelRegistry.GetById(ModelRegistry.DefaultModelId));
    }

    private static OfflineRecognizerConfig BuildParakeetConfig(string modelDir)
    {
        string encoder = Path.Combine(modelDir, "encoder.int8.onnx");
        string decoder = Path.Combine(modelDir, "decoder.int8.onnx");
        string joiner  = Path.Combine(modelDir, "joiner.int8.onnx");
        string tokens  = Path.Combine(modelDir, "tokens.txt");

        foreach (var file in new[] { encoder, decoder, joiner, tokens })
            if (!File.Exists(file))
                throw new FileNotFoundException($"Model file not found: {file}");

        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Transducer.Encoder = encoder;
        config.ModelConfig.Transducer.Decoder = decoder;
        config.ModelConfig.Transducer.Joiner  = joiner;
        config.ModelConfig.Tokens     = tokens;
        config.ModelConfig.Provider   = "cpu";
        config.ModelConfig.NumThreads = NumThreads;
        config.ModelConfig.Debug      = 0;
        config.ModelConfig.ModelType  = "nemo_transducer";
        config.DecodingMethod = "greedy_search";
        return config;
    }

    private static OnlineRecognizerConfig BuildNemotronStreamingConfig(string modelDir)
    {
        // The sherpa-onnx export names files like encoder.int8.onnx — resolve
        // by pattern so date-stamped re-exports keep working.
        string FindFile(string pattern)
        {
            string? f = Directory.EnumerateFiles(modelDir, pattern).OrderBy(p => p.Length).FirstOrDefault();
            return f ?? throw new FileNotFoundException($"Model file not found: {modelDir}\\{pattern}");
        }

        var config = new OnlineRecognizerConfig();
        config.ModelConfig.Transducer.Encoder = FindFile("encoder*.onnx");
        config.ModelConfig.Transducer.Decoder = FindFile("decoder*.onnx");
        config.ModelConfig.Transducer.Joiner  = FindFile("joiner*.onnx");
        config.ModelConfig.Tokens     = FindFile("tokens.txt");
        config.ModelConfig.Provider   = "cpu";
        config.ModelConfig.NumThreads = NumThreads;
        config.ModelConfig.Debug      = 0;
        // Nemotron 3.5 requires model_type="nemotron" and an 128-dim feature
        // extractor. Without these the encoder runs on wrong-shaped features
        // and produces garbage / empty output (this is why the model appeared
        // "not used"). Only greedy_search is currently supported for nemotron.
        config.ModelConfig.ModelType  = "nemotron";
        config.FeatConfig.SampleRate  = SampleRate;
        config.FeatConfig.FeatureDim  = 128;
        config.DecodingMethod = "greedy_search";
        return config;
    }

    /// <summary>
    /// Transcribe float audio samples (16kHz mono, [-1.0, 1.0]).
    /// Returns the recognized text, or empty string if nothing was recognized.
    /// </summary>
    public string Transcribe(float[] samples)
    {
        if (!IsReady)
            throw new InvalidOperationException("Model not loaded. Call LoadModel() first.");

        if (samples.Length == 0) return string.Empty;

        // Note: transcribed text is deliberately NOT logged (privacy).
        return _recognizer != null
            ? TranscribeOffline(_recognizer, samples)
            : TranscribeStreaming(_onlineRecognizer!, samples);
    }

    private static string TranscribeOffline(OfflineRecognizer recognizer, float[] samples)
    {
        using var stream = recognizer.CreateStream();
        stream.AcceptWaveform(SampleRate, samples);
        recognizer.Decode(stream);
        return stream.Result.Text.Trim();
    }

    /// <summary>
    /// Runs a streaming (cache-aware) model over a complete utterance.
    /// For multilingual Nemotron 3.5, the language is passed per-stream:
    /// "auto" enables the model's built-in language detection.
    /// </summary>
    private string TranscribeStreaming(OnlineRecognizer recognizer, float[] samples)
    {
        using var stream = recognizer.CreateStream();

        if (Language != "auto" && !string.IsNullOrEmpty(Language))
            stream.SetOption("language", Language);

        // Mirror the official sherpa-onnx online-decode example:
        // left padding + samples + tail padding, InputFinished, then
        // decode until the stream is drained.
        stream.AcceptWaveform(SampleRate, new float[(int)(SampleRate * 0.3f)]);
        stream.AcceptWaveform(SampleRate, samples);
        stream.AcceptWaveform(SampleRate, new float[(int)(SampleRate * 0.8f)]);
        stream.InputFinished();

        while (recognizer.IsReady(stream))
            recognizer.Decode(stream);

        return recognizer.GetResult(stream).Text.Trim();
    }

    // ── IDisposable ──────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            _recognizer?.Dispose();
            _recognizer = null;
            _onlineRecognizer?.Dispose();
            _onlineRecognizer = null;
            _disposed = true;
        }
    }
}
