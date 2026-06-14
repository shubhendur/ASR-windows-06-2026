// ═══════════════════════════════════════════════════════════════════
//  StartupManager.cs — Windows Auto-Start via Registry
//  Registers/removes the app from HKCU\...\Run for login startup.
// ═══════════════════════════════════════════════════════════════════

using Microsoft.Win32;

namespace AsrService;

/// <summary>
/// Manages Windows auto-startup registration via the Registry.
/// Uses HKCU (current user) — no admin privileges required.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AsrService";

    /// <summary>Register the current exe to run on Windows login.</summary>
    public static void Install()
    {
        string exePath = GetExePath();

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null)
        {
            Console.Error.WriteLine("[Startup] Failed to open registry key.");
            return;
        }

        key.SetValue(AppName, $"\"{exePath}\"");
        Console.WriteLine($"[Startup] Registered for auto-start: {exePath}");
        Console.WriteLine("[Startup] The service will start automatically on next login.");
    }

    /// <summary>Remove the auto-start registration.</summary>
    public static void Uninstall()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null)
        {
            Console.Error.WriteLine("[Startup] Failed to open registry key.");
            return;
        }

        if (key.GetValue(AppName) != null)
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
            Console.WriteLine("[Startup] Auto-start registration removed.");
        }
        else
        {
            Console.WriteLine("[Startup] Not currently registered for auto-start.");
        }
    }

    /// <summary>Check if the app is registered for auto-start.</summary>
    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(AppName) != null;
    }

    private static string GetExePath()
    {
        // For single-file / AOT published apps, Environment.ProcessPath
        // gives the correct path to the executable.
        return Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine executable path.");
    }
}
