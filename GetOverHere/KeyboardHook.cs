using System.Runtime.InteropServices;

namespace GetOverHere;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL), only installed during the cooldown before the focus returns.
/// It never blocks a key, and the app only looks at WHEN keys are pressed, never at which ones.
/// Like the mouse hook, the callback runs on the UI thread and must stay very short.
/// </summary>
internal sealed unsafe class KeyboardHook : IDisposable
{
    public delegate void Handler(int message, in Native.KBDLLHOOKSTRUCT data);

    private readonly Native.LowLevelKeyboardProc _proc; // kept in a field: otherwise the GC collects it and the hook crashes
    private readonly Handler _handler;
    private IntPtr _hook;

    public KeyboardHook(Handler handler)
    {
        _handler = handler;
        _proc = Callback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Installs the hook; calling it again while installed does nothing. Returns false if Windows refuses.</summary>
    public bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            // Not fatal: the cooldown simply is not extended by typing.
            DiagnosticLog.Write($"ERREUR hook clavier : code {Marshal.GetLastWin32Error()}");
            return false;
        }
        return true;
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                _handler((int)wParam, in *(Native.KBDLLHOOKSTRUCT*)lParam);
            }
            catch (Exception ex)
            {
                // Never let an exception escape into Windows: the key simply goes through.
                DiagnosticLog.Write($"ERREUR hook clavier : {ex}");
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
