// ═══════════════════════════════════════════════════════════════════
//  VoiceIme.kt — The voice keyboard (InputMethodService)
//
//  A dictation-first keyboard in the spirit of FUTO's voice input:
//  a big mic button toggles continuous on-device dictation; text is
//  committed into whatever app has focus. Works everywhere GBoard
//  voice typing works, but runs Parakeet locally and survives noise
//  (see AudioPipeline + Silero VAD).
//
//  Layout:  [🌐 switch] [────── 🎤 mic ──────] [⌫] [space] [⏎]
// ═══════════════════════════════════════════════════════════════════

package com.asr.keyboard

import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.inputmethodservice.InputMethodService
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class VoiceIme : InputMethodService() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val audio = AudioPipeline()
    private lateinit var recognizer: Recognizer

    private var dictationJob: Job? = null
    private var micButton: Button? = null
    private var statusView: TextView? = null

    override fun onCreate() {
        super.onCreate()
        recognizer = Recognizer(this)
    }

    // ── Keyboard view ────────────────────────────────────────────

    override fun onCreateInputView(): View {
        val dark = Color.rgb(28, 28, 30)
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(dark)
            setPadding(16, 12, 16, 20)
        }

        statusView = TextView(this).apply {
            text = if (recognizer.isReady) "Ready" else "Tap 🎤 to load model & dictate"
            setTextColor(Color.LTGRAY)
            gravity = Gravity.CENTER
            textSize = 13f
        }
        root.addView(statusView, lp(matchParent = true, height = LinearLayout.LayoutParams.WRAP_CONTENT))

        val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }

        row.addView(makeKey("🌐") { switchToPreviousInputMethod() }, lp(weight = 1f))

        micButton = makeKey("🎤  Dictate") { toggleDictation() }.apply {
            textSize = 20f
        }
        row.addView(micButton, lp(weight = 3f))

        row.addView(makeKey("⌫") {
            currentInputConnection?.deleteSurroundingText(1, 0)
        }, lp(weight = 1f))
        row.addView(makeKey("␣") {
            currentInputConnection?.commitText(" ", 1)
        }, lp(weight = 1f))
        row.addView(makeKey("⏎") {
            currentInputConnection?.sendKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_ENTER))
            currentInputConnection?.sendKeyEvent(KeyEvent(KeyEvent.ACTION_UP, KeyEvent.KEYCODE_ENTER))
        }, lp(weight = 1f))

        root.addView(row, lp(matchParent = true, height = 220))
        return root
    }

    private fun makeKey(label: String, onClick: () -> Unit): Button =
        Button(this).apply {
            text = label
            setTextColor(Color.WHITE)
            setBackgroundColor(Color.rgb(58, 58, 62))
            setOnClickListener { onClick() }
        }

    private fun lp(weight: Float = 0f, matchParent: Boolean = false,
                   height: Int = LinearLayout.LayoutParams.MATCH_PARENT) =
        LinearLayout.LayoutParams(
            if (matchParent) LinearLayout.LayoutParams.MATCH_PARENT else 0,
            height, weight
        ).apply { setMargins(6, 6, 6, 6) }

    // ── Dictation ────────────────────────────────────────────────

    private fun toggleDictation() {
        if (dictationJob != null) { stopDictation(); return }

        if (ContextCompat.checkSelfPermission(this, android.Manifest.permission.RECORD_AUDIO)
            != PackageManager.PERMISSION_GRANTED
        ) {
            setStatus("Mic permission missing — opening setup app")
            startActivity(Intent(this, MainActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            return
        }

        dictationJob = scope.launch {
            try {
                // Lazy-load the model on first use
                if (!recognizer.isReady) {
                    val model = SettingsStore.selectedModel(this@VoiceIme)
                    if (!ModelManager.isPresent(this@VoiceIme, model)) {
                        setStatus("Model not downloaded — open the ASR Keyboard app first")
                        dictationJob = null
                        return@launch
                    }
                    setStatus("Loading model…")
                    recognizer.load(model)
                }

                audio.start()
                setStatus("● Listening — speak naturally")
                setMicActive(true)

                while (isActive) {
                    val chunk = audio.readChunk() ?: continue
                    recognizer.feed(chunk) { text -> commitText(text) }
                }
            } catch (e: Exception) {
                setStatus("Error: ${e.message}")
            } finally {
                audio.stop()
                recognizer.finish { text -> commitText(text) }
                setMicActive(false)
            }
        }
    }

    private fun stopDictation() {
        dictationJob?.cancel()
        dictationJob = null
        setStatus("Ready")
    }

    private fun commitText(text: String) {
        scope.launch(Dispatchers.Main) {
            currentInputConnection?.commitText("$text ", 1)
        }
    }

    private fun setStatus(msg: String) {
        scope.launch(Dispatchers.Main) { statusView?.text = msg }
    }

    private fun setMicActive(active: Boolean) {
        scope.launch(Dispatchers.Main) {
            micButton?.text = if (active) "■  Stop" else "🎤  Dictate"
            micButton?.setBackgroundColor(
                if (active) Color.rgb(178, 34, 34) else Color.rgb(58, 58, 62))
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────

    override fun onFinishInputView(finishingInput: Boolean) {
        stopDictation()
        super.onFinishInputView(finishingInput)
    }

    override fun onDestroy() {
        stopDictation()
        recognizer.release()
        scope.cancel()
        super.onDestroy()
    }
}
