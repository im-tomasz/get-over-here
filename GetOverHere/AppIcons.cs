namespace GetOverHere;

/// <summary>
/// The app icons, embedded in the executable. They are generated from the pixel-art source by
/// Assets/make-icons.ps1; each .ico holds every size from 16 to 256 px.
/// Dark variants (white harpoon) are meant for a dark taskbar, light ones (navy harpoon) for a light taskbar.
/// </summary>
internal static class AppIcons
{
    internal const string ActiveResource = "GetOverHere.Assets.GetOverHere.ico";
    internal const string PausedResource = "GetOverHere.Assets.GetOverHere-paused.ico";
    internal const string DarkActiveResource = "GetOverHere.Assets.GetOverHere-dark.ico";
    internal const string DarkPausedResource = "GetOverHere.Assets.GetOverHere-dark-paused.ico";

    internal static string ResourceName(bool active, bool dark) => (active, dark) switch
    {
        (true, false) => ActiveResource,
        (false, false) => PausedResource,
        (true, true) => DarkActiveResource,
        (false, true) => DarkPausedResource,
    };

    /// <summary>Loads the icon variant closest to <paramref name="size"/> (e.g. the notification-area size).</summary>
    public static Icon Load(bool active, bool dark, Size size)
    {
        string name = ResourceName(active, dark);
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Missing embedded icon resource '{name}'.");
        return new Icon(stream, size);
    }
}
