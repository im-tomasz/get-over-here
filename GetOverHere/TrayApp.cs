using System.Diagnostics;
using Microsoft.Win32;

namespace GetOverHere;

/// <summary>Windowless application: a notification-area icon and its context menu.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly Settings _settings;
    private readonly TouchGuard _guard;
    private readonly MouseHook _hook;
    private readonly ForegroundWatcher _foreground;
    private readonly RawTouchInput _rawTouch;
    private readonly ControlDispatcher _dispatcher;
    private readonly NotifyIcon _tray;
    private Icon _iconOn = null!;
    private Icon _iconOff = null!;
    private bool? _iconsDark; // theme the current icons were loaded for

    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _focusItem;
    private readonly ToolStripMenuItem _zeroMoveItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _diagItem;

    public TrayApp()
    {
        // Must run before loading settings: brings over the settings and startup entry of the "TouchBack" era.
        var migration = LegacyMigration.Default.Run();

        _settings = Settings.Load();
        LoadIconsForTheme();

        DiagnosticLog.Enabled = _settings.DiagnosticLog;
        DiagnosticLog.Start();
        DiagnosticLog.Write($"=== {AppInfo.DisplayName} démarré ({Environment.ProcessPath}) ===");
        if (migration != LegacyMigration.Result.None) DiagnosticLog.Write($"Migration depuis {AppInfo.LegacyId} : {migration}");

        MonitorInfo.Refresh();

        if (_settings.IsFirstRun)
        {
#if !DEBUG
            Startup.Default.Set(_settings.StartWithWindows);
#endif
            _settings.Save();
        }
        else if (_settings.StartWithWindows)
        {
            Startup.Default.RefreshPathIfEnabled();
        }

        _foreground = new ForegroundWatcher();
        _dispatcher = new ControlDispatcher();
        _guard = new TouchGuard(_settings, _foreground.History, new Win32TouchPlatform(), _dispatcher, new FormsOneShotTimer());
        _foreground.Changed += _guard.OnForegroundChanged;
        _hook = new MouseHook(_guard.OnMouseEvent);
        _hook.Install();
        _rawTouch = new RawTouchInput(_guard.OnRawTouch);

        _enabledItem = new ToolStripMenuItem("Enabled", null, (_, _) => ToggleEnabled()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        _focusItem = new ToolStripMenuItem("Also restore keyboard focus", null, (_, _) => Toggle(s => s.RestoreFocus = !s.RestoreFocus));
        _zeroMoveItem = new ToolStripMenuItem("Zero-move mode (experimental)", null, (_, _) => Toggle(s => s.ZeroMoveMode = !s.ZeroMoveMode))
        {
            ToolTipText = "Blocks cursor moves caused by touch.\nModern apps still receive the touch, some older apps may not.",
        };
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup());
        _diagItem = new ToolStripMenuItem("Diagnostic log", null, (_, _) => ToggleDiagnostic());

        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        menu.Items.AddRange(new ToolStripItem[]
        {
            _enabledItem,
            new ToolStripSeparator(),
            _focusItem,
            _zeroMoveItem,
            _startupItem,
            new ToolStripSeparator(),
            _diagItem,
            new ToolStripMenuItem("Open log", null, (_, _) => OpenLog()),
            new ToolStripMenuItem("Open settings folder", null, (_, _) => Shell(Settings.Folder)),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Quit", null, (_, _) => ExitThread()),
        });
        menu.Opening += (_, _) => RefreshMenu();

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleEnabled(); };
        RefreshMenu();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        if (_settings.IsFirstRun)
        {
            _tray.ShowBalloonTip(5000, $"{AppInfo.DisplayName} is running",
                "Tap the tablet: the cursor comes back on its own. Right-click the icon for options.", ToolTipIcon.Info);
        }
    }

    private void ToggleEnabled()
    {
        _settings.Enabled = !_settings.Enabled;
        if (!_settings.Enabled) _guard.Reset();
        SaveAndRefresh();
    }

    private void Toggle(Action<Settings> change)
    {
        change(_settings);
        SaveAndRefresh();
    }

    private void ToggleStartup()
    {
        _settings.StartWithWindows = !Startup.Default.IsEnabled();
        Startup.Default.Set(_settings.StartWithWindows);
        SaveAndRefresh();
    }

    private void ToggleDiagnostic()
    {
        _settings.DiagnosticLog = !_settings.DiagnosticLog;
        DiagnosticLog.Enabled = _settings.DiagnosticLog;
        if (_settings.DiagnosticLog)
        {
            DiagnosticLog.Write("=== Journal de diagnostic activé ===");
            MonitorInfo.Refresh();
        }
        SaveAndRefresh();
    }

    private void SaveAndRefresh()
    {
        _settings.Save();
        RefreshMenu();
    }

    /// <summary>
    /// Loads the icon pair matching the taskbar theme: white harpoon on a dark taskbar, navy harpoon on a light one.
    /// Returns true when the theme changed since the last load.
    /// </summary>
    private bool LoadIconsForTheme()
    {
        bool dark = TaskbarTheme.Default.IsDark();
        if (_iconsDark == dark) return false;

        var (oldOn, oldOff) = (_iconOn, _iconOff);
        _iconOn = AppIcons.Load(active: true, dark, SystemInformation.SmallIconSize);
        _iconOff = AppIcons.Load(active: false, dark, SystemInformation.SmallIconSize);
        _iconsDark = dark;
        oldOn?.Dispose();
        oldOff?.Dispose();
        return true;
    }

    /// <summary>Switching Windows between light and dark mode raises a "General" preference change.</summary>
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && LoadIconsForTheme())
        {
            DiagnosticLog.Write($"Thème de la barre des tâches : {(_iconsDark == true ? "sombre" : "clair")}");
            RefreshMenu();
        }
    }

    private void RefreshMenu()
    {
        _enabledItem.Checked = _settings.Enabled;
        _focusItem.Checked = _settings.RestoreFocus;
        _zeroMoveItem.Checked = _settings.ZeroMoveMode;
        _startupItem.Checked = Startup.Default.IsEnabled();
        _diagItem.Checked = _settings.DiagnosticLog;

        _tray.Icon = _settings.Enabled ? _iconOn : _iconOff;
        string screen = MonitorInfo.HasSuperDisplay ? "tablet detected" : "tablet not detected";
        _tray.Text = $"{AppInfo.DisplayName} — {(_settings.Enabled ? "active" : "paused")} ({screen})";
    }

    private static void OpenLog()
    {
        DiagnosticLog.Flush();
        if (File.Exists(DiagnosticLog.FilePath)) Shell(DiagnosticLog.FilePath);
        else MessageBox.Show("No log yet. Turn on \"Diagnostic log\" first.", AppInfo.DisplayName);
    }

    private static void Shell(string path)
    {
        Directory.CreateDirectory(Settings.Folder);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        MonitorInfo.Refresh();
        RefreshMenu();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) ReinstallHook("sortie de veille");
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock) ReinstallHook("déverrouillage");
    }

    /// <summary>Windows silently drops a low-level hook that timed out (sleep, long freeze): install it again.</summary>
    private void ReinstallHook(string reason)
    {
        _guard.Reset();
        _hook.Reinstall();
        MonitorInfo.Refresh();
        DiagnosticLog.Write($"Hook réinstallé ({reason})");
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        _hook.Dispose();
        _rawTouch.Dispose();
        _guard.Dispose();
        _foreground.Dispose();
        _dispatcher.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _iconOn.Dispose();
        _iconOff.Dispose();
        DiagnosticLog.Write($"=== {AppInfo.DisplayName} arrêté ===");
        DiagnosticLog.Stop();
        base.ExitThreadCore();
    }
}
