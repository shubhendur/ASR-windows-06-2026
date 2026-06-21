# ===================================================================
#  Uninstall.ps1 - ASR Service Uninstaller (No Admin Required)
#
#  Removes the application, auto-start registration, and shortcuts.
#  Optionally removes the downloaded AI model data.
#
#  Usage:
#    powershell -ExecutionPolicy Bypass -File Uninstall.ps1
# ===================================================================

$ErrorActionPreference = "Stop"

# -- Configuration ------------------------------------------------
$AppName       = "AsrService"
$InstallBase   = Join-Path $env:LOCALAPPDATA $AppName
$AppInstallDir = Join-Path $InstallBase "app"
$LauncherDir   = Join-Path $InstallBase "launcher"
$ModelsDir     = Join-Path $InstallBase "models"
$RunKeyPath    = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"

# -- Banner -------------------------------------------------------
Write-Host ""
Write-Host "  ====================================================" -ForegroundColor Yellow
Write-Host "    ASR Service - Uninstaller                          " -ForegroundColor Yellow
Write-Host "  ====================================================" -ForegroundColor Yellow
Write-Host ""

if (-not (Test-Path $AppInstallDir)) {
    Write-Host "  ASR Service does not appear to be installed." -ForegroundColor DarkGray
    Write-Host "  Expected location: $InstallBase" -ForegroundColor DarkGray
    Write-Host ""
    exit 0
}

# -- Step 1: Stop running instance --------------------------------
Write-Host "[1/5] Stopping running instances..." -ForegroundColor Yellow

# Stop any dotnet process running our DLL
$dllPath = Join-Path $AppInstallDir "AsrService.dll"
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine
        if ($cmdLine -and $cmdLine -like "*AsrService*") {
            Write-Host "  Stopping process $($_.Id)..." -ForegroundColor DarkGray
            $_ | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    } catch { }
}

# Also try to stop any wscript running our VBS launcher
Get-Process -Name "wscript" -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine
        if ($cmdLine -and $cmdLine -like "*AsrService*") {
            $_ | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    } catch { }
}

Start-Sleep -Seconds 1
Write-Host "  [OK] Done" -ForegroundColor Green

# -- Step 2: Remove auto-start -----------------------------------
Write-Host "[2/5] Removing auto-start registration..." -ForegroundColor Yellow

$currentValue = Get-ItemProperty -Path $RunKeyPath -Name $AppName -ErrorAction SilentlyContinue
if ($currentValue) {
    Remove-ItemProperty -Path $RunKeyPath -Name $AppName -ErrorAction SilentlyContinue
    Write-Host "  [OK] Auto-start registration removed" -ForegroundColor Green
} else {
    Write-Host "  (not registered)" -ForegroundColor DarkGray
}

# -- Step 3: Remove shortcuts ------------------------------------
Write-Host "[3/5] Removing shortcuts..." -ForegroundColor Yellow

$startMenuShortcut = Join-Path (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs") "ASR Service.lnk"
$desktopShortcut   = Join-Path ([Environment]::GetFolderPath("Desktop")) "ASR Service.lnk"

foreach ($lnk in @($startMenuShortcut, $desktopShortcut)) {
    if (Test-Path $lnk) {
        Remove-Item $lnk -Force
        Write-Host "  [OK] Removed: $(Split-Path $lnk -Leaf)" -ForegroundColor Green
    }
}

# -- Step 4: Remove application files ----------------------------
Write-Host "[4/5] Removing application files..." -ForegroundColor Yellow

if (Test-Path $AppInstallDir) {
    Remove-Item $AppInstallDir -Recurse -Force
    Write-Host "  [OK] Removed: $AppInstallDir" -ForegroundColor Green
}

if (Test-Path $LauncherDir) {
    Remove-Item $LauncherDir -Recurse -Force
    Write-Host "  [OK] Removed: $LauncherDir" -ForegroundColor Green
}

# -- Step 5: Ask about model data --------------------------------
Write-Host "[5/5] Model data..." -ForegroundColor Yellow

if (Test-Path $ModelsDir) {
    $modelSize = [math]::Round((Get-ChildItem $ModelsDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 0)
    Write-Host ""
    Write-Host "  The AI model data (~${modelSize} MB) is stored at:" -ForegroundColor White
    Write-Host "  $ModelsDir" -ForegroundColor DarkGray
    Write-Host ""
    $removeModel = Read-Host "  Remove model data? This saves disk space but requires re-download to reinstall. (y/N)"

    if ($removeModel -eq "y" -or $removeModel -eq "Y") {
        Remove-Item $ModelsDir -Recurse -Force
        Write-Host "  [OK] Model data removed" -ForegroundColor Green

        # Remove the base directory if empty
        if ((Get-ChildItem $InstallBase -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0) {
            Remove-Item $InstallBase -Force -ErrorAction SilentlyContinue
        }
    } else {
        Write-Host "  Model data kept at: $ModelsDir" -ForegroundColor DarkGray
    }
} else {
    # Remove the base directory if empty
    if (Test-Path $InstallBase) {
        if ((Get-ChildItem $InstallBase -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0) {
            Remove-Item $InstallBase -Force -ErrorAction SilentlyContinue
        }
    }
}

# -- Done ---------------------------------------------------------
Write-Host ""
Write-Host "  ====================================================" -ForegroundColor Green
Write-Host "    ASR Service has been uninstalled                   " -ForegroundColor Green
Write-Host "  ====================================================" -ForegroundColor Green
Write-Host ""
