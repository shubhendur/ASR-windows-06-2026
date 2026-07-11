# ===================================================================
#  Install.ps1 - ASR Service Installer (No Admin Required)
#
#  Installs to %LOCALAPPDATA%\AsrService\ (user-space, no UAC).
#  Downloads the AI model (~670 MB) during installation.
#  Sets up auto-start on Windows login and creates shortcuts.
#
#  Usage:  Double-click Install.bat  (or run this script directly)
# ===================================================================

$ErrorActionPreference = "Stop"

# -- Configuration ------------------------------------------------
$AppName       = "AsrService"
$DisplayName   = "ASR Service - Push-to-Talk Speech-to-Text"
$InstallBase   = Join-Path $env:LOCALAPPDATA $AppName
$AppInstallDir = Join-Path $InstallBase "app"
$LauncherDir   = Join-Path $InstallBase "launcher"
$MainDll       = "AsrService.dll"
$RunKeyPath    = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"

# Where this script lives (the extracted setup folder)
$SetupDir     = $PSScriptRoot
$SourceAppDir = Join-Path $SetupDir "app"

# -- Banner -------------------------------------------------------
Write-Host ""
Write-Host "  ====================================================" -ForegroundColor Cyan
Write-Host "    ASR Service Installer                              " -ForegroundColor Cyan
Write-Host "    Push-to-Talk Speech-to-Text for Windows            " -ForegroundColor Cyan
Write-Host "    No administrator rights required                   " -ForegroundColor Cyan
Write-Host "  ====================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Install location: $InstallBase" -ForegroundColor DarkGray
Write-Host ""

# -- Preflight: Check app files exist ----------------------------
if (-not (Test-Path $SourceAppDir)) {
    Write-Host "  [ERROR] Cannot find 'app' folder next to this script." -ForegroundColor Red
    Write-Host "  Expected at: $SourceAppDir" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Make sure you extracted the full setup folder." -ForegroundColor Yellow
    exit 1
}
if (-not (Test-Path (Join-Path $SourceAppDir $MainDll))) {
    Write-Host "  [ERROR] Cannot find $MainDll inside the app folder." -ForegroundColor Red
    exit 1
}

# -- Step 1: Check .NET 8 Desktop Runtime -------------------------
Write-Host "[1/6] Checking .NET runtime..." -ForegroundColor Yellow

$dotnetPath = $null
foreach ($candidate in @(
    (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe"),
    "dotnet.exe"
)) {
    if ($candidate -eq "dotnet.exe") {
        $found = Get-Command "dotnet.exe" -ErrorAction SilentlyContinue
        if ($found) { $dotnetPath = $found.Source; break }
    } elseif (Test-Path $candidate) {
        $dotnetPath = $candidate
        break
    }
}

if (-not $dotnetPath) {
    Write-Host ""
    Write-Host "  [ERROR] .NET Runtime not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "  This app requires the .NET 8 Desktop Runtime." -ForegroundColor White
    Write-Host "  Download: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    Write-Host "  Look for: '.NET Desktop Runtime 8.x - Windows x64'" -ForegroundColor White
    Write-Host ""
    exit 1
}

$dotnetVersion = & $dotnetPath --list-runtimes 2>&1
$hasDesktopRuntime = $dotnetVersion | Where-Object { $_ -match "Microsoft\.WindowsDesktop\.App 8\." }

if (-not $hasDesktopRuntime) {
    Write-Host ""
    Write-Host "  [ERROR] .NET 8 Desktop Runtime not found!" -ForegroundColor Red
    Write-Host "  Found dotnet at: $dotnetPath" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  Please install from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    Write-Host ""
    exit 1
}

Write-Host "  [OK] .NET 8 Desktop Runtime found" -ForegroundColor Green

# -- Step 2: Stop existing instance -------------------------------
Write-Host "[2/6] Checking for running instances..." -ForegroundColor Yellow

Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine
        if ($cmdLine -and $cmdLine -like "*AsrService*") {
            Write-Host "  Stopping existing instance (PID $($_.Id))..." -ForegroundColor DarkGray
            $_ | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    } catch { }
}
Start-Sleep -Milliseconds 500

# -- Step 3: Copy application files -------------------------------
Write-Host "[3/6] Installing application files..." -ForegroundColor Yellow

New-Item -ItemType Directory -Path $AppInstallDir -Force | Out-Null
New-Item -ItemType Directory -Path $LauncherDir   -Force | Out-Null

Copy-Item -Path (Join-Path $SourceAppDir "*") -Destination $AppInstallDir -Recurse -Force

$fileCount = (Get-ChildItem $AppInstallDir -Recurse -File).Count
Write-Host "  [OK] Copied $fileCount files to $AppInstallDir" -ForegroundColor Green

# -- Step 4: Create launcher scripts -----------------------------
Write-Host "[4/6] Creating launcher scripts..." -ForegroundColor Yellow

$dllPath = Join-Path $AppInstallDir $MainDll

# Visible launcher (for shortcut - shows console with status)
$launcherCmd = Join-Path $LauncherDir "AsrService.cmd"
$cmdContent = "@echo off`r`ntitle ASR Service - Speech-to-Text`r`ncd /d `"$AppInstallDir`"`r`n`"$dotnetPath`" `"$dllPath`"`r`npause"
[System.IO.File]::WriteAllText($launcherCmd, $cmdContent, [System.Text.Encoding]::ASCII)

# Silent launcher (for auto-start - no visible window)
$launcherVbs = Join-Path $LauncherDir "AsrService-Silent.vbs"
$vbsContent = "Set WshShell = CreateObject(`"WScript.Shell`")`r`nWshShell.CurrentDirectory = `"$AppInstallDir`"`r`nWshShell.Run `"`"`"$dotnetPath`"`" `"`"$dllPath`"`"`", 0, False"
[System.IO.File]::WriteAllText($launcherVbs, $vbsContent, [System.Text.Encoding]::ASCII)

Write-Host "  [OK] Created launcher scripts" -ForegroundColor Green

# -- Step 5: Download AI Model -----------------------------------
Write-Host "[5/6] Downloading AI model (~670 MB)..." -ForegroundColor Yellow
Write-Host "  This may take several minutes on first install." -ForegroundColor DarkGray
Write-Host ""

$modelProcess = Start-Process -FilePath $dotnetPath `
    -ArgumentList "`"$dllPath`" --download-model" `
    -WorkingDirectory $AppInstallDir `
    -NoNewWindow -Wait -PassThru

if ($modelProcess.ExitCode -ne 0) {
    Write-Host ""
    Write-Host "  [WARNING] Model download may have failed." -ForegroundColor Yellow
    Write-Host "  You can retry later by running the Desktop shortcut" -ForegroundColor White
    Write-Host "  and then closing it, or by running:" -ForegroundColor White
    Write-Host "    `"$dotnetPath`" `"$dllPath`" --download-model" -ForegroundColor Cyan
    Write-Host ""
} else {
    Write-Host "  [OK] AI model downloaded successfully" -ForegroundColor Green
}

# -- Step 6: Auto-start + Shortcuts ------------------------------
Write-Host "[6/6] Setting up auto-start and shortcuts..." -ForegroundColor Yellow

# 6a: Register auto-start (silent - no window on login)
$autoStartValue = "wscript.exe `"$launcherVbs`""
Set-ItemProperty -Path $RunKeyPath -Name $AppName -Value $autoStartValue -Type String
Write-Host "  [OK] Will auto-start on every Windows login" -ForegroundColor Green

# 6b: Start Menu shortcut
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startMenuLnk = Join-Path $startMenuDir "ASR Service.lnk"
$WshShell = New-Object -ComObject WScript.Shell
$sc1 = $WshShell.CreateShortcut($startMenuLnk)
$sc1.TargetPath       = $launcherCmd
$sc1.WorkingDirectory = $AppInstallDir
$sc1.Description      = $DisplayName
$sc1.Save()
Write-Host "  [OK] Created Start Menu shortcut" -ForegroundColor Green

# 6c: Desktop shortcut
$desktopLnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "ASR Service.lnk"
$sc2 = $WshShell.CreateShortcut($desktopLnk)
$sc2.TargetPath       = $launcherCmd
$sc2.WorkingDirectory = $AppInstallDir
$sc2.Description      = $DisplayName
$sc2.Save()
Write-Host "  [OK] Created Desktop shortcut" -ForegroundColor Green

[System.Runtime.InteropServices.Marshal]::ReleaseComObject($WshShell) | Out-Null

# -- Done ---------------------------------------------------------
Write-Host ""
Write-Host "  ====================================================" -ForegroundColor Green
Write-Host "    Installation Complete!                             " -ForegroundColor Green
Write-Host "  ====================================================" -ForegroundColor Green
Write-Host ""
Write-Host "    HOW TO USE:" -ForegroundColor White
Write-Host "    * Hold RIGHT ALT      - dictate text" -ForegroundColor White
Write-Host "    * Double-tap RIGHT ALT - continuous dictation mode" -ForegroundColor White
Write-Host ""
Write-Host "    WHAT WAS SET UP:" -ForegroundColor White
Write-Host "    * Auto-starts on every Windows login (runs silently)" -ForegroundColor White
Write-Host "    * Desktop shortcut to restart if killed" -ForegroundColor White
Write-Host "    * Start Menu shortcut available" -ForegroundColor White
Write-Host ""
Write-Host "  Installed to: $InstallBase" -ForegroundColor DarkGray
Write-Host ""

$startNow = Read-Host "  Start ASR Service now? (Y/n)"
if ($startNow -ne "n" -and $startNow -ne "N") {
    Write-Host "  Starting ASR Service..." -ForegroundColor Cyan
    Start-Process -FilePath $launcherCmd -WorkingDirectory $AppInstallDir
    Write-Host "  [OK] ASR Service is running!" -ForegroundColor Green
}

Write-Host ""
