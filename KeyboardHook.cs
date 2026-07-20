// ═══════════════════════════════════════════════════════════════════
//  KeyboardHook.cs — Global Low-Level Keyboard Hook (Win32)
//  Detects Right Alt hold/release for push-to-talk recording,
//  and double-tap (press-release twice within 700ms) for toggling
//  continuous recording mode.
// ═══════════════════════════════════════════════════════════════════

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AsrService;

/// <summary>
/// Installs a global low-level keyboard hook to detect Right Alt
/// press (hold) and release for push-to-talk functionality, as well
/// as double-tap for continuous recording mode.
///
/// Double-tap detection:
///   A "tap" is a quick press-release (held &lt; 400ms).
///   Two taps within 700ms of each other trigger OnDoubleTap.
///   The first tap is "absorbed" — neither OnRecordStart nor
///   OnRecordStop fires for it. If no second tap arrives within
///   700ms, the first tap is treated as a normal hold-to-talk
///   press, and OnRecordStart fires retroactively.
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
    private bool _isHolding = false;      // physical key is currently held down
    private bool _disposed = false;

    // ── Double-tap detection state ───────────────────────────────
    private const int DoubleTapWindowMs = 700;   // max time between first tap-release and second tap-press
    private const int TapMaxHoldMs      = 400;   // max hold duration to count as a "tap" vs a "hold"

    private long _firstTapDownTime = 0;   // TickCount64 when first tap key-down occurred
    private long _firstTapUpTime   = 0;   // TickCount64 when first tap key-up occurred
    private bool _waitingForSecondTap = false;
    private bool _holdStartedDeferred = false;  // true if we deferred OnRecordStart for the first tap
    private System.Windows.Forms.Timer? _tapTimer; // fires if no second tap arrives in time

    // ── Events ───────────────────────────────────────────────────
    /// <summary>Fired when the push-to-talk key is pressed down (hold mode).</summary>
    public event Action? OnRecordStart;

    /// <summary>Fired when the push-to-talk key is released (hold mode).</summary>
    public event Action? OnRecordStop;

    /// <summary>Fired when the push-to-talk key is double-tapped (press-release twice quickly).</summary>
    public event Action? OnDoubleTap;

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

        Console.WriteLine("[Hook] Global keyboard hook installed (Right Alt = push-to-talk / double-tap = continuous)");
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
                bool isKeyDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                bool isKeyUp   = (msg == WM_KEYUP   || msg == WM_SYSKEYUP);

                if (isKeyDown && !_isHolding)
                {
                    _isHolding = true;
                    HandleKeyDown();
                }
                else if (isKeyUp && _isHolding)
                {
                    _isHolding = false;
                    HandleKeyUp();
                }
            }
        }

        // CRITICAL: Always pass the event to the next hook in the chain.
        // Failing to do this blocks ALL keyboard input system-wide.
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ── Double-tap State Machine ─────────────────────────────────

    private void HandleKeyDown()
    {
        long now = Environment.TickCount64;

        if (_waitingForSecondTap)
        {
            // This is the second tap key-down. Check timing.
            long sinceFirstTapUp = now - _firstTapUpTime;

            if (sinceFirstTapUp <= DoubleTapWindowMs)
            {
                // ✓ Second tap arrived in time — this is a double-tap!
                CancelTapTimer();
                _waitingForSecondTap = false;
                _holdStartedDeferred = false;
                _firstTapDownTime = 0;
                _firstTapUpTime = 0;

                OnDoubleTap?.Invoke();
                return;
            }
            else
            {
                // Too slow — the first tap already fired OnRecordStart
                // via the timer. Treat this as a new first tap.
                _waitingForSecondTap = false;
            }
        }

        // This is a fresh key-down. Record time and defer OnRecordStart
        // until we know it's not the start of a double-tap sequence.
        _firstTapDownTime = now;
        _holdStartedDeferred = true;

        // Start a timer: if the user doesn't complete a double-tap in time,
        // retroactively fire OnRecordStart (the user is just holding the key).
        StartTapTimer();
    }

    private void HandleKeyUp()
    {
        long now = Environment.TickCount64;
        long holdDuration = now - _firstTapDownTime;

        if (_holdStartedDeferred && holdDuration <= TapMaxHoldMs)
        {
            // This was a short tap (not a long hold).
            // Don't fire OnRecordStop — wait for possible second tap.
            CancelTapTimer();
            _firstTapUpTime = now;
            _waitingForSecondTap = true;
            _holdStartedDeferred = false;

            // Start timer for second-tap window
            StartTapTimer();
            return;
        }

        // Either the hold was long (OnRecordStart already fired via timer),
        // or we're in normal mode. Fire OnRecordStop.
        CancelTapTimer();
        _waitingForSecondTap = false;

        if (!_holdStartedDeferred)
        {
            // OnRecordStart was already fired — fire the matching stop.
            OnRecordStop?.Invoke();
        }
        else
        {
            // The hold was so short the timer hadn't fired yet, but
            // it exceeded TapMaxHoldMs. Fire start then stop.
            _holdStartedDeferred = false;
            OnRecordStart?.Invoke();
            OnRecordStop?.Invoke();
        }
    }

    // ── Timer Management ─────────────────────────────────────────

    private void StartTapTimer()
    {
        CancelTapTimer();
        _tapTimer = new System.Windows.Forms.Timer { Interval = DoubleTapWindowMs };
        _tapTimer.Tick += OnTapTimerExpired;
        _tapTimer.Start();
    }

    private void CancelTapTimer()
    {
        if (_tapTimer != null)
        {
            _tapTimer.Stop();
            _tapTimer.Tick -= OnTapTimerExpired;
            _tapTimer.Dispose();
            _tapTimer = null;
        }
    }

    private void OnTapTimerExpired(object? sender, EventArgs e)
    {
        CancelTapTimer();

        if (_holdStartedDeferred)
        {
            // The key is still held and no second tap came.
            // This is a normal hold — fire OnRecordStart now.
            _holdStartedDeferred = false;
            OnRecordStart?.Invoke();
        }
        else if (_waitingForSecondTap)
        {
            // The user tapped once and released, but no second tap came.
            // This was a single short tap — fire start+stop as a quick tap-to-record.
            _waitingForSecondTap = false;
            // A single short tap is too brief to be useful, just ignore it.
        }
    }

    // ── IDisposable ──────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            CancelTapTimer();
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
