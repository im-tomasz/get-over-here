using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GetOverHere;

/// <summary>
/// Low-level mouse hook (WH_MOUSE_LL). The callback runs on the UI thread that installed it:
/// it must stay very short, otherwise the whole Windows mouse becomes sluggish.
/// </summary>
internal sealed unsafe class MouseHook : IDisposable
{
    /// <summary>Returns true to block the event.</summary>
    public delegate bool Handler(int message, in Native.MSLLHOOKSTRUCT data);

    private readonly Native.LowLevelMouseProc _proc; // kept in a field: otherwise the GC collects it and the hook crashes
    private readonly Handler _handler;
    private IntPtr _hook;

    public MouseHook(Handler handler)
    {
        _handler = handler;
        _proc = Callback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Installs the hook; calling it again while installed does nothing.</summary>
    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible d'installer le hook souris");
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>Windows silently removes a hook that was too slow (sleep, long freeze): install it again.</summary>
    public void Reinstall()
    {
        Uninstall();
        Install();
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                if (_handler((int)wParam, in *(Native.MSLLHOOKSTRUCT*)lParam))
                    return 1;
            }
            catch (Exception ex)
            {
                // Never let an exception escape into Windows: the event simply goes through.
                DiagnosticLog.Write($"ERREUR hook : {ex}");
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
