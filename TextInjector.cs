// ═══════════════════════════════════════════════════════════════════
//  TextInjector.cs — Simulate Keystrokes into Active Window
//  Uses SendInput with KEYEVENTF_UNICODE for reliable text injection.
// ═══════════════════════════════════════════════════════════════════

using System.Runtime.InteropServices;

namespace AsrService;

/// <summary>
/// Types text into the currently focused window by simulating
/// individual Unicode keystrokes via the Win32 SendInput API.
/// No clipboard involvement — pure keystroke simulation.
/// </summary>
public static class TextInjector
{
    // ── Win32 Constants ──────────────────────────────────────────
    private const uint INPUT_KEYBOARD      = 1;
    private const uint KEYEVENTF_UNICODE   = 0x0004;
    private const uint KEYEVENTF_KEYUP     = 0x0002;

    // ── Public API ───────────────────────────────────────────────

    /// <summary>
    /// Types the given text into the currently active/focused window.
    /// Each character is sent as a Unicode keystroke (key-down + key-up).
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="delayMs">Delay in ms between characters (default 3ms).</param>
    public static void TypeText(string text, int delayMs = 3)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (char c in text)
        {
            // For surrogate pairs (emoji, etc.), handle them as two surrogates
            SendUnicodeChar(c);

            if (delayMs > 0)
                Thread.Sleep(delayMs);
        }
    }

    // ── Internal ─────────────────────────────────────────────────

    private static void SendUnicodeChar(char c)
    {
        var inputs = new INPUT[2];

        // Key Down
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki = new KEYBDINPUT
        {
            wVk = 0,
            wScan = (ushort)c,
            dwFlags = KEYEVENTF_UNICODE,
            time = 0,
            dwExtraInfo = GetMessageExtraInfo()
        };

        // Key Up
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].ki = new KEYBDINPUT
        {
            wVk = 0,
            wScan = (ushort)c,
            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
            time = 0,
            dwExtraInfo = GetMessageExtraInfo()
        };

        uint sent = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        if (sent != 2)
        {
            int error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"[TextInjector] SendInput failed for char '{c}' (0x{(int)c:X4}), error: {error}");
        }
    }

    // ── Win32 Structures ─────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        // Padding to match the size of the union (MOUSEINPUT is larger)
        private readonly int _padding1;
        private readonly int _padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // ── P/Invoke ─────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetMessageExtraInfo();
}
