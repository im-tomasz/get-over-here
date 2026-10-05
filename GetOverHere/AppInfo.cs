namespace GetOverHere;

/// <summary>Application identity, in one place.</summary>
internal static class AppInfo
{
    /// <summary>Name shown to the user (tray tooltip, notifications, dialogs).</summary>
    public const string DisplayName = "Get Over Here!";

    /// <summary>Technical identifier: settings folder, startup entry, mutex, executable name.</summary>
    public const string Id = "GetOverHere";

    /// <summary>Identifier used by versions released under the previous name, kept for migration.</summary>
    public const string LegacyId = "TouchBack";
}
