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
        string startCommand = GetStartCommand();

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null)
        {
            Console.Error.WriteLine("[Startup] Failed to open registry key.");
            return;
        }

        key.SetValue(AppName, startCommand);
        Console.WriteLine($"[Startup] Registered for auto-start: {startCommand}");
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

    /// <summary>
    /// Build the command string for the auto-start registry value.
    /// Handles both self-contained (EXE) and framework-dependent
    /// (dotnet.exe host) deployments.
    /// </summary>
    private static string GetStartCommand()
    {
        string processPath = Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine executable path.");

        // Check if we're running under dotnet.exe (framework-dependent deployment).
        // In that case, Environment.ProcessPath returns dotnet.exe and we need
        // to include the DLL path as an argument.
        string processName = Path.GetFileNameWithoutExtension(processPath);
        if (processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // Get the entry assembly DLL location
            string? dllPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(dllPath))
            {
                return $"\"{processPath}\" \"{dllPath}\"";
            }
        }

        // Self-contained / AOT: the process IS our app executable
        return $"\"{processPath}\"";
    }
}
