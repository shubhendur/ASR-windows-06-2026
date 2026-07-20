// ═══════════════════════════════════════════════════════════════════
//  Recognizer.kt — sherpa-onnx OfflineRecognizer + Silero VAD
//  Kotlin mirror of the Windows AsrEngine/AsrController VAD flow.
// ═══════════════════════════════════════════════════════════════════

package com.asr.keyboard

import android.content.Context
import android.util.Log
import com.k2fsa.sherpa.onnx.OfflineModelConfig
import com.k2fsa.sherpa.onnx.OfflineRecognizer
import com.k2fsa.sherpa.onnx.OfflineRecognizerConfig
import com.k2fsa.sherpa.onnx.OfflineTransducerModelConfig
import com.k2fsa.sherpa.onnx.OnlineRecognizer
import com.k2fsa.sherpa.onnx.OnlineRecognizerConfig
import com.k2fsa.sherpa.onnx.SileroVadModelConfig
import com.k2fsa.sherpa.onnx.Vad
import com.k2fsa.sherpa.onnx.VadModelConfig
import java.io.File

/** Wraps model loading, VAD segmentation and transcription. */
class Recognizer(private val ctx: Context) {
    companion object {
        private const val TAG = "Recognizer"
        const val SAMPLE_RATE = 16_000
    }

    private var recognizer: OfflineRecognizer? = null
    private var onlineRecognizer: OnlineRecognizer? = null
    private var vad: Vad? = null
    var loadedModel: ModelInfo? = null
        private set

    val isReady: Boolean get() = recognizer != null || onlineRecognizer != null

    /** Resolve a model file that may be specified as a glob (e.g. "encoder*.onnx"). */
    private fun resolve(dir: File, pattern: String): String {
        if (!pattern.contains('*')) return File(dir, pattern).absolutePath
        val regex = Regex("^" + pattern.replace(".", "\\.").replace("*", ".*") + "$")
        return dir.listFiles()?.firstOrNull { regex.matches(it.name) }?.absolutePath
            ?: error("Model file not found: $dir/$pattern")
    }

    /** Load (or switch) the ASR model + Silero VAD. Call off the main thread. */
    fun load(model: ModelInfo) {
        if (loadedModel?.id == model.id && isReady) return
        release()

        val dir = ModelManager.modelDir(ctx, model)
        Log.i(TAG, "Loading ${model.displayName} from $dir")

        if (model.streaming) {
            // Cache-aware streaming transducer (Nemotron 3.5) → OnlineRecognizer
            val config = OnlineRecognizerConfig().apply {
                modelConfig.transducer.encoder = resolve(dir, model.encoderFile)
                modelConfig.transducer.decoder = resolve(dir, model.decoderFile)
                modelConfig.transducer.joiner = resolve(dir, model.joinerFile)
                modelConfig.tokens = resolve(dir, model.tokensFile)
                modelConfig.numThreads = 4
                modelConfig.provider = "cpu"
                decodingMethod = "greedy_search"
            }
            onlineRecognizer = OnlineRecognizer(assetManager = null, config = config)
        } else {
            val config = OfflineRecognizerConfig(
                modelConfig = OfflineModelConfig(
                    transducer = OfflineTransducerModelConfig(
                        encoder = "$dir/${model.encoderFile}",
                        decoder = "$dir/${model.decoderFile}",
                        joiner = "$dir/${model.joinerFile}",
                    ),
                    tokens = "$dir/${model.tokensFile}",
                    modelType = "nemo_transducer",
                    numThreads = 4,   // big.LITTLE friendly; leaves cores for the audio thread
                    provider = "cpu",
                ),
            )
            recognizer = OfflineRecognizer(assetManager = null, config = config)
        }

        vad = Vad(
            assetManager = null,
            config = VadModelConfig(
                sileroVadModelConfig = SileroVadModelConfig(
                    model = ModelManager.sileroVadFile(ctx).absolutePath,
                    threshold = 0.5f,
                    minSilenceDuration = 0.8f,
                    minSpeechDuration = 0.25f,
                    maxSpeechDuration = 20f,
                    windowSize = 512,
                ),
                sampleRate = SAMPLE_RATE,
                numThreads = 1,
                provider = "cpu",
            ),
        )

        loadedModel = model
        Log.i(TAG, "Model + Silero VAD loaded")
    }

    /**
     * Feed a chunk of gain-normalized audio into the VAD. Every time a
     * complete speech segment is detected, it is transcribed and passed
     * to [onText].
     */
    fun feed(chunk: FloatArray, onText: (String) -> Unit) {
        val v = vad ?: return
        v.acceptWaveform(chunk)
        drain(v, onText)
    }

    /** Flush trailing speech when recording stops. */
    fun finish(onText: (String) -> Unit) {
        val v = vad ?: return
        v.flush()
        drain(v, onText)
        v.reset()
    }

    private fun drain(v: Vad, onText: (String) -> Unit) {
        if (!isReady) return
        while (!v.empty()) {
            val segment = v.front()
            v.pop()
            if (segment.samples.size < SAMPLE_RATE / 5) continue // < 200ms

            val t0 = System.currentTimeMillis()
            val text = transcribe(segment.samples)

            Log.i(TAG, "Segment ${segment.samples.size / SAMPLE_RATE.toFloat()}s → " +
                    "\"$text\" (${System.currentTimeMillis() - t0}ms)")
            if (text.isNotEmpty()) onText(text)
        }
    }

    private fun transcribe(samples: FloatArray): String {
        recognizer?.let { rec ->
            val stream = rec.createStream()
            stream.acceptWaveform(samples, SAMPLE_RATE)
            rec.decode(stream)
            val text = rec.getResult(stream).text.trim()
            stream.release()
            return text
        }
        onlineRecognizer?.let { rec ->
            val stream = rec.createStream()
            // "auto" language detection is the default for multilingual
            // Nemotron; a fixed language can be set per-stream:
            // stream.setOption("language", "hi")
            stream.acceptWaveform(samples, SAMPLE_RATE)
            stream.acceptWaveform(FloatArray(SAMPLE_RATE * 3 / 2), SAMPLE_RATE) // flush encoder cache (> chunk + right context)
            stream.inputFinished()
            while (rec.isReady(stream)) rec.decode(stream)
            val text = rec.getResult(stream).text.trim()
            stream.release()
            return text
        }
        return ""
    }

    fun release() {
        recognizer?.release(); recognizer = null
        onlineRecognizer?.release(); onlineRecognizer = null
        vad?.release(); vad = null
        loadedModel = null
    }
}
