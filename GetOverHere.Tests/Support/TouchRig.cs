namespace GetOverHere.Tests.Support;

/// <summary>
/// Test rig for <see cref="TouchGuard"/>. It reproduces the Windows behaviour observed in the real logs:
///  - mouse events generated from touch arrive in a burst (MOVE, L_DOWN, L_UP) when the finger is lifted
///    and, unless blocked, move the cursor to the touched point;
///  - a real mouse move is relative to the current cursor position;
///  - foreground changes are notified asynchronously (out-of-context WinEvent).
/// </summary>
internal sealed class TouchRig : IDisposable
{
    public static readonly IntPtr WinA = 0xA0; // working window on the main screen (e.g. a prompt box)
    public static readonly IntPtr WinB = 0xB0; // window on the tablet
    public static readonly IntPtr WinC = 0xC0; // another window on the main screen

    public const uint TouchExtra = 0xFF515785; // touch signature (bit 0x80)
    public const uint PenExtra = 0xFF515705;   // pen signature

    public static readonly Native.POINT Start = P(800, 450);
    public static readonly Native.POINT OnTablet = P(-1000, 600);

    public FakeClock Clock { get; } = new();
    public FakePlatform Platform { get; }
    public FakeDispatcher Dispatcher { get; } = new();
    public FakeTimer Timer { get; }
    public ForegroundHistory History { get; } = new();
    public Settings Settings { get; } = new();
    public TouchGuard Guard { get; }

    public TouchRig(Action<Settings>? configure = null)
    {
        configure?.Invoke(Settings);
        Platform = new FakePlatform(Clock) { CursorPos = Start, Foreground = WinA };
        Timer = new FakeTimer(Clock);

        // Like ForegroundWatcher: the window active at launch, dated one hour back.
        History.Record(unchecked((int)Clock.Now - 3_600_000), WinA);

        // Our own SetForegroundWindow also triggers an asynchronous notification.
        Platform.OnActivated = hwnd => Dispatcher.Post(() => NotifyForeground(hwnd));

        Guard = new TouchGuard(Settings, History, Platform, Dispatcher, Timer);
    }

    public static Native.POINT P(int x, int y) => new() { X = x, Y = y };

    public int Now => unchecked((int)Clock.Now);

    // ───────────── Simulated input ─────────────

    /// <summary>Raw event passed to the hook; returns true if it was blocked.</summary>
    public bool Hook(int msg, Native.POINT pt, uint extra)
    {
        var data = new Native.MSLLHOOKSTRUCT
        {
            pt = pt,
            time = unchecked((uint)Clock.Now),
            dwExtraInfo = extra,
            flags = extra == 0 ? 0u : Native.LLMHF_INJECTED,
        };
        return Guard.OnMouseEvent(msg, in data);
    }

    /// <summary>Mouse event Windows generates from touch; if it goes through, Windows moves the cursor.</summary>
    public bool TouchEvent(int msg, Native.POINT pt)
    {
        bool blocked = Hook(msg, pt, TouchExtra);
        if (!blocked) Platform.CursorPos = pt;
        return blocked;
    }

    /// <summary>Burst received when a tap is released. Returns true if at least one event was blocked.</summary>
    public bool TapBurst(Native.POINT pt)
    {
        bool blocked = TouchEvent(Native.WM_MOUSEMOVE, pt);
        blocked |= TouchEvent(Native.WM_LBUTTONDOWN, pt);
        blocked |= TouchEvent(Native.WM_LBUTTONUP, pt);
        return blocked;
    }

    /// <summary>A full tap as the app sees it: raw report on contact, then the burst on release.</summary>
    public void TapWithRaw(Native.POINT pt, int holdMs = 80, IntPtr? activatesOnContact = null)
    {
        Raw();
        if (activatesOnContact is IntPtr hwnd)
        {
            Advance(10);
            SystemActivates(hwnd);
            holdMs -= 10;
        }
        Advance(holdMs);
        Raw();
        TapBurst(pt);
    }

    /// <summary>Relative move of the real mouse / touchpad.</summary>
    public bool RealMove(int dx, int dy)
    {
        var pt = P(Platform.CursorPos.X + dx, Platform.CursorPos.Y + dy);
        bool blocked = Hook(Native.WM_MOUSEMOVE, pt, 0);
        if (!blocked) Platform.CursorPos = pt;
        return blocked;
    }

    public bool RealButton(int msg) => Hook(msg, Platform.CursorPos, 0);

    public void RealClick()
    {
        RealButton(Native.WM_LBUTTONDOWN);
        RealButton(Native.WM_LBUTTONUP);
    }

    public bool PenMove(Native.POINT pt)
    {
        bool blocked = Hook(Native.WM_MOUSEMOVE, pt, PenExtra);
        if (!blocked) Platform.CursorPos = pt;
        return blocked;
    }

    /// <summary>Raw touch-screen report.</summary>
    public void Raw() => Guard.OnRawTouch(Now);

    /// <summary>Windows activates a window (e.g. the one just touched): immediate notification.</summary>
    public void SystemActivates(IntPtr hwnd)
    {
        Platform.Foreground = hwnd;
        NotifyForeground(hwnd);
    }

    private void NotifyForeground(IntPtr hwnd)
    {
        History.Record(Now, hwnd);
        Guard.OnForegroundChanged(hwnd);
    }

    // ───────────── Time ─────────────

    /// <summary>Advances the clock, firing the timer at its exact due time, then runs the posted messages.</summary>
    public void Advance(int ms)
    {
        Assert.True(ms >= 0);
        long target = Clock.Now + ms;
        while (Timer.DueAt is long due && due <= target)
        {
            Clock.Now = due;
            Timer.FireIfDue();
            Dispatcher.RunAll();
        }
        Clock.Now = target;
        Dispatcher.RunAll();
    }

    public void Pump() => Dispatcher.RunAll();

    public void Dispose() => Guard.Dispose();
}
