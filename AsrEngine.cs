// ═══════════════════════════════════════════════════════════════════
//  AsrEngine.cs — Speech Recognition Engine
//  Wraps sherpa-onnx OfflineRecognizer for Parakeet TDT 0.6B v2 INT8.
// ═══════════════════════════════════════════════════════════════════

using SherpaOnnx;

namespace AsrService;

/// <summary>
/// Core ASR engine using sherpa-onnx OfflineRecognizer with the
/// NVIDIA Parakeet TDT 0.6B v2 INT8 ONNX model.
/// </summary>
public sealed class AsrEngine : IDisposable
{
    // ── Constants ────────────────────────────────────────────────
    private const int SampleRate = 16000;
    private const int NumThreads = 4; // Balanced for i7-1355U (10 threads total, leave headroom)
    private const string ModelType = "nemo_transducer";

    // ── State ────────────────────────────────────────────────────
    private OfflineRecognizer? _recognizer;
    private bool _disposed = false;

    /// <summary>True if the model is loaded and ready for inference.</summary>
    public bool IsReady => _recognizer != null;

    // ── Public API ───────────────────────────────────────────────

    /// <summary>
    /// Initialize the ASR engine with the model at the given directory.
    /// </summary>
    /// <param name="modelDir">
    /// Path to the extracted model directory containing
    /// encoder.int8.onnx, decoder.int8.onnx, joiner.int8.onnx, tokens.txt
    /// </param>
    public void LoadModel(string modelDir)
    {
        if (_recognizer != null)
        {
            Console.WriteLine("[ASR] Model already loaded, skipping.");
            return;
        }

        string encoder = Path.Combine(modelDir, "encoder.int8.onnx");
        string decoder = Path.Combine(modelDir, "decoder.int8.onnx");
        string joiner  = Path.Combine(modelDir, "joiner.int8.onnx");
        string tokens  = Path.Combine(modelDir, "tokens.txt");

        // Verify all model files exist
        foreach (var file in new[] { encoder, decoder, joiner, tokens })
        {
            if (!File.Exists(file))
                throw new FileNotFoundException($"Model file not found: {file}");
        }

        Console.WriteLine($"[ASR] Loading model from: {modelDir}");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Transducer.Encoder = encoder;
        config.ModelConfig.Transducer.Decoder = decoder;
        config.ModelConfig.Transducer.Joiner  = joiner;
        config.ModelConfig.Tokens    = tokens;
        config.ModelConfig.Provider  = "cpu";
        config.ModelConfig.NumThreads = NumThreads;
        config.ModelConfig.Debug     = 0;
        config.ModelConfig.ModelType = ModelType;
        config.DecodingMethod = "greedy_search";

        _recognizer = new OfflineRecognizer(config);

        sw.Stop();
        Console.WriteLine($"[ASR] Model loaded in {sw.Elapsed.TotalSeconds:F1}s (threads={NumThreads})");
    }

    /// <summary>
    /// Transcribe float audio samples (16kHz mono, [-1.0, 1.0]).
    /// Returns the recognized text, or empty string if nothing was recognized.
    /// </summary>
    /// <param name="samples">Audio samples at 16kHz, mono, float [-1.0, 1.0].</param>
    public string Transcribe(float[] samples)
    {
        if (_recognizer == null)
            throw new InvalidOperationException("Model not loaded. Call LoadModel() first.");

        if (samples.Length == 0) return string.Empty;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Create a stream and feed samples
        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(SampleRate, samples);

        // Run inference
        _recognizer.Decode(stream);
        string result = stream.Result.Text.Trim();

        sw.Stop();
        float audioDuration = (float)samples.Length / SampleRate;
        float rtf = (float)sw.Elapsed.TotalSeconds / audioDuration; // Real-Time Factor

        Console.WriteLine($"[ASR] Transcribed {audioDuration:F2}s audio in {sw.ElapsedMilliseconds}ms " +
                          $"(RTF={rtf:F3}, {1 / rtf:F1}x realtime)");

        if (!string.IsNullOrEmpty(result))
            Console.WriteLine($"[ASR] Text: \"{result}\"");

        return result;
    }

    // ── IDisposable ──────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            _recognizer?.Dispose();
            _recognizer = null;
            _disposed = true;
        }
    }
}
