// ═══════════════════════════════════════════════════════════════════
//  ModelDownloader.cs — Downloads models described by ModelRegistry
//  Supports tar.bz2 archives (sherpa-onnx releases) and per-file
//  downloads (Hugging Face), plus the Silero VAD model.
// ═══════════════════════════════════════════════════════════════════

using SharpCompress.Common;
using SharpCompress.Readers;

namespace AsrService;

/// <summary>
/// Downloads and extracts ASR models and the Silero VAD model.
/// Progress is reported via Console (redirected into the GUI log).
/// </summary>
public static class ModelDownloader
{
    /// <summary>
    /// Returns the default model directory path (default model) —
    /// kept for backward compatibility with CLI commands.
    /// </summary>
    public static string GetDefaultModelDir() =>
        ModelRegistry.GetModelDir(ModelRegistry.GetById(ModelRegistry.DefaultModelId));

    /// <summary>Returns true if the model files already exist at the given path.</summary>
    public static bool IsModelPresent(string modelDir) =>
        File.Exists(Path.Combine(modelDir, "encoder.int8.onnx"));

    /// <summary>Download the default model (CLI --download-model).</summary>
    public static Task DownloadModelAsync() =>
        DownloadModelAsync(ModelRegistry.GetById(ModelRegistry.DefaultModelId));

    /// <summary>
    /// Downloads the given model if not already present.
    /// </summary>
    public static async Task DownloadModelAsync(ModelInfo model, CancellationToken token = default)
    {
        if (model.Loader == ModelLoader.Unsupported)
            throw new NotSupportedException(model.Notes);

        string modelDir = ModelRegistry.GetModelDir(model);

        if (ModelRegistry.IsModelPresent(model))
        {
            Console.WriteLine($"[Download] Model already exists at: {modelDir}");
            return;
        }

        Directory.CreateDirectory(ModelRegistry.ModelsRoot);

        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine($"  Downloading: {model.DisplayName}");
        Console.WriteLine("═══════════════════════════════════════════════════════");

        if (model.ArchiveUrl != null)
        {
            await DownloadArchiveModelAsync(model, token);
        }
        else if (model.Files != null)
        {
            Directory.CreateDirectory(modelDir);
            foreach (var (url, fileName) in model.Files)
            {
                token.ThrowIfCancellationRequested();
                string dest = Path.Combine(modelDir, fileName);
                if (File.Exists(dest))
                {
                    Console.WriteLine($"[Download] Exists, skipping: {fileName}");
                    continue;
                }
                Console.WriteLine($"[Download] Fetching {fileName}...");
                // Download to a temp name first so partial files don't look complete
                string tmp = dest + ".part";
                await DownloadFileAsync(url, tmp, token);
                File.Move(tmp, dest, overwrite: true);
            }
        }

        if (ModelRegistry.IsModelPresent(model))
            Console.WriteLine($"[Download] ✓ Model ready at: {modelDir}");
        else
            throw new FileNotFoundException(
                $"Download finished but check file missing: {Path.Combine(modelDir, model.CheckFile)}");
    }

    /// <summary>Downloads the Silero VAD model (~2 MB) if not present.</summary>
    public static async Task DownloadSileroVadAsync(CancellationToken token = default)
    {
        if (ModelRegistry.IsSileroVadPresent()) return;

        Directory.CreateDirectory(ModelRegistry.ModelsRoot);
        Console.WriteLine("[Download] Fetching Silero VAD model (~2 MB)...");
        string tmp = ModelRegistry.SileroVadPath + ".part";
        await DownloadFileAsync(ModelRegistry.SileroVadUrl, tmp, token);
        File.Move(tmp, ModelRegistry.SileroVadPath, overwrite: true);
        Console.WriteLine("[Download] ✓ Silero VAD ready.");
    }

    /// <summary>Downloads the TEN VAD model (~1 MB) if not present.</summary>
    public static async Task DownloadTenVadAsync(CancellationToken token = default)
    {
        if (ModelRegistry.IsTenVadPresent()) return;

        Directory.CreateDirectory(ModelRegistry.ModelsRoot);
        Console.WriteLine("[Download] Fetching TEN VAD model (~1 MB)...");
        string tmp = ModelRegistry.TenVadPath + ".part";
        await DownloadFileAsync(ModelRegistry.TenVadUrl, tmp, token);
        File.Move(tmp, ModelRegistry.TenVadPath, overwrite: true);
        Console.WriteLine("[Download] ✓ TEN VAD ready.");
    }

    // ── Archive-based models (tar.bz2) ───────────────────────────

    private static async Task DownloadArchiveModelAsync(ModelInfo model, CancellationToken token)
    {
        string archivePath = Path.Combine(ModelRegistry.ModelsRoot, model.DirName + ".tar.bz2");

        // Reuse a previously downloaded archive (e.g. after a failed extraction)
        if (!File.Exists(archivePath))
            await DownloadFileAsync(model.ArchiveUrl!, archivePath, token);
        else
            Console.WriteLine("[Download] Archive already present, skipping download.");

        Console.WriteLine("[Download] Extracting archive...");
        ExtractArchive(archivePath, ModelRegistry.ModelsRoot);

        // Delete only after successful extraction
        try { File.Delete(archivePath); }
        catch { /* Ignore cleanup errors */ }
    }

    // ── Download with Progress ───────────────────────────────────

    private static async Task DownloadFileAsync(string url, string destPath, CancellationToken token = default)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(60); // Large files

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        string totalStr = totalBytes.HasValue
            ? $"{totalBytes.Value / (1024 * 1024.0):F0} MB"
            : "unknown size";

        Console.WriteLine($"[Download] File size: {totalStr}");

        await using var contentStream = await response.Content.ReadAsStreamAsync(token);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        int lastPercent = -1;

        while ((bytesRead = await contentStream.ReadAsync(buffer, token)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
            totalRead += bytesRead;

            if (totalBytes.HasValue)
            {
                int percent = (int)(totalRead * 100 / totalBytes.Value);
                if (percent != lastPercent && percent % 5 == 0)
                {
                    lastPercent = percent;
                    Console.WriteLine($"[Download] Progress: {percent}% ({totalRead / (1024 * 1024.0):F1} / {totalStr})");
                }
            }
        }

        Console.WriteLine($"[Download] Downloaded: {totalRead / (1024 * 1024.0):F1} MB");
    }

    // ── Archive Extraction ───────────────────────────────────────

    private static void ExtractArchive(string archivePath, string destDir)
    {
        // SharpCompress cannot extract .tar.bz2 from a non-seekable stream,
        // so decompress bzip2 → temp .tar on disk first (seekable), then
        // extract the tar. Temp cost: ~1 GB, deleted immediately after.
        string tarPath = archivePath;
        bool tempTar = false;

        if (archivePath.EndsWith(".bz2", StringComparison.OrdinalIgnoreCase))
        {
            tarPath = archivePath[..^4]; // strip ".bz2"
            tempTar = true;
            Console.WriteLine("[Download] Decompressing bzip2...");
            using Stream fileStream = File.OpenRead(archivePath);
            using Stream bz2 = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
                fileStream, SharpCompress.Compressors.CompressionMode.Decompress, false);
            using var tarOut = File.Create(tarPath);
            bz2.CopyTo(tarOut, 1 << 20);
        }

        try
        {
            using var reader = SharpCompress.Readers.Tar.TarReader.OpenReader(tarPath, null);
            int fileCount = 0;

            while (reader.MoveToNextEntry())
            {
                if (!reader.Entry.IsDirectory)
                {
                    reader.WriteEntryToDirectory(destDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                    fileCount++;
                }
            }

            Console.WriteLine($"[Download] Extracted {fileCount} files total.");
        }
        finally
        {
            if (tempTar)
            {
                try { File.Delete(tarPath); }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }
}
