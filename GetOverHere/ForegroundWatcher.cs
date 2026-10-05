namespace GetOverHere;

/// <summary>
/// Raises an event on every foreground window change (SetWinEventHook, on the UI thread)
/// after recording it in <see cref="History"/>.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly Native.WinEventProc _proc; // kept in a field for the GC
    private IntPtr _hook;

    public ForegroundHistory History { get; } = new();

    public event Action<IntPtr>? Changed;

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <param name="skipOwnProcess">Ignore the app's own windows (false only for tests).</param>
    public ForegroundWatcher(bool skipOwnProcess = true)
    {
        // Window active at launch, dated one hour back to serve as a starting point.
        History.Record(unchecked(Environment.TickCount - 3_600_000), Native.GetForegroundWindow());
        _proc = Callback;
        uint flags = Native.WINEVENT_OUTOFCONTEXT | (skipOwnProcess ? Native.WINEVENT_SKIPOWNPROCESS : 0);
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc,
                                       0, 0, flags);
        if (_hook == IntPtr.Zero) DiagnosticLog.Write("ERREUR : SetWinEventHook a échoué, le focus ne sera surveillé qu'au moment du retour");
    }

    private void Callback(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            History.Record(unchecked((int)time), hwnd);
            Changed?.Invoke(hwnd);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"ERREUR focus : {ex}");
        }
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
