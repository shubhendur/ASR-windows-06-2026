# ===================================================================
#  Build-Package.ps1 - Build the distributable installer package
#
#  Run this on the DEVELOPER machine to create a ZIP that can be
#  copied to any Windows machine for installation.
#
#  Usage:
#    powershell -ExecutionPolicy Bypass -File installer\Build-Package.ps1
#
#  Output:
#    installer\AsrService-Setup.zip - ready to distribute
# ===================================================================

$ErrorActionPreference = "Stop"

# Resolve project root: this script lives in <ProjectRoot>/installer/
$ProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $ProjectRoot "AsrService.csproj"))) {
    # Fallback: maybe running from the project root itself
    if (Test-Path (Join-Path $PSScriptRoot "AsrService.csproj")) {
        $ProjectRoot = $PSScriptRoot
    } else {
        Write-Error "Cannot find AsrService.csproj. Run this script from the project root or installer directory."
        exit 1
    }
}

# Resolve paths
$CsprojPath   = Join-Path $ProjectRoot "AsrService.csproj"
$InstallerDir = Join-Path $ProjectRoot "installer"
$StagingDir   = Join-Path $InstallerDir "AsrService-Setup"
$OutputZip    = Join-Path $InstallerDir "AsrService-Setup.zip"

if (-not (Test-Path $CsprojPath)) {
    Write-Error "Cannot find AsrService.csproj. Run this script from the project root or installer directory."
    exit 1
}

Write-Host ""
Write-Host "  ====================================================" -ForegroundColor Cyan
Write-Host "    Building ASR Service Package                       " -ForegroundColor Cyan
Write-Host "  ====================================================" -ForegroundColor Cyan
Write-Host ""

# -- Step 1: Clean previous build ---------------------------------
Write-Host "[1/4] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
if (Test-Path $OutputZip)  { Remove-Item $OutputZip -Force }

# -- Step 2: Publish framework-dependent --------------------------
#   Framework-dependent = app runs via 'dotnet AsrService.dll'
#   This means dotnet.exe (a Microsoft-signed binary) is the host,
#   which bypasses AppLocker/enterprise EXE-blocking policies.
#   The output contains only DLLs + config files, no blocked EXEs.
Write-Host "[2/4] Publishing framework-dependent build..." -ForegroundColor Yellow

# Resolve the correct dotnet.exe (prefer 64-bit Program Files which has the SDK)
$dotnetPath = $null
foreach ($candidate in @(
    (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")
)) {
    if (Test-Path $candidate) {
        # Verify this one has the SDK
        $sdkCheck = & $candidate --list-sdks 2>&1
        if ($sdkCheck -and $sdkCheck -notmatch "^$") {
            $dotnetPath = $candidate
            break
        }
    }
}
if (-not $dotnetPath) {
    $found = Get-Command "dotnet.exe" -ErrorAction SilentlyContinue
    if ($found) { $dotnetPath = $found.Source }
}
if (-not $dotnetPath) {
    Write-Error ".NET SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

Write-Host "  Using dotnet: $dotnetPath" -ForegroundColor DarkGray

$PublishDir = Join-Path $ProjectRoot "bin\publish-package"
& $dotnetPath publish $CsprojPath `
    -c Release `
    -o $PublishDir `
    --self-contained false `
    -r win-x64 `
    -p:UseAppHost=false `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed!"
    exit 1
}

Write-Host "  Published to: $PublishDir" -ForegroundColor DarkGray

# -- Step 3: Stage files ------------------------------------------
Write-Host "[3/4] Staging installer files..." -ForegroundColor Yellow

$AppDir = Join-Path $StagingDir "app"
New-Item -ItemType Directory -Path $AppDir -Force | Out-Null

# Copy all published files
Copy-Item -Path (Join-Path $PublishDir "*") -Destination $AppDir -Recurse -Force

# Copy installer scripts (PS1 for logic + BAT for double-click)
Copy-Item -Path (Join-Path $InstallerDir "Install.ps1")   -Destination $StagingDir -Force
Copy-Item -Path (Join-Path $InstallerDir "Uninstall.ps1") -Destination $StagingDir -Force
Copy-Item -Path (Join-Path $InstallerDir "Install.bat")   -Destination $StagingDir -Force
Copy-Item -Path (Join-Path $InstallerDir "Uninstall.bat") -Destination $StagingDir -Force

# Count files
$fileCount = (Get-ChildItem $AppDir -Recurse -File).Count
Write-Host "  Staged $fileCount app files + installer scripts" -ForegroundColor DarkGray

# -- Step 4: Create ZIP -------------------------------------------
Write-Host "[4/4] Creating ZIP package..." -ForegroundColor Yellow

Compress-Archive -Path $StagingDir -DestinationPath $OutputZip -Force

$zipSize = [math]::Round((Get-Item $OutputZip).Length / 1MB, 1)
Write-Host ""
Write-Host "  [OK] Package created: $OutputZip ($zipSize MB)" -ForegroundColor Green
Write-Host ""
Write-Host "  Distribution instructions:" -ForegroundColor Cyan
Write-Host "  1. Copy AsrService-Setup.zip to the target machine" -ForegroundColor White
Write-Host "  2. Extract the ZIP" -ForegroundColor White
Write-Host "  3. Double-click Install.bat" -ForegroundColor White
Write-Host ""

# -- Cleanup publish dir ------------------------------------------
Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
