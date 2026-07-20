// ═══════════════════════════════════════════════════════════════════
//  ModelManager.kt — Model catalog & downloader (mirror of the
//  Windows ModelRegistry). Models live in the app's private files
//  dir — no storage permission, no root, no admin of any kind.
// ═══════════════════════════════════════════════════════════════════

package com.asr.keyboard

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.apache.commons.compress.archivers.tar.TarArchiveInputStream
import org.apache.commons.compress.compressors.bzip2.BZip2CompressorInputStream
import java.io.File
import java.io.FileOutputStream
import java.net.HttpURLConnection
import java.net.URL

data class ModelInfo(
    val id: String,
    val displayName: String,
    val dirName: String,
    /** tar.bz2 archive (sherpa releases), or null when [files] is used. */
    val archiveUrl: String? = null,
    /** Individual files (Hugging Face): url → local file name. */
    val files: List<Pair<String, String>> = emptyList(),
    val checkFile: String,
    val encoderFile: String,
    val decoderFile: String,
    val joinerFile: String,
    val tokensFile: String,
    val multilingual: Boolean = false,
    val experimental: Boolean = false,
    /** True = cache-aware streaming transducer (OnlineRecognizer, e.g. Nemotron 3.5). */
    val streaming: Boolean = false,
    val notes: String = "",
) {
    override fun toString() = displayName
}

object ModelManager {
    const val SILERO_VAD_URL =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx"

    val models = listOf(
        ModelInfo(
            id = "parakeet-tdt-0.6b-v2-int8",
            displayName = "Parakeet TDT 0.6B v2 (INT8) — English",
            dirName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8",
            archiveUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" +
                    "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8.tar.bz2",
            checkFile = "encoder.int8.onnx",
            encoderFile = "encoder.int8.onnx",
            decoderFile = "decoder.int8.onnx",
            joinerFile = "joiner.int8.onnx",
            tokensFile = "tokens.txt",
            notes = "Recommended. ~640 MB download, English only.",
        ),
        ModelInfo(
            id = "nemotron-3.5-asr-streaming-0.6b-int8",
            displayName = "Nemotron 3.5 ASR Streaming 0.6B (INT8) — 40 languages",
            dirName = "sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-320ms-int8-2026-06-11",
            archiveUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" +
                    "sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-320ms-int8-2026-06-11.tar.bz2",
            checkFile = "tokens.txt",
            // Streaming exports resolve model files by pattern at load time
            encoderFile = "encoder*.onnx",
            decoderFile = "decoder*.onnx",
            joinerFile = "joiner*.onnx",
            tokensFile = "tokens.txt",
            multilingual = true,
            streaming = true,
            notes = "Official sherpa-onnx export of nvidia/nemotron-3.5-asr-streaming-0.6b. " +
                    "Auto language detection, incl. English & Hindi.",
        ),
    )

    fun modelsRoot(ctx: Context): File = File(ctx.filesDir, "models")
    fun modelDir(ctx: Context, m: ModelInfo): File = File(modelsRoot(ctx), m.dirName)
    fun isPresent(ctx: Context, m: ModelInfo): Boolean = File(modelDir(ctx, m), m.checkFile).exists()
    fun sileroVadFile(ctx: Context): File = File(modelsRoot(ctx), "silero_vad.onnx")

    /** Download the given model (and Silero VAD) if missing. */
    suspend fun download(
        ctx: Context,
        model: ModelInfo,
        onProgress: (String) -> Unit,
    ) = withContext(Dispatchers.IO) {
        modelsRoot(ctx).mkdirs()

        if (!sileroVadFile(ctx).exists()) {
            onProgress("Downloading Silero VAD (~2 MB)...")
            downloadFile(SILERO_VAD_URL, sileroVadFile(ctx), onProgress)
        }

        if (isPresent(ctx, model)) {
            onProgress("Model already installed.")
            return@withContext
        }

        if (model.archiveUrl != null) {
            val archive = File(modelsRoot(ctx), "${model.dirName}.tar.bz2")
            try {
                onProgress("Downloading ${model.displayName}...")
                downloadFile(model.archiveUrl, archive, onProgress)
                onProgress("Extracting...")
                extractTarBz2(archive, modelsRoot(ctx))
            } finally {
                archive.delete()
            }
        } else {
            val dir = modelDir(ctx, model).apply { mkdirs() }
            for ((url, name) in model.files) {
                val dest = File(dir, name)
                if (dest.exists()) continue
                onProgress("Downloading $name...")
                val tmp = File(dir, "$name.part")
                downloadFile(url, tmp, onProgress)
                tmp.renameTo(dest)
            }
        }

        if (!isPresent(ctx, model)) error("Download finished but ${model.checkFile} is missing")
        onProgress("✓ Model ready.")
    }

    private fun downloadFile(url: String, dest: File, onProgress: (String) -> Unit) {
        var conn = URL(url).openConnection() as HttpURLConnection
        conn.instanceFollowRedirects = true
        // HttpURLConnection won't follow http→https or cross-host redirects automatically
        var redirects = 0
        while (conn.responseCode in 301..308 && redirects++ < 5) {
            val next = conn.getHeaderField("Location") ?: break
            conn.disconnect()
            conn = URL(next).openConnection() as HttpURLConnection
        }
        check(conn.responseCode == 200) { "HTTP ${conn.responseCode} for $url" }

        val total = conn.contentLengthLong
        conn.inputStream.use { input ->
            FileOutputStream(dest).use { output ->
                val buf = ByteArray(1 shl 16)
                var read: Int
                var done = 0L
                var lastPct = -1
                while (input.read(buf).also { read = it } > 0) {
                    output.write(buf, 0, read)
                    done += read
                    if (total > 0) {
                        val pct = (done * 100 / total).toInt()
                        if (pct != lastPct && pct % 5 == 0) {
                            lastPct = pct
                            onProgress("  $pct% (${done / 1_048_576} / ${total / 1_048_576} MB)")
                        }
                    }
                }
            }
        }
        conn.disconnect()
    }

    private fun extractTarBz2(archive: File, destDir: File) {
        archive.inputStream().buffered().use { fileIn ->
            BZip2CompressorInputStream(fileIn).use { bzIn ->
                TarArchiveInputStream(bzIn).use { tarIn ->
                    var entry = tarIn.nextEntry
                    while (entry != null) {
                        val out = File(destDir, entry.name)
                        // Zip-slip guard
                        check(out.canonicalPath.startsWith(destDir.canonicalPath)) {
                            "Illegal archive path: ${entry.name}"
                        }
                        if (entry.isDirectory) out.mkdirs()
                        else {
                            out.parentFile?.mkdirs()
                            FileOutputStream(out).use { tarIn.copyTo(it) }
                        }
                        entry = tarIn.nextEntry
                    }
                }
            }
        }
    }
}
