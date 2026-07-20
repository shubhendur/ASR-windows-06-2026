# ===================================================================
#  get-sherpa-aar.ps1 - Fetch the sherpa-onnx Android AAR
#  Downloads the prebuilt AAR from sherpa-onnx GitHub releases into
#  app/libs/sherpa-onnx.aar. Run once before the first Gradle build.
# ===================================================================

$ErrorActionPreference = "Stop"

$Version = "1.13.4"   # bump together with the Windows nuget if desired
$Url = "https://github.com/k2-fsa/sherpa-onnx/releases/download/v$Version/sherpa-onnx-$Version.aar"
$LibsDir = Join-Path $PSScriptRoot "..\app\libs"
$Dest = Join-Path $LibsDir "sherpa-onnx.aar"

New-Item -ItemType Directory -Force $LibsDir | Out-Null

Write-Host "Downloading sherpa-onnx AAR v$Version..."
try {
    Invoke-WebRequest -Uri $Url -OutFile $Dest
} catch {
    Write-Host ""
    Write-Host "Download failed. Check the release assets for the exact AAR name:" -ForegroundColor Yellow
    Write-Host "  https://github.com/k2-fsa/sherpa-onnx/releases/tag/v$Version" -ForegroundColor Cyan
    Write-Host "and save it as: $Dest" -ForegroundColor Cyan
    exit 1
}

Write-Host "OK: $Dest ($([math]::Round((Get-Item $Dest).Length / 1MB, 1)) MB)"
