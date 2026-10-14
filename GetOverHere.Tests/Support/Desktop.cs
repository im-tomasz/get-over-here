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
    /// Real (non-injected) mouse input during the test is counted: if the test fails while the user was moving
    /// the mouse, the failure says so, instead of looking like a bug in the app.
    /// </summary>
    public static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        int userInput = 0;
        var thread = new Thread(() =>
        {
            try
            {
                SetThreadDpiAwarenessContext(DpiAwarenessPerMonitorV2);
                using var watcher = new MouseHook((int msg, in Native.MSLLHOOKSTRUCT d) =>
                {
                    if ((d.flags & Native.LLMHF_INJECTED) == 0) userInput++;
                    return false;
                });
                watcher.Install();
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

        if (error is not null && userInput > 0)
            throw new UserInterferenceException(userInput, error.SourceException);
        error?.Throw();
    }

    /// <summary>The test failed while the real mouse was being used: the result is not meaningful.</summary>
    public sealed class UserInterferenceException(int events, Exception inner) : Exception(
        $"{events} vrai(s) mouvement(s) de souris pendant le test : relancez-le sans toucher la souris. " +
        $"Échec d'origine : {inner.Message}", inner);

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

    // Keyboard flavour of INPUT (the app's Native.INPUT only declares the mouse one). The padding brings it to the
    // size of the whole union, which SendInput checks.
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public uint type;
        public KEYBDINPUT ki;
        private ulong _padding;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, KeyboardInput[] pInputs, int cbSize);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// Unassigned virtual-key code: delivered to the foreground window like any key, but no app reacts to it.
    /// </summary>
    public const ushort HarmlessKey = 0xE8;

    /// <summary>Injects a press and release of <see cref="HarmlessKey"/>, with the given dwExtraInfo.</summary>
    public static void InjectHarmlessKeyPress(uint extraInfo)
    {
        Assert.Equal(Marshal.SizeOf<Native.INPUT>(), Marshal.SizeOf<KeyboardInput>());
        var input = new[]
        {
            new KeyboardInput { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = HarmlessKey, dwExtraInfo = extraInfo } },
            new KeyboardInput { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = HarmlessKey, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = extraInfo } },
        };
        Assert.Equal(2u, SendInput(2, input, Marshal.SizeOf<KeyboardInput>()));
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
