## create dotnet DLL files to share it with any user for installation. 
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
```powershell
dotnet publish -c Release -r win-x64 --self-contained true 
```
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```



# 🎙️ ASR Service — Offline Speech-to-Text for Windows (+ Android)

**Fast, fully offline speech recognition.** Uses **NVIDIA Parakeet TDT 0.6B v2**
(INT8 ONNX) via **sherpa-onnx**, with **Silero VAD** for noise-robust continuous
dictation. No internet required after setup; audio never leaves the machine.

Two apps in this repo:

| Platform | Location | What it is |
|---|---|---|
| Windows 11 | repo root | GUI + push-to-talk dictation service, file transcription (C# / .NET 8) |
| Android | [`android/`](android/README.md) | Voice keyboard (IME) — dictation that works in noise (Kotlin) |

---

## ✨ Features (Windows)

- **Push-to-talk** — hold **Right Alt**, speak, release → text is typed into the focused window
- **Continuous mode** — double-tap Right Alt (or GUI button); **Silero VAD** segments speech automatically, works in noisy rooms
- **GUI** — choose audio source (any mic, or **System Audio loopback** to record only the other person on a call), AI model, and language; runs in the tray
- **File transcription** — drop in any **video or audio file** (mp4, mkv, mp3, m4a, wav, …); FFmpeg decodes it, output is a timestamped `.transcript.txt`
- **No admin rights required** — installs and runs entirely per-user

---

## 🚀 Quick Start (Windows)

### Prerequisites
- Windows 11 x64, a microphone
- End users: [.NET 8 **Desktop Runtime**](https://dotnet.microsoft.com/download/dotnet/8.0) · Developers: .NET 8 **SDK**

### Run from source (developers)
```powershell
git clone <this repo>
cd ASR-windows-06-2026
dotnet run -c Release                 # opens the GUI; download the model from there
```
Or pre-download the default model first:
```powershell
dotnet run -c Release -- --download-model
```

### Install for an end user (no admin rights)
```powershell
.\installer\Build-Package.ps1         # on your machine → AsrService-Setup.zip
# on the target machine: extract the zip, double-click Install.bat
```
The installer copies files to `%LOCALAPPDATA%\AsrService\app\`, downloads the
model, registers auto-start in the **HKCU** registry hive and creates shortcuts —
**no UAC prompt at any point**. The app runs via `dotnet.exe AsrService.dll`
(a Microsoft-signed binary), which also keeps AppLocker/WDAC-restricted
enterprise machines happy.

---

## 📁 Where things are stored (Windows)

| What | Path |
|---|---|
| Installed app | `%LOCALAPPDATA%\AsrService\app\` |
| **ASR models** | `%LOCALAPPDATA%\AsrService\models\<model-dir>\` |
| Parakeet TDT 0.6B v2 INT8 | `%LOCALAPPDATA%\AsrService\models\sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8\` |
| Nemotron 3.5 Streaming INT8 | `%LOCALAPPDATA%\AsrService\models\sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-320ms-int8-2026-06-11\` |
| **Silero VAD model** | `%LOCALAPPDATA%\AsrService\models\silero_vad.onnx` |
| **User settings** (source/model/language) | `%LOCALAPPDATA%\AsrService\settings.json` |
| Auto-downloaded FFmpeg (if not on PATH) | `%LOCALAPPDATA%\AsrService\ffmpeg\` |
| Auto-start registration | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` → `AsrService` |
| File transcripts | next to the input file, as `<name>.transcript.txt` |

Delete a model folder to force a re-download.

---

## 🔧 CLI Commands

| Command | Description |
|---------|-------------|
| `AsrService.exe` | Run with the settings GUI (default) |
| `AsrService.exe --headless` | Run without GUI (console only) |
| `AsrService.exe --transcribe <file>` | Transcribe a video/audio file to text |
| `AsrService.exe --download-model` | Download the selected ASR model + Silero VAD |
| `AsrService.exe --install` / `--uninstall` | Register / remove Windows auto-startup |
| `AsrService.exe --status` | Show model, VAD, source and startup status |

(When running from source, use `dotnet run -c Release -- <command>`.)

---

## 🏗️ Architecture (Windows)

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point, CLI parsing, GUI/headless bootstrap |
| `MainForm.cs` | Settings GUI: source/model/language dropdowns, file transcription, tray icon |
| `AsrController.cs` | Orchestration: push-to-talk, continuous mode (Silero VAD loop) |
| `AsrEngine.cs` | sherpa-onnx OfflineRecognizer wrapper (model-aware) |
| `AudioRecorder.cs` | WASAPI capture (mic or speaker loopback), streaming FIR resampler → 16 kHz mono |
| `FileTranscriber.cs` | FFmpeg pipe → VAD segmentation → timestamped transcript |
| `ModelRegistry.cs` / `ModelDownloader.cs` | Model catalog + downloads (GitHub releases / Hugging Face) |
| `AppSettings.cs` | Persisted settings (JSON) |
| `KeyboardHook.cs` | Global Right-Alt hook (hold / double-tap) |
| `TextInjector.cs` | SendInput keystroke injection |
| `MicController.cs` | Sets mic volume to 100 % via Core Audio |
| `StartupManager.cs` | HKCU Run-key auto-start |

---

## 🧠 Models

| Model | Languages | Size | Status |
|-------|-----------|------|--------|
| **Parakeet TDT 0.6B v2 (INT8)** | English | ~670 MB | ✅ Default, batch (offline) recognizer |
| **Nemotron 3.5 ASR Streaming 0.6B (INT8)** | 40 (incl. English & Hindi) | ~453 MB | ✅ Official [sherpa-onnx export](https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models) of `nvidia/nemotron-3.5-asr-streaming-0.6b`; streaming recognizer, per-utterance language selection or auto-detect (requires sherpa-onnx ≥ 1.13.4) |

The language dropdown takes effect with Nemotron ("Auto-detect" uses the model's
built-in language ID); Parakeet is English-only and ignores it.

Performance on an i7-1355U: ~15–25× realtime, ~2 GB RAM with Parakeet loaded.

---

## 📝 License / third-party

[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) (Apache 2.0) ·
[NVIDIA Parakeet TDT](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2) (CC-BY-4.0) ·
[Silero VAD](https://github.com/snakers4/silero-vad) (MIT) ·
[NAudio](https://github.com/naudio/NAudio) (MIT) ·
FFmpeg (LGPL/GPL, downloaded separately at first use)

