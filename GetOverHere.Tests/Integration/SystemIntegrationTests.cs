using GetOverHere.Tests.Support;
using static GetOverHere.Tests.Support.Desktop;

namespace GetOverHere.Tests.Integration;

/// <summary>Cursor, focus, monitors and raw touch input on the real Windows.</summary>
[Collection(nameof(DesktopCollection))]
[Trait("Category", "Integration")]
public class SystemIntegrationTests
{
    // ───────────── Cursor ─────────────

    [Fact]
    public void MoveCursorVisible_PlacesTheCursorExactly_OnEveryMonitor() => RunSta(() =>
    {
        using var keeper = new StateKeeper();

        foreach (var screen in Screen.AllScreens)
        {
            var b = screen.Bounds;
            var points = new[]
            {
                P(b.Left + b.Width / 2, b.Top + b.Height / 2),
                P(b.Left + 1, b.Top + 1),
                P(b.Right - 2, b.Bottom - 2),
                P(b.Left + b.Width / 3, b.Top + 2 * b.Height / 3),
            };
            foreach (var pt in points)
            {
                OwnInput.MoveCursorVisible(pt);
                PumpFor(5);
                Assert.True(CursorPos.Equals(pt), $"Écran {screen.DeviceName} : attendu {pt}, obtenu {CursorPos}");
            }
        }
    });

    [Fact]
    public void VirtualDesktop_IsTheUnionOfAllScreens_InPhysicalPixels() => RunSta(() =>
    {
        var union = Screen.AllScreens.Select(s => s.Bounds).Aggregate(Rectangle.Union);
        var desk = OwnInput.VirtualDesktop();

        Assert.Equal(union.Left, desk.Left);
        Assert.Equal(union.Top, desk.Top);
        Assert.Equal(union.Right, desk.Right);
        Assert.Equal(union.Bottom, desk.Bottom);
    });

    [Fact]
    public void InjectedAbsoluteMove_AloneLandsOnTheExactPixel_OnEveryMonitor() => RunSta(() =>
    {
        // The injected move alone (without SetCursorPos) must already hit the right pixel: exact formula + desktop in physical pixels.
        using var keeper = new StateKeeper();

        foreach (var screen in Screen.AllScreens)
        {
            var b = screen.Bounds;
            foreach (var pt in new[] { P(b.Left + b.Width / 2, b.Top + b.Height / 2), P(b.Left, b.Top), P(b.Right - 1, b.Bottom - 1), P(b.Left + 37, b.Bottom - 53) })
            {
                OwnInput.SendAbsolute(pt);
                Assert.True(PumpUntil(() => CursorPos.Equals(pt), 500), $"Écran {screen.DeviceName} : attendu {pt}, obtenu {CursorPos}");
            }
        }
    });

    [Fact]
    public void Nudge_IsARealInputSeenByHooks_AndLeavesTheCursorExactlyInPlace() => RunSta(() =>
    {
        // Regression: a (0,0) move is not delivered by Windows, so it did not grant the right to change the foreground.
        using var keeper = new StateKeeper();
        var c = PrimaryCenter();
        Native.SetCursorPos(c.X, c.Y);
        int own = 0;
        using var hook = new MouseHook((int msg, in Native.MSLLHOOKSTRUCT d) =>
        {
            if (TouchGuard.Classify(d.dwExtraInfo) == TouchGuard.Source.Own) own++;
            return false;
        });
        hook.Install();

        OwnInput.Nudge();

        Assert.True(PumpUntil(() => own >= 1), "Le hook n'a vu aucune entrée de Nudge");
        PumpFor(50);
        Assert.Equal(c, CursorPos);
    });

    [Fact]
    public void Nudge_AtTheRightEdgeOfTheDesktop_StaysInPlace() => RunSta(() =>
    {
        using var keeper = new StateKeeper();
        var d = OwnInput.VirtualDesktop();
        var edge = P(d.Right - 1, PrimaryCenter().Y);
        Native.SetCursorPos(edge.X, edge.Y);

        OwnInput.Nudge();
        PumpFor(50);

        Assert.Equal(edge, CursorPos);
    });

    // ───────────── Focus ─────────────

    private static Form CreateWindow(string title, int offset)
    {
        var b = Screen.PrimaryScreen!.WorkingArea;
        var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(b.Left + 100 + offset, b.Top + 100 + offset, 300, 200),
            ShowInTaskbar = false,
        };
        form.Show();
        PumpFor(50);
        return form;
    }

    [Fact]
    public void FocusRestorer_BringsWindowsToTheForeground_FromABackgroundProcess() => RunSta(() =>
    {
        using var keeper = new StateKeeper();
        using var a = CreateWindow("Get Over Here test A", 0);
        using var b = CreateWindow("Get Over Here test B", 60);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(FocusRestorer.Restore(a.Handle), $"Tour {i} : A non activée");
            Assert.Equal(a.Handle, Native.GetForegroundWindow());

            Assert.True(FocusRestorer.Restore(b.Handle), $"Tour {i} : B non activée");
            Assert.Equal(b.Handle, Native.GetForegroundWindow());
        }
    });

    [Fact]
    public void FocusRestorer_ReturnsKeyboardFocusToTheTextBoxThatHadIt() => RunSta(() =>
    {
        // The actual need: the text box that was in use must become active again.
        using var keeper = new StateKeeper();
        using var a = CreateWindow("Get Over Here test A", 0);
        var text = new TextBox { Dock = DockStyle.Top };
        var other = new Button { Dock = DockStyle.Bottom };
        a.Controls.Add(text);
        a.Controls.Add(other);
        Assert.True(FocusRestorer.Restore(a.Handle));
        text.Focus();
        PumpFor(20);
        Assert.True(text.Focused);

        using var b = CreateWindow("Get Over Here test B", 60);
        Assert.True(FocusRestorer.Restore(b.Handle));
        PumpFor(20);
        Assert.False(text.Focused);

        Assert.True(FocusRestorer.Restore(a.Handle));
        Assert.True(PumpUntil(() => text.Focused, 1000), "La zone de texte n'a pas retrouvé le focus clavier");
    });

    [Fact]
    public void FocusRestorer_RefusesMinimizedOrInvalidWindows() => RunSta(() =>
    {
        using var keeper = new StateKeeper();
        using var a = CreateWindow("Get Over Here test A", 0);
        a.WindowState = FormWindowState.Minimized;
        PumpFor(50);

        Assert.False(FocusRestorer.Restore(a.Handle));
        Assert.False(FocusRestorer.Restore(IntPtr.Zero));
        Assert.False(FocusRestorer.Restore(0x7FFF_FFF0));
    });

    [Fact]
    public void ForegroundWatcher_NotifiesAndRecordsEachActivation() => RunSta(() =>
    {
        using var keeper = new StateKeeper();
        using var a = CreateWindow("Get Over Here test A", 0);
        using var b = CreateWindow("Get Over Here test B", 60);
        FocusRestorer.Restore(a.Handle);
        PumpFor(50);

        using var watcher = new ForegroundWatcher(skipOwnProcess: false);
        Assert.True(watcher.IsInstalled);
        var notified = new List<IntPtr>();
        watcher.Changed += notified.Add;

        int beforeSwitch = Environment.TickCount;
        FocusRestorer.Restore(b.Handle);
        Assert.True(PumpUntil(() => notified.Contains(b.Handle)), "Aucune notification pour B");
        FocusRestorer.Restore(a.Handle);
        Assert.True(PumpUntil(() => notified.LastOrDefault() == a.Handle), "Aucune notification pour A");

        Assert.Equal(a.Handle, watcher.History.ForegroundBefore(unchecked(Environment.TickCount + 1000), IntPtr.Zero));
        Assert.Equal(a.Handle, watcher.History.ForegroundBefore(beforeSwitch, IntPtr.Zero));
    });

    // ───────────── Monitors and touch screen ─────────────

    [Fact]
    public void MonitorInfo_PrimaryScreenIsNeverTheTablet_AndTheTabletIsFoundWhenPresent() => RunSta(() =>
    {
        MonitorInfo.Refresh();
        var primary = PrimaryCenter();

        if (!MonitorInfo.HasSuperDisplay)
        {
            Assert.True(MonitorInfo.IsOnTouchMonitor(primary)); // documented fallback: everything is accepted
            return;
        }

        Assert.False(MonitorInfo.IsOnTouchMonitor(primary));
        var tablets = Screen.AllScreens
            .Where(s => !s.Primary)
            .Count(s => MonitorInfo.IsOnTouchMonitor(P(s.Bounds.Left + s.Bounds.Width / 2, s.Bounds.Top + s.Bounds.Height / 2)));
        Assert.Equal(1, tablets);
    });

    [Fact]
    public void MonitorInfo_WindowOnTheTablet_IsTheOneOnTheTouchMonitor() => RunSta(() =>
    {
        MonitorInfo.Refresh();
        using var main = CreateWindow("Get Over Here test main", 0);

        Assert.False(MonitorInfo.IsWindowOnTouchMonitor(IntPtr.Zero));
        Assert.Equal(MonitorInfo.IsOnTouchMonitor(PrimaryCenter()), MonitorInfo.IsWindowOnTouchMonitor(main.Handle));

        var tablet = Screen.AllScreens.FirstOrDefault(s => !s.Primary && MonitorInfo.HasSuperDisplay &&
            MonitorInfo.IsOnTouchMonitor(P(s.Bounds.Left + s.Bounds.Width / 2, s.Bounds.Top + s.Bounds.Height / 2)));
        if (tablet is null) return; // no tablet connected: nothing more to check

        using var onTablet = new Form
        {
            Text = "Get Over Here test tablet",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(tablet.WorkingArea.Left + 50, tablet.WorkingArea.Top + 50, 300, 200),
            ShowInTaskbar = false,
        };
        onTablet.Show();
        PumpFor(50);
        Assert.True(MonitorInfo.IsWindowOnTouchMonitor(onTablet.Handle));
    });

    [Fact]
    public void RawTouchInput_RegistersForTheTouchScreen() => RunSta(() =>
    {
        using var raw = new RawTouchInput(_ => { });

        Assert.True(raw.IsRegistered);
        Assert.NotEqual(IntPtr.Zero, raw.Handle);
    });
}
