package com.example.voicetotext.ui.main

import android.app.Activity
import android.content.Intent
import android.speech.RecognizerIntent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.navigation3.runtime.NavKey
import com.example.voicetotext.ModelDownloader
import com.example.voicetotext.theme.VoiceToTextTheme

@Composable
fun MainScreen(
    onItemClick: (NavKey) -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    var recognizedText by remember { mutableStateOf("Press the mic button and start speaking...") }
    var downloadState by remember { mutableStateOf<ModelDownloader.DownloadState>(ModelDownloader.DownloadState.Idle) }

    val speechRecognizerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK) {
            val data = result.data
            val results = data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)
            if (!results.isNullOrEmpty()) {
                recognizedText = results[0]
            }
        }
    }

    LaunchedEffect(Unit) {
        if (!ModelDownloader.isModelPresent(context)) {
            ModelDownloader.downloadAndExtractModel(context).collect { state ->
                downloadState = state
            }
        } else {
            downloadState = ModelDownloader.DownloadState.Success
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(16.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            when (val state = downloadState) {
                is ModelDownloader.DownloadState.Idle -> {
                    Text("Checking model...")
                }
                is ModelDownloader.DownloadState.Downloading -> {
                    Text("Downloading model: ${state.progress}%")
                    CircularProgressIndicator(modifier = Modifier.padding(top = 16.dp))
                }
                is ModelDownloader.DownloadState.Extracting -> {
                    Text("Extracting model archive...")
                    CircularProgressIndicator(modifier = Modifier.padding(top = 16.dp))
                }
                is ModelDownloader.DownloadState.Error -> {
                    Text("Error: ${state.message}", color = MaterialTheme.colorScheme.error)
                }
                is ModelDownloader.DownloadState.Success -> {
                    Text(
                        text = recognizedText,
                        style = MaterialTheme.typography.headlineMedium,
                        textAlign = TextAlign.Center
                    )
                }
            }
        }

        if (downloadState is ModelDownloader.DownloadState.Success) {
            FloatingActionButton(
                onClick = {
                    val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
                        putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
                        putExtra(RecognizerIntent.EXTRA_PROMPT, "Speak now...")
                    }
                    speechRecognizerLauncher.launch(intent)
                },
                modifier = Modifier.padding(bottom = 32.dp)
            ) {
                Text(text = "🎙️")
            }
        }
    }
}

@Preview(showBackground = true)
@Composable
fun MainScreenPreview() {
    VoiceToTextTheme { MainScreen(onItemClick = {}) }
}
