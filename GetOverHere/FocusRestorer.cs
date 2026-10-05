namespace GetOverHere;

/// <summary>
/// Brings a window back to the foreground. Windows refuses SetForegroundWindow to a process that did not
/// receive the last user input, so several well-known workarounds are tried in turn.
/// </summary>
internal static class FocusRestorer
{
    public static bool Restore(IntPtr hwnd)
    {
        if (!Native.IsWindow(hwnd) || Native.IsIconic(hwnd)) return false;
        if (Native.GetForegroundWindow() == hwnd) return true;

        // 1. Direct attempt.
        if (TrySet(hwnd)) return true;

        // 2. Synthetic input: our process becomes "the last one to have received input".
        OwnInput.Nudge();
        if (TrySet(hwnd)) return true;

        // 3. Attach our input queue to the one of the current foreground window.
        uint fgThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero);
        uint me = Native.GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true);
        try
        {
            Native.BringWindowToTop(hwnd);
            return TrySet(hwnd);
        }
        finally
        {
            if (attached) Native.AttachThreadInput(me, fgThread, false);
        }
    }

    private static bool TrySet(IntPtr hwnd) =>
        Native.SetForegroundWindow(hwnd) && Native.GetForegroundWindow() == hwnd;
}
