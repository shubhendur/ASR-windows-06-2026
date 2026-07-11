package com.example.voicetotext

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOn
import okhttp3.OkHttpClient
import okhttp3.Request
import org.apache.commons.compress.archivers.tar.TarArchiveEntry
import org.apache.commons.compress.archivers.tar.TarArchiveInputStream
import org.apache.commons.compress.compressors.bzip2.BZip2CompressorInputStream
import java.io.File
import java.io.FileOutputStream
import java.io.InputStream
import java.util.concurrent.TimeUnit

object ModelDownloader {
    private const val MODEL_URL = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8.tar.bz2"
    private const val MODEL_DIR_NAME = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8"
    private const val CHECK_FILE = "encoder.int8.onnx"

    fun getModelDir(context: Context): File {
        return File(context.filesDir, "models/$MODEL_DIR_NAME")
    }

    fun isModelPresent(context: Context): Boolean {
        val modelDir = getModelDir(context)
        return File(modelDir, CHECK_FILE).exists()
    }

    sealed class DownloadState {
        object Idle : DownloadState()
        data class Downloading(val progress: Int) : DownloadState()
        object Extracting : DownloadState()
        object Success : DownloadState()
        data class Error(val message: String) : DownloadState()
    }

    fun downloadAndExtractModel(context: Context): Flow<DownloadState> = flow {
        val modelDir = getModelDir(context)
        val modelsRoot = modelDir.parentFile ?: return@flow

        if (isModelPresent(context)) {
            emit(DownloadState.Success)
            return@flow
        }

        if (!modelsRoot.exists()) {
            modelsRoot.mkdirs()
        }

        val archiveFile = File(modelsRoot, "model-download.tar.bz2")

        try {
            emit(DownloadState.Downloading(0))

            val client = OkHttpClient.Builder()
                .connectTimeout(30, TimeUnit.MINUTES)
                .readTimeout(30, TimeUnit.MINUTES)
                .build()

            val request = Request.Builder().url(MODEL_URL).build()
            val response = client.newCall(request).execute()

            if (!response.isSuccessful) {
                throw Exception("Failed to download file: ${response.code}")
            }

            val body = response.body ?: throw Exception("Empty response body")
            val totalBytes = body.contentLength()
            var downloadedBytes = 0L

            body.byteStream().use { input ->
                FileOutputStream(archiveFile).use { output ->
                    val buffer = ByteArray(81920)
                    var bytesRead: Int
                    var lastProgress = 0

                    while (input.read(buffer).also { bytesRead = it } != -1) {
                        output.write(buffer, 0, bytesRead)
                        downloadedBytes += bytesRead

                        if (totalBytes > 0) {
                            val progress = ((downloadedBytes * 100) / totalBytes).toInt()
                            if (progress > lastProgress) {
                                lastProgress = progress
                                emit(DownloadState.Downloading(progress))
                            }
                        }
                    }
                }
            }

            emit(DownloadState.Extracting)
            extractArchive(archiveFile, modelsRoot)

            if (isModelPresent(context)) {
                emit(DownloadState.Success)
            } else {
                emit(DownloadState.Error("Extraction completed but model files not found!"))
            }

        } catch (e: Exception) {
            emit(DownloadState.Error(e.message ?: "Unknown error"))
        } finally {
            if (archiveFile.exists()) {
                archiveFile.delete()
            }
        }
    }.flowOn(Dispatchers.IO)

    private fun extractArchive(archiveFile: File, destDir: File) {
        archiveFile.inputStream().use { fileInput ->
            BZip2CompressorInputStream(fileInput).use { bzIn ->
                TarArchiveInputStream(bzIn).use { tarIn ->
                    var entry: TarArchiveEntry?
                    while (tarIn.nextEntry.also { entry = it as TarArchiveEntry? } != null) {
                        val e = entry!!
                        val destFile = File(destDir, e.name)
                        
                        if (e.isDirectory) {
                            destFile.mkdirs()
                        } else {
                            destFile.parentFile?.mkdirs()
                            FileOutputStream(destFile).use { out ->
                                val buffer = ByteArray(8192)
                                var bytesRead: Int
                                while (tarIn.read(buffer).also { bytesRead = it } != -1) {
                                    out.write(buffer, 0, bytesRead)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
