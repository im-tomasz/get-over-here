using System.Diagnostics;
using GetOverHere.Tests.Support;
using static GetOverHere.Tests.Support.Desktop;

namespace GetOverHere.Tests.Integration;

/// <summary>
/// Full real pipeline (Win32 hook, WinForms timer, message queue, SendInput, SetCursorPos):
/// events carrying Windows' touch signature are injected towards the tablet, exactly like
/// SuperDisplay does, and the cursor must come back on its own.
/// No click is injected: nothing is triggered in the tablet apps.
/// </summary>
[Collection(nameof(DesktopCollection))]
[Trait("Category", "Integration")]
public class EndToEndTests
{
    private sealed class Pipeline : IDisposable
    {
        public Settings Settings { get; } = new();
        public ForegroundWatcher Watcher { get; } = new();
        public ControlDispatcher Dispatcher { get; } = new();
        public TouchGuard Guard { get; }
        public MouseHook Hook { get; }
        public int TouchEventsSeen;

        public Pipeline()
        {
            Guard = new TouchGuard(Settings, Watcher.History, new Win32TouchPlatform(), Dispatcher, new FormsOneShotTimer());
            Watcher.Changed += Guard.OnForegroundChanged;
            Hook = new MouseHook((int msg, in Native.MSLLHOOKSTRUCT d) =>
            {
                if (TouchGuard.Classify(d.dwExtraInfo) == TouchGuard.Source.Touch) TouchEventsSeen++;
                return Guard.OnMouseEvent(msg, in d);
            });
            Hook.Install();
        }

        public void Dispose()
        {
            Hook.Dispose();
            Guard.Dispose();
            Watcher.Dispose();
            Dispatcher.Dispose();
        }
    }

    private static void RequireAppStopped() =>
        Assert.True(Process.GetProcessesByName(AppInfo.Id).Length == 0,
            "Précondition : quittez l'appli Get Over Here! (icône > Quitter) avant les tests de bout en bout, sinon elle réagit aussi aux touchers simulés.");

    /// <summary>A point on the touch screen (the tablet if it is connected).</summary>
    private static Native.POINT TouchTarget()
    {
        MonitorInfo.Refresh();
        var screen = Screen.AllScreens.FirstOrDefault(s => !s.Primary && MonitorInfo.IsOnTouchMonitor(Center(s)))
                     ?? Screen.PrimaryScreen!;
        var c = Center(screen);
        return screen.Primary ? P(c.X - 300, c.Y - 200) : c;
    }

    private static Native.POINT Center(Screen s) => P(s.Bounds.Left + s.Bounds.Width / 2, s.Bounds.Top + s.Bounds.Height / 2);

    /// <summary>Places the cursor like the real mouse would (event without signature).</summary>
    private static Native.POINT PlaceRealCursor(Native.POINT pt)
    {
        InjectAbsoluteMove(pt, 0);
        PumpFor(30);
        return CursorPos;
    }

    [Fact]
    public void SimulatedTouchOnTablet_CursorComesBackToItsExactPosition_AfterTheRestoreDelay() => RunSta(() =>
    {
        RequireAppStopped();
        using var keeper = new StateKeeper();
        var start = PlaceRealCursor(PrimaryCenter());
        var target = TouchTarget();
        using var pipeline = new Pipeline();

        var sw = Stopwatch.StartNew();
        InjectAbsoluteMove(target, TouchRig.TouchExtra);

        Assert.True(PumpUntil(() => pipeline.TouchEventsSeen > 0), "Le hook n'a pas vu le toucher simulé");
        Assert.True(PumpUntil(() => pipeline.Guard.IsSessionActive == false && CursorPos.Equals(start), 2000),
                    $"Curseur en {CursorPos}, attendu {start}");
        Assert.InRange(sw.ElapsedMilliseconds, pipeline.Settings.RestoreDelayMs - 5, 1000);

        PumpFor(200);
        Assert.Equal(start, CursorPos); // and it stays there
    });

    [Fact]
    public void SimulatedTouch_RepeatedRapidly_AlwaysEndsAtTheStartPosition() => RunSta(() =>
    {
        RequireAppStopped();
        using var keeper = new StateKeeper();
        var start = PlaceRealCursor(PrimaryCenter());
        var target = TouchTarget();
        using var pipeline = new Pipeline();

        for (int i = 0; i < 10; i++)
        {
            InjectAbsoluteMove(P(target.X + 7 * i, target.Y + 3 * i), TouchRig.TouchExtra);
            PumpFor(15 + 10 * (i % 3)); // sometimes before, sometimes after the restore delay
        }

        Assert.True(PumpUntil(() => !pipeline.Guard.IsSessionActive && CursorPos.Equals(start), 2000),
                    $"Curseur en {CursorPos}, attendu {start}");
    });

    [Fact]
    public void RealMouseMovingRightAfterTheTouch_IsReportedFromTheStartPosition_NotFromTheTablet() => RunSta(() =>
    {
        RequireAppStopped();
        using var keeper = new StateKeeper();
        var start = PlaceRealCursor(PrimaryCenter());
        var target = TouchTarget();
        using var pipeline = new Pipeline();

        InjectAbsoluteMove(target, TouchRig.TouchExtra);
        Assert.True(PumpUntil(() => pipeline.Guard.IsHijacked));
        InjectRelativeMove(3, 0, 0); // touchpad, a few ms after the release
        Assert.True(PumpUntil(() => !pipeline.Guard.IsSessionActive, 1000));
        PumpFor(100);

        var end = CursorPos;
        Assert.True(Screen.FromPoint(new Point(end.X, end.Y)).Primary, $"Curseur resté hors de l'écran principal : {end}");
        Assert.InRange(end.X, start.X, start.X + 15); // the small move is carried over, neither lost nor applied from the tablet
        Assert.Equal(start.Y, end.Y);
    });

    [Fact]
    public void ZeroMoveMode_TheCursorNeverLeavesItsPosition() => RunSta(() =>
    {
        RequireAppStopped();
        using var keeper = new StateKeeper();
        var start = PlaceRealCursor(PrimaryCenter());
        var target = TouchTarget();
        using var pipeline = new Pipeline();
        pipeline.Settings.ZeroMoveMode = true;

        InjectAbsoluteMove(target, TouchRig.TouchExtra);
        Assert.True(PumpUntil(() => pipeline.TouchEventsSeen > 0));
        Assert.Equal(start, CursorPos);

        PumpFor(150);
        Assert.Equal(start, CursorPos);
    });

    [Fact]
    public void Disabled_TheCursorStaysWhereTheTouchPutIt() => RunSta(() =>
    {
        RequireAppStopped();
        using var keeper = new StateKeeper();
        PlaceRealCursor(PrimaryCenter());
        var target = TouchTarget();
        using var pipeline = new Pipeline();
        pipeline.Settings.Enabled = false;

        InjectAbsoluteMove(target, TouchRig.TouchExtra);
        Assert.True(PumpUntil(() => pipeline.TouchEventsSeen > 0));
        PumpFor(200);

        Assert.InRange(CursorPos.X, target.X - 1, target.X + 1);
        Assert.InRange(CursorPos.Y, target.Y - 1, target.Y + 1);
    });
}
