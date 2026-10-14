namespace GetOverHere;

/// <summary>
/// Finds the monitor(s) driven by the SuperDisplay virtual adapter.
/// Data is replaced as a whole (immutable snapshot) so the hook can read it without locking.
/// </summary>
internal static class MonitorInfo
{
    private sealed record Snapshot(Dictionary<IntPtr, string> Names, HashSet<IntPtr> SuperDisplay);

    private static volatile Snapshot _snapshot = new(new(), new());
    private static long _lastRefreshTicks;

    public const string AdapterKeyword = "SuperDisplay";

    /// <summary>true when at least one SuperDisplay monitor was found.</summary>
    public static bool HasSuperDisplay => _snapshot.SuperDisplay.Count > 0;

    public static void Refresh()
    {
        _lastRefreshTicks = Environment.TickCount64;

        // Device name (\\.\DISPLAY3) -> adapter description ("SuperDisplay Virtual Adapter").
        var adapters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dd = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
        for (uint i = 0; Native.EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            adapters[dd.DeviceName] = dd.DeviceString;
            dd.cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>();
        }

        var names = new Dictionary<IntPtr, string>();
        var super = new HashSet<IntPtr>();
        var lines = new List<string>();

        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, _, _) =>
        {
            var mi = new Native.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (!Native.GetMonitorInfo(hMon, ref mi)) return true;

            adapters.TryGetValue(mi.szDevice, out var adapter);
            bool isSuper = IsSuperDisplayAdapter(adapter);
            bool primary = (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;

            names[hMon] = isSuper ? $"{mi.szDevice} (SuperDisplay)" : mi.szDevice;
            if (isSuper) super.Add(hMon);
            lines.Add($"  {mi.szDevice} {mi.rcMonitor} {(primary ? "principal " : "")}adaptateur=\"{adapter}\"{(isSuper ? "  <= SUPERDISPLAY" : "")}");
            return true;
        }, IntPtr.Zero);

        _snapshot = new Snapshot(names, super);
        DiagnosticLog.Write("Écrans détectés :" + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    internal static bool IsSuperDisplayAdapter(string? adapterDescription) =>
        adapterDescription?.Contains(AdapterKeyword, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Decision rule: with no known SuperDisplay monitor, everything is accepted (the PC has no other touch screen).</summary>
    internal static bool IsTouchMonitor(IReadOnlySet<IntPtr> superDisplayMonitors, IntPtr hMonitor) =>
        superDisplayMonitors.Count == 0 || superDisplayMonitors.Contains(hMonitor);

    /// <summary>Is the point on the tablet? (If no SuperDisplay monitor is found, accept: the PC has no other touch screen.)</summary>
    public static bool IsOnTouchMonitor(Native.POINT pt) =>
        IsKnownTouchMonitor(Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONULL));

    /// <summary>Is the window (mostly) on the tablet? Same fallback as <see cref="IsOnTouchMonitor"/>.</summary>
    public static bool IsWindowOnTouchMonitor(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && IsKnownTouchMonitor(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST));

    private static bool IsKnownTouchMonitor(IntPtr hMon)
    {
        var snap = _snapshot;
        if (hMon != IntPtr.Zero && !snap.Names.ContainsKey(hMon) && Environment.TickCount64 - _lastRefreshTicks > 2000)
        {
            Refresh(); // monitor layout changed without notification
            snap = _snapshot;
        }
        return IsTouchMonitor(snap.SuperDisplay, hMon);
    }

    public static string NameAt(Native.POINT pt)
    {
        var hMon = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONULL);
        return _snapshot.Names.TryGetValue(hMon, out var n) ? n : $"0x{hMon:X}";
    }
}
