// ═══════════════════════════════════════════════════════════════════
//  AudioPipeline.kt — Microphone capture tuned for noisy environments
//
//  Why Google keyboard performs badly in noise, and what we do instead:
//    1. Source  : VOICE_RECOGNITION — the OEM tuning meant for ASR
//                 (less aggressive processing than the default MIC).
//    2. Hardware: NoiseSuppressor + AutomaticGainControl +
//                 AcousticEchoCanceler platform effects when present.
//    3. Software: an adaptive gain stage that lifts speech peaks to
//                 ~95–100 % of full scale (Android exposes no "mic
//                 sensitivity %", so this is the equivalent), with a
//                 soft limiter so boosted audio never clips.
//    4. Down the line, Silero VAD gates out residual noise so only
//                 real speech reaches the model.
// ═══════════════════════════════════════════════════════════════════

package com.asr.keyboard

import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.media.audiofx.AcousticEchoCanceler
import android.media.audiofx.AutomaticGainControl
import android.media.audiofx.NoiseSuppressor
import android.util.Log
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min
import kotlin.math.tanh

class AudioPipeline {
    companion object {
        const val SAMPLE_RATE = 16_000
        private const val TAG = "AudioPipeline"

        /** Target peak level: 95–100 % of full scale, per user requirement. */
        private const val TARGET_PEAK = 0.97f
        private const val MAX_GAIN = 12f          // never boost more than ~21 dB
        private const val GAIN_ATTACK = 0.30f     // how fast gain rises  (per 100ms chunk)
        private const val GAIN_RELEASE = 0.05f    // how fast gain falls
    }

    private var record: AudioRecord? = null
    private var noiseSuppressor: NoiseSuppressor? = null
    private var agc: AutomaticGainControl? = null
    private var aec: AcousticEchoCanceler? = null

    private var currentGain = 4f                  // starting boost, adapts immediately

    val isRecording: Boolean get() = record?.recordingState == AudioRecord.RECORDSTATE_RECORDING

    @SuppressLint("MissingPermission") // caller checks RECORD_AUDIO
    fun start() {
        if (isRecording) return

        val minBuf = AudioRecord.getMinBufferSize(
            SAMPLE_RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT
        )

        val rec = AudioRecord(
            MediaRecorder.AudioSource.VOICE_RECOGNITION,
            SAMPLE_RATE,
            AudioFormat.CHANNEL_IN_MONO,
            AudioFormat.ENCODING_PCM_16BIT,
            max(minBuf, SAMPLE_RATE) // ≥ 0.5s of headroom
        )
        check(rec.state == AudioRecord.STATE_INITIALIZED) { "AudioRecord failed to initialize" }

        // ── Hardware effects (no-ops on devices without them) ────
        val session = rec.audioSessionId
        if (NoiseSuppressor.isAvailable()) {
            noiseSuppressor = NoiseSuppressor.create(session)?.apply { enabled = true }
            Log.i(TAG, "NoiseSuppressor enabled: ${noiseSuppressor?.enabled}")
        }
        if (AutomaticGainControl.isAvailable()) {
            agc = AutomaticGainControl.create(session)?.apply { enabled = true }
            Log.i(TAG, "AutomaticGainControl enabled: ${agc?.enabled}")
        }
        if (AcousticEchoCanceler.isAvailable()) {
            aec = AcousticEchoCanceler.create(session)?.apply { enabled = true }
        }

        rec.startRecording()
        record = rec
        Log.i(TAG, "Recording started (VOICE_RECOGNITION, 16kHz mono)")
    }

    /**
     * Blocking read of ~100ms of audio, returned as float [-1, 1] with
     * adaptive gain applied (peaks normalized toward 95–100 % FS).
     * Returns null when not recording.
     */
    fun readChunk(): FloatArray? {
        val rec = record ?: return null
        val shorts = ShortArray(SAMPLE_RATE / 10) // 100ms
        val n = rec.read(shorts, 0, shorts.size)
        if (n <= 0) return null

        val out = FloatArray(n)
        var peak = 0f
        for (i in 0 until n) {
            val s = shorts[i] / 32768f
            out[i] = s
            peak = max(peak, abs(s))
        }

        // ── Adaptive gain toward TARGET_PEAK ─────────────────────
        // Only track gain on frames that plausibly contain signal, so
        // silence doesn't drive the gain to maximum and amplify noise.
        if (peak > 0.008f) {
            val desired = min(TARGET_PEAK / peak, MAX_GAIN)
            val rate = if (desired < currentGain) GAIN_ATTACK else GAIN_RELEASE
            currentGain += (desired - currentGain) * rate
        }

        for (i in 0 until n) {
            val boosted = out[i] * currentGain
            // Soft limiter: linear below 90 % FS, tanh knee above —
            // guarantees |sample| < 1 with no hard clipping distortion.
            out[i] = if (abs(boosted) <= 0.9f) boosted
                     else (if (boosted > 0) 1f else -1f) * (0.9f + 0.1f * tanh((abs(boosted) - 0.9f) * 10f))
        }
        return out
    }

    fun stop() {
        noiseSuppressor?.release(); noiseSuppressor = null
        agc?.release(); agc = null
        aec?.release(); aec = null
        record?.let {
            try { it.stop() } catch (_: IllegalStateException) {}
            it.release()
        }
        record = null
        Log.i(TAG, "Recording stopped")
    }
}
