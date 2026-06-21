@echo off
:: ═══════════════════════════════════════════════════════════════════
::  Uninstall.bat — Double-click to uninstall ASR Service
::  This is a thin wrapper that calls Uninstall.ps1 via PowerShell.
::  No administrator rights required.
:: ═══════════════════════════════════════════════════════════════════

title ASR Service - Uninstaller

echo.
echo   ══════════════════════════════════════════════════
echo     ASR Service - Uninstallation
echo   ══════════════════════════════════════════════════
echo.

:: Check if Uninstall.ps1 exists next to this bat file
if not exist "%~dp0Uninstall.ps1" (
    echo   ERROR: Uninstall.ps1 not found!
    echo   Make sure Uninstall.bat and Uninstall.ps1 are in the same folder.
    echo.
    pause
    exit /b 1
)

:: Run the PowerShell uninstaller
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0Uninstall.ps1"

echo.
if %ERRORLEVEL% EQU 0 (
    echo   Uninstallation completed successfully.
) else (
    echo   Uninstallation encountered errors. See messages above.
)
echo.
pause
