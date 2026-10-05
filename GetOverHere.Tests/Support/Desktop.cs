using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace GetOverHere.Tests.Support;

/// <summary>Helpers for integration tests on the real Windows desktop.</summary>
internal static class Desktop
{
    private static readonly IntPtr DpiAwarenessPerMonitorV2 = -4;

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    /// <summary>
    /// Runs the test on a dedicated STA thread, with the same DPI handling as the app (physical pixels),
    /// and rethrows any exception on the test's thread.
    /// </summary>
    public static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                SetThreadDpiAwarenessContext(DpiAwarenessPerMonitorV2);
                body();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Test d'intégration bloqué plus de 30 s");
        error?.Throw();
    }

    /// <summary>Pumps Windows messages (hooks, timers, BeginInvoke) until the condition is true.</summary>
    public static bool PumpUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) return false;
            Application.DoEvents();
            Thread.Sleep(1);
        }
        return true;
    }

    /// <summary>Pumps messages for a fixed duration.</summary>
    public static void PumpFor(int ms) => PumpUntil(() => false, ms);

    public static Native.POINT CursorPos
    {
        get
        {
            Native.GetCursorPos(out var p);
            return p;
        }
    }

    public static Native.POINT P(int x, int y) => new() { X = x, Y = y };

    /// <summary>Injects an absolute mouse move to a pixel, with the given dwExtraInfo.</summary>
    public static void InjectAbsoluteMove(Native.POINT pt, uint extraInfo)
    {
        var d = OwnInput.VirtualDesktop();
        var (nx, ny) = OwnInput.ToAbsolute(pt, d.Left, d.Top, d.Right - d.Left, d.Bottom - d.Top);
        Send(nx, ny, Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK, extraInfo);
    }

    /// <summary>Injected relative move. Beware: Windows does not deliver a (0,0) move.</summary>
    public static void InjectRelativeMove(int dx, int dy, uint extraInfo)
    {
        Assert.False(dx == 0 && dy == 0, "Un mouvement (0,0) n'est jamais transmis par Windows : le test serait vide");
        Send(dx, dy, Native.MOUSEEVENTF_MOVE, extraInfo);
    }

    private static void Send(int dx, int dy, uint flags, uint extraInfo)
    {
        var input = new[]
        {
            new Native.INPUT
            {
                type = Native.INPUT_MOUSE,
                mi = new Native.MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags, dwExtraInfo = extraInfo },
            },
        };
        Assert.Equal(1u, Native.SendInput(1, input, Marshal.SizeOf<Native.INPUT>()));
    }

    /// <summary>Center of the main screen, in physical pixels.</summary>
    public static Native.POINT PrimaryCenter()
    {
        var b = Screen.PrimaryScreen!.Bounds;
        return P(b.Left + b.Width / 2, b.Top + b.Height / 2);
    }

    /// <summary>Restores the cursor position and the active window at the end of a test that changes them.</summary>
    public sealed class StateKeeper : IDisposable
    {
        private readonly Native.POINT _cursor = CursorPos;
        private readonly IntPtr _foreground = Native.GetForegroundWindow();

        public void Dispose()
        {
            Native.SetCursorPos(_cursor.X, _cursor.Y);
            if (_foreground != IntPtr.Zero) FocusRestorer.Restore(_foreground);
        }
    }
}
