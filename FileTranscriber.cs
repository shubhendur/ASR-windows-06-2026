// ═══════════════════════════════════════════════════════════════════
//  FileTranscriber.cs — Transcribe any audio/video file to text
//
//  Uses FFmpeg to decode ANY media format (mp4, mkv, mp3, m4a, wav,
//  webm, mov, ...) into a raw 16kHz mono float stream piped over
//  stdout — no temp files, constant memory even for long videos.
//  The stream is segmented with Silero VAD and each speech segment
//  is transcribed, producing a timestamped transcript.
//
//  FFmpeg is resolved without administrator rights:
//    1. ffmpeg.exe already on the user's PATH, else
//    2. %LOCALAPPDATA%\AsrService\ffmpeg\... (auto-downloaded static
//       build, extracted per-user — no UAC, no system install).
// ═══════════════════════════════════════════════════════════════════

using System.Diagnostics;
using SharpCompress.Archives;
using SharpCompress.Common;
using SherpaOnnx;

namespace AsrService;

/// <summary>Transcribes media files (video or audio) to text.</summary>
public static class FileTranscriber
{
    private const int SampleRate = 16000;

    // Static single-file-friendly FFmpeg build (zip — extractable per-user)
    private const string FfmpegDownloadUrl =
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    private static string FfmpegRoot
    {
        get
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "AsrService", "ffmpeg");
        }
    }

    // ── FFmpeg resolution (no admin required) ────────────────────

    /// <summary>
    /// Returns the path to ffmpeg.exe, downloading a per-user copy
    /// into %LOCALAPPDATA% if it isn't available anywhere.
    /// </summary>
    public static async Task<string> EnsureFfmpegAsync(CancellationToken token = default)
    {
        // 1. Already on PATH?
        string? onPath = FindOnPath("ffmpeg.exe");
        if (onPath != null)
        {
            Console.WriteLine($"[FFmpeg] Using ffmpeg from PATH: {onPath}");
            return onPath;
        }

        // 2. Previously downloaded per-user copy?
        string? local = FindLocalFfmpeg();
        if (local != null) return local;

        // 3. Download a static build into user space (no UAC)
        Console.WriteLine("[FFmpeg] Not found — downloading static build (~90 MB, per-user, no admin)...");
        Directory.CreateDirectory(FfmpegRoot);
        string zipPath = Path.Combine(FfmpegRoot, "ffmpeg.zip");

        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var response = await http.GetAsync(FfmpegDownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var src = await response.Content.ReadAsStreamAsync(token);
                await using var dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
                await src.CopyToAsync(dst, token);
            }

            Console.WriteLine("[FFmpeg] Extracting...");
            using (var archive = ArchiveFactory.OpenArchive(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    // We only need the binaries, skip docs/presets
                    if (!entry.Key!.Contains("/bin/")) continue;
                    entry.WriteToDirectory(FfmpegRoot, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true,
                    });
                }
            }
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }

        local = FindLocalFfmpeg();
        if (local == null)
            throw new FileNotFoundException(
                "FFmpeg download completed but ffmpeg.exe was not found. " +
                $"You can also place ffmpeg.exe manually under: {FfmpegRoot}");

        Console.WriteLine($"[FFmpeg] ✓ Ready: {local}");
        return local;
    }

    private static string? FindLocalFfmpeg()
    {
        if (!Directory.Exists(FfmpegRoot)) return null;
        return Directory.EnumerateFiles(FfmpegRoot, "ffmpeg.exe", SearchOption.AllDirectories)
                        .FirstOrDefault();
    }

    private static string? FindOnPath(string exeName)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                string candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    // ── Transcription ────────────────────────────────────────────

    /// <summary>
    /// Transcribes the given media file. Returns the full transcript
    /// text and writes a timestamped .txt next to the input file.
    /// </summary>
    public static async Task<string> TranscribeAsync(
        string inputPath,
        AsrEngine engine,
        AppSettings settings,
        CancellationToken token = default)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"File not found: {inputPath}");
        if (!engine.IsReady)
            throw new InvalidOperationException("Load a model before transcribing a file.");

        string ffmpeg = await EnsureFfmpegAsync(token);

        Console.WriteLine($"[File] Transcribing: {Path.GetFileName(inputPath)}");
        var sw = Stopwatch.StartNew();

        // Silero VAD instance for segmentation. MaxSpeechDuration keeps
        // individual segments small enough for fast offline decoding.
        var vadConfig = new VadModelConfig();
        if (settings.VadEngine == "ten")
        {
            vadConfig.TenVad.Model = ModelRegistry.TenVadPath;
            vadConfig.TenVad.Threshold = settings.VadThreshold;
            vadConfig.TenVad.MinSilenceDuration = 1.4f; // keep whole sentences together → correct punctuation
            vadConfig.TenVad.MinSpeechDuration = 0.10f; // keep short words
            vadConfig.TenVad.MaxSpeechDuration = 30f;
            vadConfig.TenVad.WindowSize = 256;
        }
        else
        {
            vadConfig.SileroVad.Model = ModelRegistry.SileroVadPath;
            vadConfig.SileroVad.Threshold = settings.VadThreshold;
            vadConfig.SileroVad.MinSilenceDuration = 1.4f;  // keep whole sentences together → correct punctuation
            vadConfig.SileroVad.MinSpeechDuration = 0.10f;  // keep short words
            vadConfig.SileroVad.MaxSpeechDuration = 30f;
            vadConfig.SileroVad.WindowSize = 512;
        }
        vadConfig.SampleRate = SampleRate;
        vadConfig.NumThreads = 1;
        vadConfig.Provider = "cpu";

        using var vad = new VoiceActivityDetector(vadConfig, 180f);

        // FFmpeg: any container/codec → raw 32-bit float, 16kHz, mono on stdout.
        // -vn drops video; works identically for pure audio files.
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-v error -i \"{inputPath}\" -vn -ac 1 -ar {SampleRate} -f f32le pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start FFmpeg.");

        var transcript = new System.Text.StringBuilder();
        var plainText = new System.Text.StringBuilder();
        int segmentCount = 0;
        long totalSamples = 0;

        // Read stderr asynchronously so a chatty FFmpeg can't deadlock the pipe
        var stderrTask = proc.StandardError.ReadToEndAsync(token);

        // Stream stdout → VAD → engine, ~0.5s of audio per read
        byte[] byteBuffer = new byte[SampleRate * 4 / 2];
        var stdout = proc.StandardOutput.BaseStream;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            int read = await FillBufferAsync(stdout, byteBuffer, token);
            if (read == 0) break;

            int floats = read / 4;
            float[] chunk = new float[floats];
            Buffer.BlockCopy(byteBuffer, 0, chunk, 0, floats * 4);
            totalSamples += floats;

            vad.AcceptWaveform(chunk);
            segmentCount += DrainSegments(vad, engine, transcript, plainText);
        }

        // Flush trailing speech held inside the VAD
        vad.Flush();
        segmentCount += DrainSegments(vad, engine, transcript, plainText);

        await proc.WaitForExitAsync(token);
        string stderr = await stderrTask;
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"FFmpeg failed (exit {proc.ExitCode}): {stderr.Trim()}");

        sw.Stop();
        double mediaDuration = (double)totalSamples / SampleRate;
        Console.WriteLine($"[File] ✓ {segmentCount} segments, {mediaDuration / 60:F1} min of audio " +
                          $"transcribed in {sw.Elapsed.TotalSeconds:F0}s " +
                          $"({mediaDuration / Math.Max(sw.Elapsed.TotalSeconds, 0.001):F1}x realtime)");

        if (segmentCount == 0)
        {
            Console.WriteLine("[File] No speech detected in this file.");
            return string.Empty;
        }

        // Save the timestamped transcript next to the input file
        string outPath = Path.ChangeExtension(inputPath, ".transcript.txt");
        await File.WriteAllTextAsync(outPath,
            $"Transcript of: {Path.GetFileName(inputPath)}\r\n" +
            $"Generated by ASR Service ({engine.LoadedModel?.DisplayName})\r\n" +
            new string('─', 60) + "\r\n\r\n" +
            transcript.ToString(), token);
        Console.WriteLine($"[File] Transcript saved: {outPath}");

        return plainText.ToString().Trim();
    }

    /// <summary>Pop completed VAD segments, transcribe each, append to the transcript.</summary>
    private static int DrainSegments(
        VoiceActivityDetector vad,
        AsrEngine engine,
        System.Text.StringBuilder transcript,
        System.Text.StringBuilder plainText)
    {
        int count = 0;
        while (!vad.IsEmpty())
        {
            var segment = vad.Front();
            vad.Pop();

            if (segment.Samples.Length < SampleRate / 16) continue; // < ~60ms (keep short words)

            string text = engine.Transcribe(segment.Samples);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var start = TimeSpan.FromSeconds((double)segment.Start / SampleRate);
            var end = start + TimeSpan.FromSeconds((double)segment.Samples.Length / SampleRate);

            transcript.AppendLine($"[{start:hh\\:mm\\:ss} → {end:hh\\:mm\\:ss}]  {text}");
            plainText.Append(text).Append(' ');
            count++;
        }
        return count;
    }

    /// <summary>Reads until the buffer is full or the stream ends (float alignment safety).</summary>
    private static async Task<int> FillBufferAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), token);
            if (read == 0) break;
            total += read;
        }
        // Truncate to whole floats (only possible at end-of-stream)
        return total - (total % 4);
    }
}
