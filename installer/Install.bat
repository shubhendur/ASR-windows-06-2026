@echo off
:: ═══════════════════════════════════════════════════════════════════
::  Install.bat — Double-click to install ASR Service
::  This is a thin wrapper that calls Install.ps1 via PowerShell.
::  No administrator rights required.
:: ═══════════════════════════════════════════════════════════════════

title ASR Service - Installer

echo.
echo   ══════════════════════════════════════════════════
echo     ASR Service - Installation
echo     No administrator rights required.
echo   ══════════════════════════════════════════════════
echo.

:: Check if Install.ps1 exists next to this bat file
if not exist "%~dp0Install.ps1" (
    echo   ERROR: Install.ps1 not found!
    echo   Make sure Install.bat and Install.ps1 are in the same folder.
    echo.
    pause
    exit /b 1
)

:: Run the PowerShell installer
:: -ExecutionPolicy Bypass: allows the script to run regardless of system policy
::                          (this is a per-session override, not a system change)
:: -NoProfile: skip loading user's PS profile for a clean environment
:: -File: run the installer script
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0Install.ps1"

echo.
if %ERRORLEVEL% EQU 0 (
    echo   Installation completed successfully.
) else (
    echo   Installation encountered errors. See messages above.
)
echo.
pause
