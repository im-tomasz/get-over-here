namespace GetOverHere;

/// <summary>
/// One-time upgrade from the app's previous name ("TouchBack"): copies the settings file to the new folder
/// and moves the Windows startup entry to the new executable. Safe to run on every launch.
/// </summary>
internal sealed class LegacyMigration
{
    [Flags]
    public enum Result
    {
        None = 0,
        SettingsCopied = 1,
        StartupMoved = 2,
    }

    private readonly string _legacySettingsPath;
    private readonly string _settingsPath;
    private readonly Startup _legacyStartup;
    private readonly Startup _startup;

    internal LegacyMigration(string legacySettingsPath, string settingsPath, Startup legacyStartup, Startup startup)
    {
        _legacySettingsPath = legacySettingsPath;
        _settingsPath = settingsPath;
        _legacyStartup = legacyStartup;
        _startup = startup;
    }

    /// <summary>Real locations: %APPDATA%\TouchBack and the "TouchBack" value of the HKCU Run key.</summary>
    public static LegacyMigration Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.LegacyId, "settings.json"),
        Settings.DefaultFilePath,
        new Startup(Startup.DefaultRunKey, AppInfo.LegacyId, () => Environment.ProcessPath!),
        Startup.Default);

    public Result Run()
    {
        var result = Result.None;

        // Settings: only when the new app has none yet, so user changes are never overwritten.
        try
        {
            if (!File.Exists(_settingsPath) && File.Exists(_legacySettingsPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.Copy(_legacySettingsPath, _settingsPath);
                result |= Result.SettingsCopied;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable legacy settings: the app simply starts with defaults.
        }

        // Startup: the legacy entry points to an executable that no longer exists; replace it with ours.
        if (_legacyStartup.IsEnabled())
        {
            _legacyStartup.Set(false);
            _startup.Set(true);
            result |= Result.StartupMoved;
        }

        return result;
    }
}
