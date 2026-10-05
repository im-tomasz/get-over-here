using System.Diagnostics;
using System.Drawing.Drawing2D;
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
    private readonly Icon _iconOn = CreateIcon(active: true);
    private readonly Icon _iconOff = CreateIcon(active: false);

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

        _enabledItem = new ToolStripMenuItem("Activé", null, (_, _) => ToggleEnabled()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        _focusItem = new ToolStripMenuItem("Remettre aussi le focus clavier", null, (_, _) => Toggle(s => s.RestoreFocus = !s.RestoreFocus));
        _zeroMoveItem = new ToolStripMenuItem("Mode zéro mouvement (expérimental)", null, (_, _) => Toggle(s => s.ZeroMoveMode = !s.ZeroMoveMode))
        {
            ToolTipText = "Bloque les mouvements de curseur dus au tactile.\nLes applis modernes reçoivent quand même le toucher, certaines vieilles applis non.",
        };
        _startupItem = new ToolStripMenuItem("Lancer au démarrage de Windows", null, (_, _) => ToggleStartup());
        _diagItem = new ToolStripMenuItem("Journal de diagnostic", null, (_, _) => ToggleDiagnostic());

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
            new ToolStripMenuItem("Ouvrir le journal", null, (_, _) => OpenLog()),
            new ToolStripMenuItem("Ouvrir le dossier des réglages", null, (_, _) => Shell(Settings.Folder)),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Quitter", null, (_, _) => ExitThread()),
        });
        menu.Opening += (_, _) => RefreshMenu();

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleEnabled(); };
        RefreshMenu();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        if (_settings.IsFirstRun)
        {
            _tray.ShowBalloonTip(5000, $"{AppInfo.DisplayName} est actif",
                "Touchez la tablette : le curseur revient tout seul. Clic droit sur l'icône pour les options.", ToolTipIcon.Info);
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

    private void RefreshMenu()
    {
        _enabledItem.Checked = _settings.Enabled;
        _focusItem.Checked = _settings.RestoreFocus;
        _zeroMoveItem.Checked = _settings.ZeroMoveMode;
        _startupItem.Checked = Startup.Default.IsEnabled();
        _diagItem.Checked = _settings.DiagnosticLog;

        _tray.Icon = _settings.Enabled ? _iconOn : _iconOff;
        string screen = MonitorInfo.HasSuperDisplay ? "tablette détectée" : "tablette non détectée";
        _tray.Text = $"{AppInfo.DisplayName} — {(_settings.Enabled ? "actif" : "en pause")} ({screen})";
    }

    private static void OpenLog()
    {
        DiagnosticLog.Flush();
        if (File.Exists(DiagnosticLog.FilePath)) Shell(DiagnosticLog.FilePath);
        else MessageBox.Show("Aucun journal pour l'instant. Active d'abord « Journal de diagnostic ».", AppInfo.DisplayName);
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

        _hook.Dispose();
        _rawTouch.Dispose();
        _guard.Dispose();
        _foreground.Dispose();
        _dispatcher.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        DiagnosticLog.Write($"=== {AppInfo.DisplayName} arrêté ===");
        DiagnosticLog.Stop();
        base.ExitThreadCore();
    }

    /// <summary>Icon drawn in code: a tablet outline, a fingertip and a return arrow.</summary>
    private static Icon CreateIcon(bool active)
    {
        int size = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = size / 16f;
            var color = active ? Color.FromArgb(0, 150, 255) : Color.FromArgb(140, 140, 140);

            // screen / tablet
            using var pen = new Pen(color, 1.6f * s);
            g.DrawRectangle(pen, 1.5f * s, 2.5f * s, 13f * s, 9.5f * s);
            // fingertip
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 9f * s, 5f * s, 4f * s, 4f * s);
            // return arrow
            using var arrow = new Pen(active ? Color.White : Color.Gainsboro, 1.4f * s) { EndCap = LineCap.ArrowAnchor };
            g.DrawLine(arrow, 9f * s, 7f * s, 3.5f * s, 7f * s);
        }
        // One icon per state, created once at startup: leaking the HICON is negligible.
        return Icon.FromHandle(bmp.GetHicon());
    }
}
