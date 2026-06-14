// ═══════════════════════════════════════════════════════════════════
//  ModelDownloader.cs — Download Parakeet TDT 0.6B v2 INT8 ONNX
//  Downloads and extracts the model from sherpa-onnx GitHub releases.
// ═══════════════════════════════════════════════════════════════════

using SharpCompress.Archives;
using SharpCompress.Common;

namespace AsrService;

/// <summary>
/// Downloads the NVIDIA Parakeet TDT 0.6B v2 INT8 ONNX model bundle
/// from the sherpa-onnx GitHub releases page and extracts it.
/// </summary>
public static class ModelDownloader
{
    // ── Constants ────────────────────────────────────────────────
    private const string ModelUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/" +
        "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8.tar.bz2";

    private const string ModelDirName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
    private const string CheckFile = "encoder.int8.onnx"; // Presence = model is extracted

    /// <summary>
    /// Returns the default model directory path under %LOCALAPPDATA%.
    /// </summary>
    public static string GetDefaultModelDir()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "AsrService", "models", ModelDirName);
    }

    /// <summary>
    /// Returns true if the model files already exist at the given path.
    /// </summary>
    public static bool IsModelPresent(string modelDir)
    {
        return File.Exists(Path.Combine(modelDir, CheckFile));
    }

    /// <summary>
    /// Downloads and extracts the model. Shows progress in the console.
    /// Skips if model files already exist.
    /// </summary>
    public static async Task DownloadModelAsync(string? targetDir = null)
    {
        string modelDir = targetDir ?? GetDefaultModelDir();
        string modelsRoot = Path.GetDirectoryName(modelDir)!;

        if (IsModelPresent(modelDir))
        {
            Console.WriteLine($"[Download] Model already exists at: {modelDir}");
            Console.WriteLine("[Download] Skipping download. Delete the folder to re-download.");
            return;
        }

        Directory.CreateDirectory(modelsRoot);

        string archivePath = Path.Combine(modelsRoot, "model-download.tar.bz2");

        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine("  Downloading NVIDIA Parakeet TDT 0.6B v2 (INT8 ONNX)");
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine($"  URL:    {ModelUrl}");
        Console.WriteLine($"  Target: {modelDir}");
        Console.WriteLine();

        try
        {
            // ── Download ─────────────────────────────────────────
            await DownloadFileAsync(ModelUrl, archivePath);

            // ── Extract ──────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("[Download] Extracting archive...");
            ExtractArchive(archivePath, modelsRoot);

            // ── Verify ───────────────────────────────────────────
            if (IsModelPresent(modelDir))
            {
                Console.WriteLine();
                Console.WriteLine("[Download] ✓ Model downloaded and extracted successfully!");
                Console.WriteLine($"[Download] Location: {modelDir}");

                // List key files
                var keyFiles = new[] { "encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt" };
                foreach (var f in keyFiles)
                {
                    var fi = new FileInfo(Path.Combine(modelDir, f));
                    if (fi.Exists)
                        Console.WriteLine($"  ├── {f} ({fi.Length / (1024 * 1024.0):F1} MB)");
                }
            }
            else
            {
                Console.Error.WriteLine("[Download] ✗ Extraction completed but model files not found!");
                Console.Error.WriteLine($"[Download] Expected: {Path.Combine(modelDir, CheckFile)}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Download] ✗ Failed: {ex.Message}");
            throw;
        }
        finally
        {
            // Clean up the archive file
            if (File.Exists(archivePath))
            {
                try { File.Delete(archivePath); }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }

    // ── Download with Progress ───────────────────────────────────

    private static async Task DownloadFileAsync(string url, string destPath)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(30); // Large file

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        string totalStr = totalBytes.HasValue
            ? $"{totalBytes.Value / (1024 * 1024.0):F0} MB"
            : "unknown size";

        Console.WriteLine($"[Download] File size: {totalStr}");

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        int lastPercent = -1;

        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            totalRead += bytesRead;

            if (totalBytes.HasValue)
            {
                int percent = (int)(totalRead * 100 / totalBytes.Value);
                if (percent != lastPercent && percent % 5 == 0)
                {
                    lastPercent = percent;
                    Console.Write($"\r[Download] Progress: {percent}% ({totalRead / (1024 * 1024.0):F1} / {totalStr})    ");
                }
            }
        }

        Console.WriteLine($"\r[Download] Downloaded: {totalRead / (1024 * 1024.0):F1} MB                              ");
    }

    // ── Archive Extraction ───────────────────────────────────────

    private static void ExtractArchive(string archivePath, string destDir)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        int fileCount = 0;

        foreach (var entry in archive.Entries)
        {
            if (!entry.IsDirectory)
            {
                entry.WriteToDirectory(destDir, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });
                fileCount++;

                if (fileCount % 10 == 0)
                    Console.Write($"\r[Download] Extracted {fileCount} files...    ");
            }
        }

        Console.WriteLine($"\r[Download] Extracted {fileCount} files total.                    ");
    }
}
