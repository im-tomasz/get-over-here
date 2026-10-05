using Microsoft.Win32;

namespace GetOverHere;

/// <summary>Launch at Windows startup through HKCU\...\Run (no admin rights needed).</summary>
internal sealed class Startup
{
    public const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = AppInfo.Id;

    /// <summary>The app's real startup entry.</summary>
    public static Startup Default { get; } = new(DefaultRunKey, DefaultValueName, () => Environment.ProcessPath!);

    private readonly string _runKey;
    private readonly string _valueName;
    private readonly Func<string> _exePath;

    internal Startup(string runKey, string valueName, Func<string> exePath)
    {
        _runKey = runKey;
        _valueName = valueName;
        _exePath = exePath;
    }

    internal string Command => $"\"{_exePath()}\"";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey);
        return key?.GetValue(_valueName) is string;
    }

    public void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_runKey);
        if (enabled) key.SetValue(_valueName, Command);
        else key.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    /// <summary>If the executable was moved, update the registered path (never creates the entry).</summary>
    public void RefreshPathIfEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey, writable: true);
        if (key?.GetValue(_valueName) is string current && current != Command)
            key.SetValue(_valueName, Command);
    }
}
