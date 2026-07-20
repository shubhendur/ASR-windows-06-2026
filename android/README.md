# ASR Voice Keyboard (Android)

On-device speech-to-text **keyboard** for Android — the mobile counterpart of the
Windows ASR Service in this repo. Dictation-first IME in the spirit of
[FUTO Keyboard / FUTO Voice Input](https://github.com/futo-org/android-keyboard):
everything runs locally, nothing leaves the phone.

Built for one specific problem: **Google keyboard voice typing performs badly in
noise.** This app fixes that at three levels:

| Layer | What it does |
|---|---|
| Audio source | `VOICE_RECOGNITION` source + hardware `NoiseSuppressor`, `AutomaticGainControl`, `AcousticEchoCanceler` effects |
| Software gain | Adaptive gain normalizes speech peaks to **~95–100 % of full scale** (Android has no "mic sensitivity %", this is the equivalent), with a soft limiter so nothing clips |
| VAD | **Silero VAD** (neural) gates the stream — background noise is never fed to the model as speech |

---

## Models & where they are stored

Selected in the setup app and downloaded **on the device** into app-private
storage (no storage permission, no root):

| What | Path on device |
|---|---|
| Models root | `/data/data/com.asr.keyboard/files/models/` |
| Parakeet TDT 0.6B v2 INT8 | `.../models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/` |
| Nemotron 3.5 Streaming INT8 | `.../models/sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-320ms-int8-2026-06-11/` |
| Silero VAD | `.../models/silero_vad.onnx` |
| Selected model preference | SharedPreferences `asr_settings` |

Inspect from a dev machine: `adb shell run-as com.asr.keyboard ls files/models`.
Uninstalling the app deletes the models.

- **NVIDIA Parakeet TDT 0.6B v2 INT8** *(recommended, English, ~640 MB)* — same model as the Windows app.
- **NVIDIA Nemotron 3.5 ASR Streaming 0.6B INT8** *(40 languages incl. Hindi, ~453 MB)* — the official
  [sherpa-onnx export](https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models) of
  `nvidia/nemotron-3.5-asr-streaming-0.6b` (320 ms chunks), run via the streaming
  `OnlineRecognizer` with automatic language detection.

---

## Target device: Snapdragon 695, 8 GB RAM

The app is built and tuned for this class of device:

- **CPU**: SD 695 = 2× Cortex-A78 @ 2.2 GHz + 6× A55. The recognizer uses
  **4 threads** (`Recognizer.kt`), which lands on the big cores plus headroom.
  Expect roughly **1–3× realtime** with Parakeet INT8 — fine for dictation,
  since VAD-segmented sentences are transcribed while you keep speaking.
- **RAM**: Parakeet INT8 needs ~1.5–2 GB resident. With 8 GB physical RAM this
  is comfortable. Note: the "+8 GB Dynamic/Virtual RAM" is flash-backed swap —
  it prevents the keyboard from being killed in the background, but it does
  **not** speed up inference; if the model gets swapped out, the first
  utterance after a long idle will be slow. Nemotron INT4 (~1.2 GB+) also fits
  but is experimental.
- **ABI**: the build ships **arm64-v8a only** (see `app/build.gradle.kts`),
  which is what the SD 695 runs; this keeps the APK small.
- Everything is CPU inference (ONNX Runtime via sherpa-onnx) — no GPU/DSP
  delegate needed, no vendor SDK.

---

## Build / generate the APK

### Prerequisites
- JDK 17
- Android SDK (API 35) — easiest via Android Studio
- The sherpa-onnx Android library (one-time):
  ```powershell
  cd android
  .\scripts\get-sherpa-aar.ps1     # → app/libs/sherpa-onnx.aar (~47 MB)
  ```

### Option A — Android Studio
Open the `android/` folder → let Gradle sync → **Run** (device/emulator) or
**Build ▸ Build App Bundle(s)/APK(s) ▸ Build APK(s)**.

### Option B — command line
```powershell
cd android
# if you don't have gradle installed, any Gradle 8.5+ works; SDK path goes in local.properties:
#   sdk.dir=C:/Users/<you>/AppData/Local/Android/Sdk
gradle assembleDebug
```
**APK output:** `app/build/outputs/apk/debug/app-debug.apk`

Install on the phone:
```powershell
adb install app\build\outputs\apk\debug\app-debug.apk
```

### Release APK (signed)
```powershell
gradle assembleRelease
```
Unsigned output lands in `app/build/outputs/apk/release/`. Sign it with your
keystore (`apksigner sign --ks my.keystore app-release-unsigned.apk`) or add a
`signingConfig` to `app/build.gradle.kts`. For sideloading on the target device,
the debug APK is enough.

---

## Use on the phone

1. Open the **ASR Keyboard** app: grant mic permission → download a model
   (Wi-Fi recommended, ~640 MB) → enable the keyboard in system settings.
2. In any text field, tap the keyboard-switcher (⌨ icon in the nav bar / bottom
   corner) and pick **ASR Voice Keyboard**.
3. Tap **🎤 Dictate** — continuous dictation with automatic sentence
   segmentation. **■ Stop** ends it; **🌐** switches back to your regular
   keyboard; **⌫ / ␣ / ⏎** for quick edits.

---

## Project layout

```
android/
  scripts/get-sherpa-aar.ps1        fetch sherpa-onnx AAR into app/libs/
  app/src/main/java/com/asr/keyboard/
    AudioPipeline.kt    mic capture, NS/AGC/AEC, 95–100% peak gain + soft limiter
    Recognizer.kt       sherpa-onnx OfflineRecognizer + Silero VAD
    ModelManager.kt     model catalog + downloader (mirror of Windows ModelRegistry)
    VoiceIme.kt         the keyboard (InputMethodService)
    MainActivity.kt     setup: permission, model download, enable IME
```
## the app can be configured to run with zero .NET installation. Two options:

1. Self-contained publish (simplest, works today):
```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```
    1. This bundles the entire .NET 8 runtime (~150 MB extra) into the publish folder. 
    Add -p:PublishSingleFile=true to squash it into one AsrService.exe. No runtime install needed on the target machine — just copy the folder and run. The Build-Package.ps1 installer script would need updating to publish this way and launch AsrService.exe directly instead of dotnet.exe AsrService.dll.

    2. Native AOT (<PublishAot>true</PublishAot>, already stubbed in the .csproj comments): compiles to native code, fastest startup, no runtime at all. But it's riskier here — WinForms support in AOT is limited on .NET 8, and the reflection/serialization used (e.g. System.Text.Json in AppSettings) needs source-generator adjustments. I wouldn't recommend it for this app.

    Trade-offs to be aware of:
    - Package size grows from a few MB to ~150–180 MB (before the model, which is downloaded separately either way).
    - You lose the AppLocker/WDAC bypass: a self-contained AsrService.exe is an unsigned custom executable, which is exactly what strict enterprise policies block. If your users are on locked-down corporate laptops, the current dotnet.exe-hosted design is actually the safer distribution; if they're on personal/unmanaged machines, self-contained is more convenient.
      - Servicing: security patches to the runtime require you to re-ship the app rather than users just updating .NET.

      So: keep framework-dependent + Desktop Runtime for enterprise machines, or switch to self-contained single-file if "unzip and run with nothing installed" is the priority. Both are a one-line publish-flag change; only the installer scripts need matching edits. the app can be configured to run with zero .NET installation. Two options:

   This is the testing of Parakeet AI model. After the first line has been spoken, the A model does not take the text or second line automatically. line get get skipped. 


dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true






what um so in React uh tell me whatever you have worked on. The tell me on the technical detail. 

So I'm giving an example of let's say there is a an element detail of a user. A user has created a loan. So on React what we do is we use like a component fields like input fields and like select uh select uh components and uh uh for state management uh we use uh suspens library of react and uh we use uh uh react hook like user state etc also uh for UI we uh creating UI we use SAC and UI library so that UI is consistent between uh components. 

Sansian Sansian Which library you have used? First year. 


Okay. So in React, uh suppose I the uh there are many components and it's a big application, so how will you decide um whether you have to use uh some state management tool and which state management tool you have used till of now. 

So uh a view uh I have used uh uh just end a uh for state management. Which one? Just turn CD US T and D. Okay. 

So if if the local is if the state is local like uh we are uh we are having like some increment field. So what we do is we create uh a local state using uh react use state hook. But uh if the uh state is being used in multiple components uh then we use uh They should stand for it. 

Can you speak a bit louder? Because your voice is not I'm I'm not able to clearly hear what the terms you are saying, like the hook for state management, I it's not clear to me what you have used. 

Then uh I I use the React Use state to manage the state in that component. But uh if if I am uh if the state is being used in multiple components Then I use zoottend so that it is used uh it can be used in multiple components. Okay.

Uh yeah. Uh  And which version of React you have worked on?

Uh I I I d I don't know the what is the like it is the like relatent one only, but I don't know like specific version na name. 

My my front end work actually sir, my uh front end work is like uh only thirty to twenty percent. 

So what you did in that? 

Like uh for example, uh I have uh I have to add uh a field, uh I have to add a field or I have to change some validation inside the form, uh I have to add uh a component, uh thi uh this type of stuff I have done. Okay. 
---
So suppose you have to hit an API into that React and return that data into uh the web, display that data. How will you do that? 

So first uh we have to fetch it from the back end uh using fatch and uh uh and then we can convert that uh batch response uh into JSON and uh j uh using the uh use state uh we we we we use uh the uh the React GSX elements like list etc. We can we can use that data use state data and we can get data from it and use inside our GSX. 
---
So which hook you should use to hit an API? 
So hit API patch is used to get the data. And when will you use that hook? I mean to say catches not a hook. 

It's it's yeah, it's not a hook. It is like uh uh browser space it is browser uh internal uh I am asking in which in which React hook you should mention the logic okay look we should use uh uh a use use uh effect inside the use effect we should use patch get the data. 
--And what are the parameters used in that? Do you remember? 

We can use get parameter, host parameter and body. What is the body? 
---
Okay. Um what about the CSS? How comfortable are you in CSS? 

So what we do is in session is we we don't write a manual uh CSS, it is a it is backed by eleven CSS in the background. So let's say I want a select component. So we want to theme that select component. Inside session what we do is we select the uh parameter like we have we do it read etc and it generates the uh underlying code for us then we add that component over our code base. 
---
Okay. So um how will you uh okay let's talk about So in Java, um if you talk about the microservices. So in the microservices architecture okay let's talk about the Spring Boot. Um so how will you generate create a skeleton of a Spring Boot application? 

So there are two ways. If uh we have an IDE that support is like IntelliJ IDEA, it provides uh the uh it provides the way to create it uh a uh boilerplate, but if we don't want to use a specific ID like IntelliJ, we can use uh uh through the spring initializer. and Spring In Swift Initializer we can provide Java name uh we want to use lay one with Java version we want to use and add those dependency to create that uh uh through spring in initializer uh website. 
---
Okay. So suppose you created a String Boot application and that application is not performing as expected. It takes a lot of time to load that application. So where you will figure out the fixes? What can be the likely cause? 

So first we have to check the matrix and logs of that project. So to check matrix what we do is we use Prometheus and Prometheus will take that data and we can visualize that data through Grafana. And inside that Grafana we can see like what request is utilizing a CPU. etcetera or what is not being used and inside the visualizer we can go to the specific uh request uh uh ID to check the logs we use locking and to uh inside that uh logs we can check like some specific services are failing etcetera. 

---
Have you deployed any Spring Boot application into AWS Lambda? 

Not AWS A I have not deployed on AWS AWS Lumbar but I have deployed in AWS EC2 and AWS Light C. 
AWS Light C Sale a life sale. Let's say you no, no, no, sir, it is uh L I G S T uh S A I L. Okay. light sail. Yeah, yeah. 

So in this service what we do is we create if we want a containerized version of our application. So we create a container and uh we deploy that container and we can also uh provision a database there for it and provide our secrets like uh database URL password while deploying uh so it is very uh useful in that. 
---
And in a Spring Boot application um where you will store the database, username and password, and where you will mention the database. 

So while deployment these secrets like uh u database URL and password uh it when we are uh uh developing what we d do is in develop and develop environment we put these inside our uh uh app application dot properties file or application.ml file, but in production We don't put these secrets because they are very sensitive. So while deploying the while deploying in AWS, AWS provide us to provide the secrets in key value pair. So we can provide the URL of database and password of database while the deployment and the database takes care of handling those secrets. AWS had done so the grades for us. 

---
So, um in a Spring Boot application What is the life cycle? What is the starting point of a Spring Boot application? 

So when Spring Boot starts up uh it uh it checks for the uh uh components like beams etcetera to initialize. and uh it it auto autowires the dependency dependency injection. And uh those where there where those dependency are needed, it provide us for us. We don't have to create it um manually. And it automatically uh configure Some properties for us like if we provide a database URL, we don't have to provide database database database dialect or etc. It is automatically being checked by Springboard itself. So, it it auto auto configured that for us. The driver of database is also auto configured for us. We don't have to put it manually in Springboard. So these are the while the auto configuration. And after this being said, Spring Boot is started through the integrated integrated web server for Tomcat and uh Tomcat starts the server. 

---
So have you seen Tomcat? 

It it is a I I have seen if if we are deploying a uh dedicated Tomcat then I have seen Tomcat. But in Springboard uh the Tomcat is used uh a different version. It is it is embeddable Tomcat. 

---
Um you mentioned database dialect. What is a database dialect? 

So let's say I have a database Postgres, one is version version 2 and other database we are using in other application. It is also Postgres, but it is a different version. So what happens is different version of the same database can behave differently. The query can be little bit different. and it can break some things. So dialect tells the hyperdate, the persistent level of spring boot, how to handle the queries, how to perceive data in that database version of of that. 

---
And suppose you are having some issues in the in the database queries. So is it possible with the Spring Boot to log database queries? 

So database queries hypernate hypernate queries etc we can log that so we can provide a logic for while logging inside Grafana and it it can log the what are the queries are running etc. We just have to provide uh inside our configuration that we want to load these queries. 
---
Okay, I am done with the interview. Do you have any questions? 

Of my side. I actually don't have like a specific like question but No question from my side. Okay. Alright. Thank you. Thanks for your time. What? 