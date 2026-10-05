using System.Runtime.InteropServices;

namespace GetOverHere;

/// <summary>Mouse input injected by the app itself, tagged so that our own hook ignores it.</summary>
internal static class OwnInput
{
    public const uint Signature = 0x474F4800; // "GOH"

    /// <summary>
    /// Puts the cursor back on the exact pixel AND makes it visible. Windows hides the cursor after a touch
    /// until the next mouse input: SetCursorPos alone moves it but leaves it hidden.
    /// An injected mouse move makes it reappear; SetCursorPos guarantees the position whatever the order
    /// in which Windows processes both (they target the same pixel).
    /// </summary>
    public static void MoveCursorVisible(Native.POINT pt)
    {
        SendAbsolute(pt);
        Native.SetCursorPos(pt.X, pt.Y);
    }

    /// <summary>Injected absolute move to an exact pixel (see <see cref="ToAbsolute"/>).</summary>
    internal static void SendAbsolute(Native.POINT pt)
    {
        var desk = VirtualDesktop();
        var (nx, ny) = ToAbsolute(pt, desk.Left, desk.Top, desk.Right - desk.Left, desk.Bottom - desk.Top);
        Send(nx, ny, Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK);
    }

    /// <summary>
    /// Virtual desktop rectangle in physical pixels, computed from the monitors themselves.
    /// Unlike GetSystemMetrics(SM_*VIRTUALSCREEN), the result follows the thread's DPI context rather than
    /// the process's, so it stays correct outside the app too (e.g. tests at 125 % scaling).
    /// </summary>
    internal static Native.RECT VirtualDesktop()
    {
        var desk = new Native.RECT { Left = int.MaxValue, Top = int.MaxValue, Right = int.MinValue, Bottom = int.MinValue };
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, _, _) =>
        {
            var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(hMon, ref mi))
            {
                desk.Left = Math.Min(desk.Left, mi.rcMonitor.Left);
                desk.Top = Math.Min(desk.Top, mi.rcMonitor.Top);
                desk.Right = Math.Max(desk.Right, mi.rcMonitor.Right);
                desk.Bottom = Math.Max(desk.Bottom, mi.rcMonitor.Bottom);
            }
            return true;
        }, IntPtr.Zero);

        if (desk.Left == int.MaxValue) // no monitor enumerated: fallback
        {
            desk.Left = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            desk.Top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            desk.Right = desk.Left + Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            desk.Bottom = desk.Top + Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
        }
        return desk;
    }

    /// <summary>
    /// Converts a virtual-desktop pixel into SendInput absolute coordinates (0..65535 across the virtual desktop).
    /// Windows converts back with pixel = left + floor(n × width / 65536) (measured on the target machine):
    /// we aim at the middle of the range of n that yields exactly that pixel, so there is no error.
    /// Points outside the desktop are clamped to its edge.
    /// </summary>
    internal static (int X, int Y) ToAbsolute(Native.POINT pt, int left, int top, int width, int height) =>
        (Axis(pt.X - left, width), Axis(pt.Y - top, height));

    private static int Axis(int offset, int size)
    {
        size = Math.Max(1, size);
        offset = Math.Clamp(offset, 0, size - 1);
        long n = (2L * offset + 1) * 65536 / (2L * size);
        return (int)Math.Clamp(n, 0, 65535);
    }

    /// <summary>
    /// Real mouse input with no visible effect: makes us "the last process to have received input"
    /// (the right to change the foreground window). A (0,0) move is not enough: Windows does not even deliver it.
    /// So the cursor is shifted by one pixel and put back exactly, with two injected moves that Windows
    /// applies in order.
    /// </summary>
    public static void Nudge()
    {
        Native.GetCursorPos(out var pt);
        var desk = VirtualDesktop();
        var shifted = pt;
        shifted.X = pt.X + 1 < desk.Right ? pt.X + 1 : pt.X - 1;
        SendAbsolute(shifted);
        SendAbsolute(pt);
    }

    private static void Send(int dx, int dy, uint flags)
    {
        var inputs = new[]
        {
            new Native.INPUT
            {
                type = Native.INPUT_MOUSE,
                mi = new Native.MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags, dwExtraInfo = Signature },
            },
        };
        Native.SendInput(1, inputs, Marshal.SizeOf<Native.INPUT>());
    }
}
