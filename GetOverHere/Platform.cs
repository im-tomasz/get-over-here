namespace GetOverHere;

/// <summary>
/// Everything TouchGuard needs from the system. The real implementation calls Win32;
/// tests provide a simulated one to replay scenarios to the millisecond.
/// </summary>
internal interface ITouchPlatform
{
    /// <summary>Monotonic clock in ms (same base as GetTickCount / message times).</summary>
    long TickCount64 { get; }

    Native.POINT GetCursorPos();

    /// <summary>Puts the cursor on the exact pixel and makes it visible.</summary>
    void MoveCursorVisible(Native.POINT pt);

    IntPtr GetForegroundWindow();

    bool IsWindow(IntPtr hwnd);

    /// <summary>Brings a window to the foreground; true if it actually is.</summary>
    bool ActivateWindow(IntPtr hwnd);

    /// <summary>Is the point on the touch screen (tablet)?</summary>
    bool IsOnTouchMonitor(Native.POINT pt);

    string DescribeWindow(IntPtr hwnd);

    string DescribeMonitor(Native.POINT pt);
}

/// <summary>Runs an action later on the UI thread (after the mouse hook has returned).</summary>
internal interface IDispatcher
{
    void Post(Action action);
}

/// <summary>One-shot timer: Start restarts the countdown, Elapsed is raised once.</summary>
internal interface IOneShotTimer : IDisposable
{
    event Action? Elapsed;
    void Start(int intervalMs);
    void Stop();
}

internal sealed class Win32TouchPlatform : ITouchPlatform
{
    public long TickCount64 => Environment.TickCount64;

    public Native.POINT GetCursorPos()
    {
        Native.GetCursorPos(out var pt);
        return pt;
    }

    public void MoveCursorVisible(Native.POINT pt) => OwnInput.MoveCursorVisible(pt);

    public IntPtr GetForegroundWindow() => Native.GetForegroundWindow();

    public bool IsWindow(IntPtr hwnd) => Native.IsWindow(hwnd);

    public bool ActivateWindow(IntPtr hwnd) => FocusRestorer.Restore(hwnd);

    public bool IsOnTouchMonitor(Native.POINT pt) => MonitorInfo.IsOnTouchMonitor(pt);

    public string DescribeWindow(IntPtr hwnd) => Native.WindowTitle(hwnd);

    public string DescribeMonitor(Native.POINT pt) => MonitorInfo.NameAt(pt);
}

/// <summary>Posts actions through a hidden window created on the UI thread.</summary>
internal sealed class ControlDispatcher : IDispatcher, IDisposable
{
    private readonly Control _control = new();

    public ControlDispatcher() => _ = _control.Handle;

    public void Post(Action action) => _control.BeginInvoke(action);

    public void Dispose() => _control.Dispose();
}

internal sealed class FormsOneShotTimer : IOneShotTimer
{
    private readonly System.Windows.Forms.Timer _timer = new();

    public event Action? Elapsed;

    public FormsOneShotTimer()
    {
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Elapsed?.Invoke();
        };
    }

    public void Start(int intervalMs)
    {
        _timer.Stop();
        _timer.Interval = Math.Max(1, intervalMs);
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Dispose() => _timer.Dispose();
}
