namespace GetOverHere;

/// <summary>
/// Core of the app. A "touch" is a session that starts with the first touch signal and ends after a short
/// silence; at the end, the cursor and the focus are put back where they were.
///
/// Two sources of touch signals:
///  - raw touch-screen reports (RawTouchInput): arrive as soon as the finger lands;
///  - mouse events Windows generates from touch (mouse hook): arrive in a burst when the finger is lifted,
///    and they are what moves the cursor ("hijacked cursor").
///
/// Important: nothing expensive (SendInput, SetCursorPos, SetForegroundWindow) may run INSIDE the mouse hook,
/// otherwise Windows stalls for ~300 ms and drops events. The restore is therefore posted to the message queue.
/// </summary>
internal sealed class TouchGuard : IDisposable
{
    // Signature Windows puts in dwExtraInfo of mouse events generated from touch or pen.
    internal const uint PointerSignatureMask = 0xFFFFFF00;
    internal const uint PointerSignature = 0xFF515700;
    internal const uint TouchBit = 0x80; // set = finger, clear = pen

    // Without raw reports, the touch is only seen on release: assume it started at most this long before.
    internal const int TapLookbackMs = 500;
    // A real click activates its window a few ms later: the touch necessarily comes after that activation.
    internal const int ClickActivationGraceMs = 150;
    internal const int MaxFocusRestoresPerTouch = 3; // avoids ping-pong with an app that forces itself to the foreground

    internal enum Source { Mouse, Touch, Pen, Own }

    private readonly Settings _settings;
    private readonly ForegroundHistory _history;
    private readonly ITouchPlatform _platform;
    private readonly IDispatcher _dispatcher;
    // During a touch: end of the touch. Between touches: end of the cooldown before the focus comes back.
    private readonly IOneShotTimer _timer;

    private bool _active;      // touch session in progress
    private bool _hijacked;    // Windows moved the cursor onto the tablet during this session
    private bool _buttonDown;
    private bool _restorePosted;
    private bool _focusReturnPending; // cursor restored, focus given back when the cooldown ends
    private int _keysDuringCooldown;

    // Last position set by the real mouse (or the pen): this is where the cursor is brought back.
    private Native.POINT _anchor;
    private Native.POINT _savedPos;
    private IntPtr _savedForeground;
    private int? _lastRealClickTime;
    private long _lastTouchTicks = long.MinValue / 2;
    private long _focusGuardUntil;
    private int _focusRestores;
    private int _rawReports;

    public TouchGuard(Settings settings, ForegroundHistory history, ITouchPlatform platform,
                      IDispatcher dispatcher, IOneShotTimer timer)
    {
        _settings = settings;
        _history = history;
        _platform = platform;
        _dispatcher = dispatcher;
        _timer = timer;
        _anchor = platform.GetCursorPos();
        _timer.Elapsed += OnTimerElapsed;
    }

    // Exposed for tests and diagnostics.
    internal bool IsSessionActive => _active;
    internal bool IsHijacked => _hijacked;
    internal Native.POINT Anchor => _anchor;
    internal IntPtr SavedForeground => _savedForeground;
    internal bool IsFocusGuardActive => _platform.TickCount64 < _focusGuardUntil;
    internal bool IsFocusReturnPending => _focusReturnPending;

    /// <summary>
    /// Raised when a cooldown starts or ends (see <see cref="IsFocusReturnPending"/>): the keyboard only needs
    /// to be watched meanwhile. May be raised from inside the mouse hook: handlers must only post work.
    /// </summary>
    public event Action? FocusReturnPendingChanged;

    private bool FocusReturnPending
    {
        set
        {
            if (_focusReturnPending == value) return;
            _focusReturnPending = value;
            _keysDuringCooldown = 0;
            FocusReturnPendingChanged?.Invoke();
        }
    }

    /// <summary>Cooldown applied after a touch before the focus comes back (0 = immediately).</summary>
    private int FocusReturnDelay =>
        _settings.DelayFocusReturn && _settings.RestoreFocus ? Math.Max(0, _settings.FocusReturnDelayMs) : 0;

    // ───────────────────────── Touch signals ─────────────────────────

    /// <summary>Raw touch-screen report (finger down / moving / up).</summary>
    public void OnRawTouch(int messageTime)
    {
        if (!_settings.Enabled) return;
        _rawReports++;
        if (!_active) StartSession(messageTime, "lecture directe");
        ArmTimer();
    }

    /// <summary>Called from the hook for every mouse event. Returns true to block the event.</summary>
    public bool OnMouseEvent(int msg, in Native.MSLLHOOKSTRUCT data)
    {
        var source = Classify(data.dwExtraInfo);

        if (DiagnosticLog.Enabled && (msg != Native.WM_MOUSEMOVE || source != Source.Mouse
                                      || _platform.TickCount64 - _lastTouchTicks < 2000))
        {
            DiagnosticLog.Write($"{MessageName(msg),-12} {source,-5} pt={data.pt} extra=0x{(ulong)data.dwExtraInfo:X} " +
                                $"flags=0x{data.flags:X} écran={_platform.DescribeMonitor(data.pt)} " +
                                $"session={(_active ? (_hijacked ? "détournée" : "active") : "-")}");
        }

        switch (source)
        {
            case Source.Touch:
                return OnPromotedTouch(msg, data);

            case Source.Mouse:
                return OnRealMouse(msg, data);

            case Source.Pen:
                if (_active) Cancel();
                _anchor = data.pt;
                EndFocusGuard();
                return false;

            default: // our own injected input
                return false;
        }
    }

    private bool OnPromotedTouch(int msg, in Native.MSLLHOOKSTRUCT data)
    {
        if (!_settings.Enabled) return false;

        if (!_active)
        {
            if (!_platform.IsOnTouchMonitor(data.pt)) return false;
            StartSession(EstimateContactTime(unchecked((int)data.time)), "événement souris tactile");
        }

        _hijacked = true;
        if (IsButtonDown(msg)) _buttonDown = true;
        else if (IsButtonUp(msg)) _buttonDown = false;
        ArmTimer();

        return _settings.ZeroMoveMode;
    }

    /// <summary>
    /// Touch not seen by the raw reader: it started a little before the release,
    /// but necessarily after the activation caused by the last real click.
    /// </summary>
    internal int EstimateContactTime(int releaseTime)
    {
        int contact = unchecked(releaseTime - TapLookbackMs);
        if (_lastRealClickTime is int click)
        {
            int afterClick = unchecked(click + ClickActivationGraceMs);
            if (unchecked(afterClick - contact) > 0) contact = afterClick;
            if (unchecked(contact - releaseTime) > 0) contact = releaseTime;
        }
        return contact;
    }

    private bool OnRealMouse(int msg, in Native.MSLLHOOKSTRUCT data)
    {
        if (IsButtonDown(msg))
        {
            _lastRealClickTime = unchecked((int)data.time);
            if (!_active) EndFocusGuard(); // real click: the focus change that follows is intended
        }

        if (!_active)
        {
            _anchor = data.pt;
            return false;
        }

        if (!_hijacked)
        {
            // Finger down but cursor not moved yet: the mouse still owns its position.
            _anchor = _savedPos = data.pt;
            return false;
        }

        // The cursor sits on the tablet because of the touch and the user moves the mouse:
        // drop this move (computed from the tablet) but apply its delta to the original position,
        // then bring the cursor back right after the hook returns.
        if (msg == Native.WM_MOUSEMOVE)
        {
            var cur = _platform.GetCursorPos();
            _savedPos.X += data.pt.X - cur.X;
            _savedPos.Y += data.pt.Y - cur.Y;
        }
        PostRestore("souris utilisée");
        return msg == Native.WM_MOUSEMOVE;
    }

    // ───────────────────────── Session ─────────────────────────

    private void StartSession(int contactTime, string via)
    {
        _active = true;
        _hijacked = false;
        _buttonDown = false;
        _rawReports = 0;
        _savedPos = _anchor;
        FocusReturnPending = false; // the end of this touch starts a new cooldown

        // Chained touch during the focus guard: keep the original window already remembered.
        bool chained = IsFocusGuardActive && _savedForeground != IntPtr.Zero && _platform.IsWindow(_savedForeground);
        if (!chained)
        {
            // The touched window may have taken the focus before we were told: look it up in the history.
            _savedForeground = _history.ForegroundBefore(contactTime, _platform.GetForegroundWindow());
            _focusRestores = 0;
        }
        _focusGuardUntil = long.MaxValue; // guard active as long as the touch is not over

        Log(() => $"  -> début toucher ({via}{(chained ? ", enchaîné" : "")}), curseur mémorisé {_savedPos}, " +
                  $"focus {_platform.DescribeWindow(_savedForeground)}");
    }

    private void ArmTimer()
    {
        _lastTouchTicks = _platform.TickCount64;
        int delay = _buttonDown ? _settings.HeldTimeoutMs
                  : _hijacked ? _settings.RestoreDelayMs
                  : _settings.RawQuietMs;
        _timer.Start(Math.Max(1, delay));
    }

    private void PostRestore(string reason)
    {
        if (_restorePosted) return;
        _restorePosted = true;
        _dispatcher.Post(() =>
        {
            _restorePosted = false;
            Restore(reason);
        });
    }

    private void Restore(string reason)
    {
        if (!_active) return; // the timer may be counting the focus cooldown: leave it alone
        _timer.Stop();
        _active = false;
        _buttonDown = false;

        var before = _platform.GetCursorPos();
        if (_hijacked || before.X != _savedPos.X || before.Y != _savedPos.Y)
            _platform.MoveCursorVisible(_savedPos);
        _hijacked = false;
        _anchor = _savedPos;
        Log(() => $"  -> retour ({reason}, {_rawReports} rapports bruts) curseur {before} -> {_savedPos}");

        int delay = FocusReturnDelay;
        if (delay > 0)
        {
            // The touched window keeps the keyboard during the cooldown; the guard then covers late grabs as usual.
            FocusReturnPending = true;
            _focusGuardUntil = _platform.TickCount64 + delay + _settings.FocusGuardMs;
            _timer.Start(delay);
            Log(() => $"  -> focus rendu dans {delay} ms");
            return;
        }

        _focusGuardUntil = _platform.TickCount64 + _settings.FocusGuardMs;
        EnsureFocus();
    }

    private void OnTimerElapsed()
    {
        if (_active)
        {
            Restore("fin du toucher");
        }
        else if (_focusReturnPending)
        {
            int keys = _keysDuringCooldown;
            FocusReturnPending = false;
            Log(() => $"  -> fin du délai ({keys} frappes clavier), retour du focus");
            EnsureFocus();
        }
    }

    /// <summary>
    /// Keyboard hook event, during the cooldown only.
    ///  - Typing in a window on the tablet (e.g. a Discord message): the user wants to stay there, however long
    ///    they think before the first key. The return is cancelled.
    ///  - Typing elsewhere: the cooldown starts over from the last key press, so the focus is never taken away
    ///    in the middle of a sentence.
    /// </summary>
    public void OnKeyboardEvent(int msg)
    {
        if (!_focusReturnPending || msg is not (Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)) return;

        var current = _platform.GetForegroundWindow();
        if (current != _savedForeground && _platform.IsWindowOnTouchMonitor(current))
        {
            Log(() => $"  -> frappe clavier dans {_platform.DescribeWindow(current)} (tablette)");
            EndFocusGuard();
            return;
        }

        int delay = FocusReturnDelay;
        if (delay <= 0) return; // option turned off meanwhile: let the current countdown end

        _keysDuringCooldown++;
        _focusGuardUntil = _platform.TickCount64 + delay + _settings.FocusGuardMs;
        _timer.Start(delay);
    }

    private void Cancel()
    {
        _timer.Stop();
        _active = false;
        _hijacked = false;
        _buttonDown = false;
        FocusReturnPending = false;
        _focusGuardUntil = 0;
    }

    /// <summary>The user picked a window themselves (real click, pen): stop correcting the focus for this touch.</summary>
    private void EndFocusGuard()
    {
        _focusGuardUntil = 0;
        if (!_focusReturnPending) return;
        FocusReturnPending = false;
        _timer.Stop();
        Log(() => "  -> retour du focus annulé");
    }

    /// <summary>Called when the app is disabled: cleanly ends a touch in progress.</summary>
    public void Reset() => Cancel();

    // ───────────────────────── Focus ─────────────────────────

    /// <summary>Called on every foreground window change.</summary>
    public void OnForegroundChanged(IntPtr hwnd)
    {
        bool guarded = IsFocusGuardActive;
        Log(() => $"PREMIER PLAN -> {_platform.DescribeWindow(hwnd)}{(guarded ? $" (garde active{(_active ? ", toucher en cours" : _focusReturnPending ? ", retour différé" : "")})" : "")}");

        // During the touch, Restore() takes care of it; during the cooldown, the timer does;
        // afterwards, correct as long as the guard is active.
        if (guarded && !_active && !_focusReturnPending) EnsureFocus();
    }

    private void EnsureFocus()
    {
        if (!_settings.Enabled || !_settings.RestoreFocus || _savedForeground == IntPtr.Zero) return;

        var current = _platform.GetForegroundWindow();
        if (current == _savedForeground || current == IntPtr.Zero) return;
        if (!_platform.IsWindow(_savedForeground)) return;

        if (_focusRestores >= MaxFocusRestoresPerTouch)
        {
            Log(() => "  -> focus : trop de tentatives, abandon");
            return;
        }
        _focusRestores++;

        bool ok = _platform.ActivateWindow(_savedForeground);
        Log(() => $"  -> focus {_platform.DescribeWindow(current)} -> {_platform.DescribeWindow(_savedForeground)} {(ok ? "OK" : "ÉCHEC")}");
    }

    // ───────────────────────── Helpers ─────────────────────────

    /// <summary>Builds the message only when logging is on (keeps the hook path allocation-free otherwise).</summary>
    private static void Log(Func<string> message)
    {
        if (DiagnosticLog.Enabled) DiagnosticLog.Write(message());
    }

    private static bool IsButtonDown(int msg) =>
        msg is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN;

    private static bool IsButtonUp(int msg) =>
        msg is Native.WM_LBUTTONUP or Native.WM_RBUTTONUP or Native.WM_MBUTTONUP;

    /// <summary>Tells where a mouse event comes from, using the low 32 bits of dwExtraInfo.</summary>
    internal static Source Classify(nuint extraInfo)
    {
        uint low = unchecked((uint)extraInfo);
        if (low == OwnInput.Signature) return Source.Own;
        if ((low & PointerSignatureMask) != PointerSignature) return Source.Mouse;
        return (low & TouchBit) != 0 ? Source.Touch : Source.Pen;
    }

    private static string MessageName(int msg) => msg switch
    {
        Native.WM_MOUSEMOVE => "MOVE",
        Native.WM_LBUTTONDOWN => "L_DOWN",
        Native.WM_LBUTTONUP => "L_UP",
        Native.WM_RBUTTONDOWN => "R_DOWN",
        Native.WM_RBUTTONUP => "R_UP",
        Native.WM_MBUTTONDOWN => "M_DOWN",
        Native.WM_MBUTTONUP => "M_UP",
        Native.WM_MOUSEWHEEL => "WHEEL",
        Native.WM_MOUSEHWHEEL => "HWHEEL",
        _ => $"0x{msg:X}",
    };

    public void Dispose() => _timer.Dispose();
}
