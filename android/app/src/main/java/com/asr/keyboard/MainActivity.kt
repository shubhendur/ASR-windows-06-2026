// ═══════════════════════════════════════════════════════════════════
//  MainActivity.kt — Setup screen
//  1. Grant microphone permission
//  2. Pick and download a model (Parakeet / Nemotron)
//  3. Enable & select the ASR Voice Keyboard
// ═══════════════════════════════════════════════════════════════════

package com.asr.keyboard

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Bundle
import android.provider.Settings
import android.view.inputmethod.InputMethodManager
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.withContext

/** Tiny prefs wrapper shared with the IME. */
object SettingsStore {
    private const val PREFS = "asr_settings"

    fun selectedModel(ctx: Context): ModelInfo {
        val id = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getString("model_id", ModelManager.models[0].id)
        return ModelManager.models.firstOrNull { it.id == id } ?: ModelManager.models[0]
    }

    fun setSelectedModel(ctx: Context, model: ModelInfo) {
        ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .edit().putString("model_id", model.id).apply()
    }
}

class MainActivity : AppCompatActivity() {

    private lateinit var log: TextView
    private lateinit var spinner: Spinner

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val pad = (16 * resources.displayMetrics.density).toInt()
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(pad, pad, pad, pad)
        }

        root.addView(TextView(this).apply {
            text = "ASR Voice Keyboard — Setup"
            textSize = 22f
        })

        root.addView(TextView(this).apply {
            text = "On-device dictation that works in noisy environments.\n" +
                    "Steps: 1) mic permission  2) download model  3) enable keyboard."
            setPadding(0, pad / 2, 0, pad)
        })

        // 1. Mic permission
        root.addView(Button(this).apply {
            text = "1. Grant microphone permission"
            setOnClickListener {
                ActivityCompat.requestPermissions(
                    this@MainActivity, arrayOf(android.Manifest.permission.RECORD_AUDIO), 1)
            }
        })

        // 2. Model picker + download
        spinner = Spinner(this).apply {
            adapter = ArrayAdapter(this@MainActivity,
                android.R.layout.simple_spinner_dropdown_item, ModelManager.models)
            setSelection(ModelManager.models.indexOfFirst {
                it.id == SettingsStore.selectedModel(this@MainActivity).id
            }.coerceAtLeast(0))
        }
        root.addView(spinner)

        root.addView(Button(this).apply {
            text = "2. Download selected model"
            setOnClickListener { downloadSelected() }
        })

        // 3. Enable + switch keyboard
        root.addView(Button(this).apply {
            text = "3. Enable keyboard in system settings"
            setOnClickListener {
                startActivity(Intent(Settings.ACTION_INPUT_METHOD_SETTINGS))
            }
        })
        root.addView(Button(this).apply {
            text = "Switch keyboard now"
            setOnClickListener {
                (getSystemService(INPUT_METHOD_SERVICE) as InputMethodManager)
                    .showInputMethodPicker()
            }
        })

        log = TextView(this).apply {
            text = statusText()
            setPadding(0, pad, 0, 0)
            textSize = 13f
        }
        root.addView(log)

        setContentView(ScrollView(this).apply { addView(root) })
    }

    private fun statusText(): String {
        val model = SettingsStore.selectedModel(this)
        val micOk = ContextCompat.checkSelfPermission(
            this, android.Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED
        return buildString {
            appendLine("Mic permission: ${if (micOk) "✓ granted" else "✗ not granted"}")
            appendLine("Model: ${model.displayName}")
            appendLine("Installed: ${if (ModelManager.isPresent(this@MainActivity, model)) "✓" else "✗ (download needed)"}")
            if (model.notes.isNotEmpty()) appendLine("Note: ${model.notes}")
        }
    }

    private fun downloadSelected() {
        val model = spinner.selectedItem as ModelInfo
        SettingsStore.setSelectedModel(this, model)

        lifecycleScope.launch {
            try {
                ModelManager.download(this@MainActivity, model) { msg ->
                    runOnUiThread { log.text = "$msg\n\n${statusText()}" }
                }
                withContext(Dispatchers.Main) { log.text = "✓ Done.\n\n${statusText()}" }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) { log.text = "✗ Download failed: ${e.message}\n\n${statusText()}" }
            }
        }
    }

    override fun onRequestPermissionsResult(
        requestCode: Int, permissions: Array<out String>, grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        log.text = statusText()
    }
}
