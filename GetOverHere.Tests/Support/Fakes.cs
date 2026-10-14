namespace GetOverHere.Tests.Support;

/// <summary>Manual clock shared by all test doubles (ms, same base as GetTickCount).</summary>
internal sealed class FakeClock
{
    public long Now { get; set; } = 10_000_000;
}

/// <summary>One-shot timer driven by <see cref="FakeClock"/>.</summary>
internal sealed class FakeTimer(FakeClock clock) : IOneShotTimer
{
    public event Action? Elapsed;

    public long? DueAt { get; private set; }
    public int? LastInterval { get; private set; }
    public bool Disposed { get; private set; }

    public void Start(int intervalMs)
    {
        Assert.True(intervalMs >= 1, $"Intervalle invalide : {intervalMs}");
        LastInterval = intervalMs;
        DueAt = clock.Now + intervalMs;
    }

    public void Stop() => DueAt = null;

    /// <summary>Fires the timer if its due time has been reached.</summary>
    public bool FireIfDue()
    {
        if (DueAt is not long due || due > clock.Now) return false;
        DueAt = null;
        Elapsed?.Invoke();
        return true;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Queue of posted actions, run only when the test "pumps" messages.</summary>
internal sealed class FakeDispatcher : IDispatcher
{
    private readonly Queue<Action> _queue = new();

    public int Pending => _queue.Count;

    public void Post(Action action) => _queue.Enqueue(action);

    public void RunAll()
    {
        // Safety bound: an infinite message loop is a bug to report, not to wait for.
        for (int i = 0; _queue.Count > 0; i++)
        {
            Assert.True(i < 1000, "Plus de 1000 messages traités d'affilée : boucle infinie probable");
            _queue.Dequeue()();
        }
    }
}

/// <summary>Simulated platform: a main screen at X ≥ 0 and the tablet at X &lt; 0 (as on the user's machine).</summary>
internal sealed class FakePlatform(FakeClock clock) : ITouchPlatform
{
    public Native.POINT CursorPos { get; set; }
    public IntPtr Foreground { get; set; }

    public List<Native.POINT> CursorMoves { get; } = new();
    public List<IntPtr> Activations { get; } = new();
    public HashSet<IntPtr> DeadWindows { get; } = new();

    public bool ActivationSucceeds { get; set; } = true;

    /// <summary>Called after a successful activation (the rig uses it to simulate the WinEvent notification).</summary>
    public Action<IntPtr>? OnActivated { get; set; }

    public long TickCount64 => clock.Now;

    public Native.POINT GetCursorPos() => CursorPos;

    public void MoveCursorVisible(Native.POINT pt)
    {
        CursorMoves.Add(pt);
        CursorPos = pt;
    }

    public IntPtr GetForegroundWindow() => Foreground;

    public bool IsWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && !DeadWindows.Contains(hwnd);

    public bool ActivateWindow(IntPtr hwnd)
    {
        Activations.Add(hwnd);
        if (!ActivationSucceeds) return false;
        Foreground = hwnd;
        OnActivated?.Invoke(hwnd);
        return true;
    }

    public bool IsOnTouchMonitor(Native.POINT pt) => pt.X < 0;

    /// <summary>Windows placed on the tablet (the others are on the main screen).</summary>
    public HashSet<IntPtr> WindowsOnTablet { get; } = new();

    public bool IsWindowOnTouchMonitor(IntPtr hwnd) => WindowsOnTablet.Contains(hwnd);

    public string DescribeWindow(IntPtr hwnd) => $"fenêtre 0x{hwnd:X}";

    public string DescribeMonitor(Native.POINT pt) => pt.X < 0 ? "tablette" : "principal";
}
