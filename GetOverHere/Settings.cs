using System.Text.Json;
using System.Text.Json.Serialization;

namespace GetOverHere;

/// <summary>User settings, persisted as JSON in %APPDATA%\GetOverHere\settings.json.</summary>
internal sealed class Settings
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Id);

    public static string DefaultFilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Master switch for the whole behaviour.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Also bring back to the foreground the window that had the focus before the touch.</summary>
    public bool RestoreFocus { get; set; } = true;

    /// <summary>
    /// Wait <see cref="FocusReturnDelayMs"/> before giving the focus back, so the touched window can be used
    /// from the keyboard for a moment (the cursor still comes back right away).
    /// </summary>
    public bool DelayFocusReturn { get; set; }

    /// <summary>Cooldown before the focus comes back when <see cref="DelayFocusReturn"/> is on (slider in the menu).</summary>
    public int FocusReturnDelayMs { get; set; } = 3000;

    /// <summary>Experimental: block the mouse moves Windows generates from touch, so the cursor never leaves.</summary>
    public bool ZeroMoveMode { get; set; }

    public bool StartWithWindows { get; set; } = true;

    public bool DiagnosticLog { get; set; }

    /// <summary>Delay after the finger is lifted before the cursor is brought back (lets the click reach the app).</summary>
    public int RestoreDelayMs { get; set; } = 60;

    /// <summary>If the release is never seen (still finger, swallowed gesture), restore after this much silence.</summary>
    public int HeldTimeoutMs { get; set; } = 4000;

    /// <summary>Touch seen only through raw touch-screen reports (no mouse event): the touch ends after this much silence.</summary>
    public int RawQuietMs { get; set; } = 250;

    /// <summary>
    /// For this long after the cursor is restored, if the touched window grabs the foreground late
    /// (the app handles the click afterwards), the focus is still given back to the original window.
    /// </summary>
    public int FocusGuardMs { get; set; } = 1500;

    /// <summary>true when no readable settings file existed (first launch).</summary>
    [JsonIgnore]
    public bool IsFirstRun { get; private set; }

    public static Settings Load(string? path = null)
    {
        path ??= DefaultFilePath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? new Settings { IsFirstRun = true };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings: fall back to defaults.
        }
        return new Settings { IsFirstRun = true };
    }

    public void Save(string? path = null)
    {
        path ??= DefaultFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
