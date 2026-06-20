# ═══════════════════════════════════════════════════════════════════
#  Install.ps1 — ASR Service Installer (No Admin Required)
#
#  Installs to %LOCALAPPDATA%\AsrService\ (user-space, no UAC).
#  Downloads the AI model (~670 MB) during installation.
#  Sets up auto-start on Windows login and creates shortcuts.
#
#  Usage:
#    powershell -ExecutionPolicy Bypass -File Install.ps1
#
#  Enterprise-safe:
#    - No EXE or MSI — this is a PowerShell text script
#    - Installs to user AppData, not Program Files
#    - App runs via dotnet.exe (Microsoft-signed), not a custom EXE
#    - Auto-start uses HKCU registry, not HKLM (no admin)
# ═══════════════════════════════════════════════════════════════════

$ErrorActionPreference = "Stop"

# ── Configuration ────────────────────────────────────────────────
$AppName        = "AsrService"
$DisplayName    = "ASR Service — Push-to-Talk Speech-to-Text"
$InstallBase    = Join-Path $env:LOCALAPPDATA $AppName
$AppInstallDir  = Join-Path $InstallBase "app"
$LauncherDir    = Join-Path $InstallBase "launcher"
$MainDll        = "AsrService.dll"
$RunKeyPath     = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"

# Where this script lives (should be the extracted setup folder)
$SetupDir       = $PSScriptRoot
$SourceAppDir   = Join-Path $SetupDir "app"

# ── Banner ───────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ╔═══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "  ║   🎙️  ASR Service Installer                  ║" -ForegroundColor Cyan
Write-Host "  ║   Push-to-Talk Speech-to-Text for Windows     ║" -ForegroundColor Cyan
Write-Host "  ║   No administrator rights required            ║" -ForegroundColor Cyan
Write-Host "  ╚═══════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Install location: $InstallBase" -ForegroundColor DarkGray
Write-Host ""

# ── Preflight Checks ────────────────────────────────────────────

# Check source files exist
if (-not (Test-Path $SourceAppDir)) {
    Write-Error "Cannot find app files at: $SourceAppDir"
    Write-Error "Make sure Install.ps1 is in the same folder as the 'app' directory."
    exit 1
}

if (-not (Test-Path (Join-Path $SourceAppDir $MainDll))) {
    Write-Error "Cannot find $MainDll in $SourceAppDir"
    exit 1
}

# Check .NET 8 Desktop Runtime is available
Write-Host "[1/6] Checking .NET runtime..." -ForegroundColor Yellow

$dotnetPath = $null
# Try the standard locations
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
    Write-Host "  ✗ .NET Runtime not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "  This application requires the .NET 8 Desktop Runtime." -ForegroundColor White
    Write-Host "  Download it from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    Write-Host "  Look for: '.NET Desktop Runtime 8.x — Windows x64 Installer'" -ForegroundColor White
    Write-Host ""
    exit 1
}

# Verify .NET 8+ is available
$dotnetVersion = & $dotnetPath --list-runtimes 2>&1
$hasDesktopRuntime = $dotnetVersion | Where-Object { $_ -match "Microsoft\.WindowsDesktop\.App 8\." }

if (-not $hasDesktopRuntime) {
    Write-Host ""
    Write-Host "  ✗ .NET 8 Desktop Runtime not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Found dotnet at: $dotnetPath" -ForegroundColor DarkGray
    Write-Host "  Installed runtimes:" -ForegroundColor DarkGray
    $dotnetVersion | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    Write-Host ""
    Write-Host "  Please install the .NET 8 Desktop Runtime from:" -ForegroundColor White
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    Write-Host ""
    exit 1
}

Write-Host "  ✓ .NET 8 Desktop Runtime found at: $dotnetPath" -ForegroundColor Green

# ── Step 2: Stop existing instance ───────────────────────────────
Write-Host "[2/6] Checking for running instances..." -ForegroundColor Yellow

$running = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            $_.CommandLine -like "*AsrService*" -or
            $_.MainModule.FileName -like "*AsrService*"
        } catch { $false }
    }

if ($running) {
    Write-Host "  Stopping existing ASR Service instance..." -ForegroundColor DarkGray
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# ── Step 3: Copy application files ──────────────────────────────
Write-Host "[3/6] Installing application files..." -ForegroundColor Yellow

# Create install directories
New-Item -ItemType Directory -Path $AppInstallDir -Force | Out-Null
New-Item -ItemType Directory -Path $LauncherDir   -Force | Out-Null

# Copy all app files
Copy-Item -Path (Join-Path $SourceAppDir "*") -Destination $AppInstallDir -Recurse -Force

$fileCount = (Get-ChildItem $AppInstallDir -Recurse -File).Count
Write-Host "  ✓ Copied $fileCount files to: $AppInstallDir" -ForegroundColor Green

# ── Step 4: Create launcher scripts ─────────────────────────────
Write-Host "[4/6] Creating launcher scripts..." -ForegroundColor Yellow

$dllPath = Join-Path $AppInstallDir $MainDll

# Launcher CMD (for desktop shortcut — shows console window for status)
$launcherCmd = Join-Path $LauncherDir "AsrService.cmd"
@"
@echo off
title ASR Service - Push-to-Talk Speech-to-Text
cd /d "$AppInstallDir"
"$dotnetPath" "$dllPath"
pause
"@ | Set-Content -Path $launcherCmd -Encoding ASCII

# Silent launcher VBS (for auto-start — no console window)
$launcherVbs = Join-Path $LauncherDir "AsrService-Silent.vbs"
@"
' ASR Service — Silent Background Launcher
' Runs the service without a visible console window.
Set WshShell = CreateObject("WScript.Shell")
WshShell.CurrentDirectory = "$AppInstallDir"
WshShell.Run """$dotnetPath"" ""$dllPath""", 0, False
"@ | Set-Content -Path $launcherVbs -Encoding ASCII

Write-Host "  ✓ Created launcher scripts" -ForegroundColor Green

# ── Step 5: Download AI Model ────────────────────────────────────
Write-Host "[5/6] Downloading AI model (~670 MB)..." -ForegroundColor Yellow
Write-Host "  This may take several minutes depending on your connection." -ForegroundColor DarkGray
Write-Host ""

# Use the app's built-in model downloader
$modelProcess = Start-Process -FilePath $dotnetPath `
    -ArgumentList """$dllPath"" --download-model" `
    -WorkingDirectory $AppInstallDir `
    -NoNewWindow -Wait -PassThru

if ($modelProcess.ExitCode -ne 0) {
    Write-Host ""
    Write-Host "  ⚠ Model download may have failed (exit code: $($modelProcess.ExitCode))" -ForegroundColor Yellow
    Write-Host "  You can retry later by running:" -ForegroundColor White
    Write-Host "    dotnet ""$dllPath"" --download-model" -ForegroundColor Cyan
    Write-Host ""
}
else {
    Write-Host "  ✓ AI model downloaded successfully" -ForegroundColor Green
}

# ── Step 6: Set up auto-start + shortcuts ────────────────────────
Write-Host "[6/6] Setting up auto-start and shortcuts..." -ForegroundColor Yellow

# 6a: Register auto-start via HKCU registry (silent VBS launcher)
$autoStartCmd = "wscript.exe ""$launcherVbs"""
Set-ItemProperty -Path $RunKeyPath -Name $AppName -Value $autoStartCmd -Type String
Write-Host "  ✓ Registered auto-start on Windows login" -ForegroundColor Green

# 6b: Create Start Menu shortcut
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startMenuShortcut = Join-Path $startMenuDir "ASR Service.lnk"

$WshShell = New-Object -ComObject WScript.Shell
$shortcut = $WshShell.CreateShortcut($startMenuShortcut)
$shortcut.TargetPath       = $launcherCmd
$shortcut.WorkingDirectory = $AppInstallDir
$shortcut.Description      = $DisplayName
$shortcut.Save()
Write-Host "  ✓ Created Start Menu shortcut" -ForegroundColor Green

# 6c: Create Desktop shortcut
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "ASR Service.lnk"

$shortcut2 = $WshShell.CreateShortcut($desktopShortcut)
$shortcut2.TargetPath       = $launcherCmd
$shortcut2.WorkingDirectory = $AppInstallDir
$shortcut2.Description      = $DisplayName
$shortcut2.Save()
Write-Host "  ✓ Created Desktop shortcut" -ForegroundColor Green

# Release COM object
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($WshShell) | Out-Null

# ── Done ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ╔═══════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "  ║   ✓ Installation Complete!                    ║" -ForegroundColor Green
Write-Host "  ╠═══════════════════════════════════════════════╣" -ForegroundColor Green
Write-Host "  ║                                               ║" -ForegroundColor Green
Write-Host "  ║   • Hold RIGHT ALT to dictate text            ║" -ForegroundColor Green
Write-Host "  ║   • Double-tap RIGHT ALT for continuous mode  ║" -ForegroundColor Green
Write-Host "  ║   • App will auto-start on Windows login      ║" -ForegroundColor Green
Write-Host "  ║   • Desktop shortcut created for manual start ║" -ForegroundColor Green
Write-Host "  ║                                               ║" -ForegroundColor Green
Write-Host "  ╚═══════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "  Installed to: $InstallBase" -ForegroundColor DarkGray
Write-Host ""

# Ask if user wants to start the service now
$startNow = Read-Host "  Start ASR Service now? (Y/n)"
if ($startNow -ne "n" -and $startNow -ne "N") {
    Write-Host "  Starting ASR Service..." -ForegroundColor Cyan
    Start-Process -FilePath $launcherCmd -WorkingDirectory $AppInstallDir
    Write-Host "  ✓ ASR Service is running!" -ForegroundColor Green
}

Write-Host ""
