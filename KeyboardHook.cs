// ═══════════════════════════════════════════════════════════════════
//  KeyboardHook.cs — Global Low-Level Keyboard Hook (Win32)
//  Detects Right Alt hold/release for push-to-talk recording.
// ═══════════════════════════════════════════════════════════════════

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AsrService;

/// <summary>
/// Installs a global low-level keyboard hook to detect Right Alt
/// press (hold) and release for push-to-talk functionality.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    // ── Win32 Constants ──────────────────────────────────────────
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_KEYUP       = 0x0101;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int WM_SYSKEYUP    = 0x0105;
    private const int VK_RMENU       = 0xA5; // Right Alt

    // ── Delegate & Handle ────────────────────────────────────────
    // MUST keep a static reference to prevent GC from collecting the delegate
    // while the hook is active — this would cause an AccessViolation crash.
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    // ── State ────────────────────────────────────────────────────
    private bool _isHolding = false;
    private bool _disposed = false;

    // ── Events ───────────────────────────────────────────────────
    /// <summary>Fired when the push-to-talk key is pressed down.</summary>
    public event Action? OnRecordStart;

    /// <summary>Fired when the push-to-talk key is released.</summary>
    public event Action? OnRecordStop;

    // ── Constructor ──────────────────────────────────────────────
    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    // ── Public API ───────────────────────────────────────────────

    /// <summary>Install the global keyboard hook.</summary>
    public void Install()
    {
        if (_hookId != IntPtr.Zero) return; // Already installed

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(
            WH_KEYBOARD_LL,
            _proc,
            GetModuleHandle(curModule.ModuleName),
            0);

        if (_hookId == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Failed to install keyboard hook. Win32 error: {error}");
        }

        Console.WriteLine("[Hook] Global keyboard hook installed (Right Alt = push-to-talk)");
    }

    /// <summary>Remove the global keyboard hook.</summary>
    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            Console.WriteLine("[Hook] Keyboard hook removed");
        }
    }

    // ── Hook Callback ────────────────────────────────────────────

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);

            if (vkCode == VK_RMENU)
            {
                int msg = wParam.ToInt32();

                // Key pressed — start recording (with repeat-guard)
                if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && !_isHolding)
                {
                    _isHolding = true;
                    OnRecordStart?.Invoke();
                }
                // Key released — stop recording
                else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && _isHolding)
                {
                    _isHolding = false;
                    OnRecordStop?.Invoke();
                }
            }
        }

        // CRITICAL: Always pass the event to the next hook in the chain.
        // Failing to do this blocks ALL keyboard input system-wide.
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ── IDisposable ──────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            Uninstall();
            _disposed = true;
        }
    }

    // ── P/Invoke Declarations ────────────────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
