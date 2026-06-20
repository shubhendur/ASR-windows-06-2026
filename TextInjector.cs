// ═══════════════════════════════════════════════════════════════════
//  TextInjector.cs — Fast Text Injection into Active Window
//  Primary: Clipboard paste (Ctrl+V) — instant for any text length.
//  Fallback: SendInput per-character — for apps that block paste.
//
//  The clipboard approach:
//    1. Save the current clipboard content
//    2. Put our transcribed text on the clipboard
//    3. Simulate Ctrl+V to paste
//    4. Restore the original clipboard content
// ═══════════════════════════════════════════════════════════════════

using System.Runtime.InteropServices;

namespace AsrService;

/// <summary>
/// Injects text into the currently focused window.
/// Uses clipboard paste (Ctrl+V) for instant injection,
/// with automatic save/restore of clipboard content.
/// </summary>
public static class TextInjector
{
    // ── Win32 Constants ──────────────────────────────────────────
    private const uint INPUT_KEYBOARD      = 1;
    private const uint KEYEVENTF_KEYUP     = 0x0002;
    private const uint KEYEVENTF_UNICODE   = 0x0004;
    private const ushort VK_CONTROL        = 0x11;
    private const ushort VK_V              = 0x56;
    private const uint CF_UNICODETEXT      = 13;
    private const uint GMEM_MOVEABLE       = 0x0002;

    // Lock to prevent concurrent clipboard access
    private static readonly object _clipboardLock = new();

    // ── Public API ───────────────────────────────────────────────

    /// <summary>
    /// Injects text into the currently focused window via clipboard paste.
    /// Saves and restores the original clipboard content automatically.
    /// This is near-instantaneous regardless of text length.
    /// </summary>
    /// <param name="text">The text to inject.</param>
    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        lock (_clipboardLock)
        {
            InjectViaClipboard(text);
        }
    }

    // ── Clipboard Paste Implementation ───────────────────────────

    private static void InjectViaClipboard(string text)
    {
        // ── Step 1: Save current clipboard content ───────────
        string? previousText = null;
        bool hadPreviousText = false;

        if (OpenClipboard(IntPtr.Zero))
        {
            try
            {
                IntPtr hData = GetClipboardData(CF_UNICODETEXT);
                if (hData != IntPtr.Zero)
                {
                    IntPtr pData = GlobalLock(hData);
                    if (pData != IntPtr.Zero)
                    {
                        try
                        {
                            previousText = Marshal.PtrToStringUni(pData);
                            hadPreviousText = true;
                        }
                        finally
                        {
                            GlobalUnlock(hData);
                        }
                    }
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        // ── Step 2: Set our text on the clipboard ────────────
        if (!SetClipboardText(text))
        {
            Console.Error.WriteLine("[TextInjector] Failed to set clipboard. Falling back to per-char input.");
            TypeTextFallback(text);
            return;
        }

        // ── Step 3: Simulate Ctrl+V ──────────────────────────
        SendCtrlV();

        // Brief delay for the target app to process the paste
        Thread.Sleep(50);

        // ── Step 4: Restore original clipboard content ───────
        // Small extra delay to ensure paste is fully consumed
        Thread.Sleep(30);

        if (hadPreviousText && previousText != null)
        {
            SetClipboardText(previousText);
        }
        else
        {
            // Clear clipboard if it was empty before
            ClearClipboard();
        }
    }

    /// <summary>
    /// Put text on the clipboard using Win32 APIs.
    /// Returns true on success.
    /// </summary>
    private static bool SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            EmptyClipboard();

            // Allocate global memory for the string (null-terminated UTF-16)
            int byteCount = (text.Length + 1) * 2; // UTF-16: 2 bytes per char + null terminator
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero)
                return false;

            IntPtr pGlobal = GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pGlobal, text.Length);
                // Write null terminator
                Marshal.WriteInt16(pGlobal, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            // SetClipboardData takes ownership of hGlobal — do NOT free it
            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Clear the clipboard.</summary>
    private static void ClearClipboard()
    {
        if (OpenClipboard(IntPtr.Zero))
        {
            try { EmptyClipboard(); }
            finally { CloseClipboard(); }
        }
    }

    /// <summary>
    /// Simulate Ctrl+V keystroke via SendInput.
    /// Sends: Ctrl down → V down → V up → Ctrl up.
    /// </summary>
    private static void SendCtrlV()
    {
        var inputs = new INPUT[4];

        // Ctrl down
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki = new KEYBDINPUT
        {
            wVk = VK_CONTROL,
            wScan = 0,
            dwFlags = 0,
            time = 0,
            dwExtraInfo = GetMessageExtraInfo()
        };

        // V down
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].ki = new KEYBDINPUT
        {
            wVk = VK_V,
            wScan = 0,
            dwFlags = 0,
            time = 0,
            dwExtraInfo = GetMessageExtraInfo()
        };

        // V up
        inputs[2].type = INPUT_KEYBOARD;
        inputs[2].ki = new KEYBDINPUT
        {
            wVk = VK_V,
            wScan = 0,
            dwFlags = KEYEVENTF_KEYUP,
            time = 0,
            dwExtraInfo = GetMessageExtraInfo()
        };

        // Ctrl up
        inputs[3].type = INPUT_KEYBOARD;
        inputs[3].ki = new KEYBDINPUT
        {
            wVk = VK_CONTROL,
            wScan = 0,
            dwFlags = KEYEVENTF_KEYUP,
            time = 0,
            dwExtraInfo = GetMessageExtraInfo()
        };

        uint sent = SendInput(4, inputs, Marshal.SizeOf<INPUT>());
        if (sent != 4)
        {
            int error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"[TextInjector] SendInput Ctrl+V failed, error: {error}");
        }
    }

    // ── Fallback: Per-Character SendInput ────────────────────────
    // Used when clipboard injection fails (e.g., clipboard locked
    // by another app, or running in a restricted environment).

    private static void TypeTextFallback(string text, int delayMs = 2)
    {
        foreach (char c in text)
        {
            SendUnicodeChar(c);
            if (delayMs > 0)
                Thread.Sleep(delayMs);
        }
    }

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

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
