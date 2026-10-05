using Microsoft.Win32;

namespace GetOverHere;

/// <summary>
/// Tells whether the taskbar (and therefore the notification area) uses the dark theme.
/// Windows stores it in HKCU\...\Themes\Personalize\SystemUsesLightTheme: 0 = dark, 1 = light.
/// When the value is missing (older Windows 10 builds, where the taskbar was always dark), dark is assumed.
/// </summary>
internal sealed class TaskbarTheme
{
    public const string DefaultKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string DefaultValueName = "SystemUsesLightTheme";

    /// <summary>The real Windows setting.</summary>
    public static TaskbarTheme Default { get; } = new(DefaultKey, DefaultValueName);

    private readonly string _key;
    private readonly string _valueName;

    internal TaskbarTheme(string key, string valueName)
    {
        _key = key;
        _valueName = valueName;
    }

    public bool IsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_key);
        return key?.GetValue(_valueName) is not int light || light == 0;
    }
}
