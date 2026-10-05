using GetOverHere.Tests.Support;
using Microsoft.Win32;

namespace GetOverHere.Tests;

/// <summary>Upgrade from the previous name ("TouchBack"), tested with throwaway folders and registry keys.</summary>
public sealed class LegacyMigrationTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly string _runKey = $@"Software\GetOverHere.Tests\{Guid.NewGuid():N}";
    private readonly string _legacySettings;
    private readonly string _settings;
    private readonly Startup _legacyStartup;
    private readonly Startup _startup;
    private readonly LegacyMigration _migration;

    public LegacyMigrationTests()
    {
        _legacySettings = Path.Combine(_dir.Path, "TouchBack", "settings.json");
        _settings = Path.Combine(_dir.Path, "GetOverHere", "settings.json");
        _legacyStartup = new Startup(_runKey, "TouchBack", () => @"C:\Old\TouchBack.exe");
        _startup = new Startup(_runKey, "GetOverHere", () => @"C:\New\GetOverHere.exe");
        _migration = new LegacyMigration(_legacySettings, _settings, _legacyStartup, _startup);
    }

    private void WriteLegacySettings(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_legacySettings)!);
        File.WriteAllText(_legacySettings, json);
    }

    private string? RunValue(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(_runKey);
        return k?.GetValue(name) as string;
    }

    [Fact]
    public void NothingFromThePreviousVersion_NothingHappens()
    {
        Assert.Equal(LegacyMigration.Result.None, _migration.Run());

        Assert.False(File.Exists(_settings));
        Assert.False(_startup.IsEnabled());
    }

    [Fact]
    public void LegacySettings_AreCopied_AndKeepTheUsersValues()
    {
        WriteLegacySettings("""{ "RestoreFocus": false, "RestoreDelayMs": 90 }""");

        Assert.Equal(LegacyMigration.Result.SettingsCopied, _migration.Run());

        var s = Settings.Load(_settings);
        Assert.False(s.IsFirstRun);
        Assert.False(s.RestoreFocus);
        Assert.Equal(90, s.RestoreDelayMs);
        Assert.True(File.Exists(_legacySettings)); // the old file is left untouched
    }

    [Fact]
    public void ExistingNewSettings_AreNeverOverwritten()
    {
        WriteLegacySettings("""{ "Enabled": false }""");
        new Settings { Enabled = true, RestoreDelayMs = 75 }.Save(_settings);

        Assert.Equal(LegacyMigration.Result.None, _migration.Run());

        var s = Settings.Load(_settings);
        Assert.True(s.Enabled);
        Assert.Equal(75, s.RestoreDelayMs);
    }

    [Fact]
    public void LegacyStartupEntry_IsReplacedByOneForTheNewExecutable()
    {
        _legacyStartup.Set(true);

        Assert.Equal(LegacyMigration.Result.StartupMoved, _migration.Run());

        Assert.Null(RunValue("TouchBack"));
        Assert.Equal("\"C:\\New\\GetOverHere.exe\"", RunValue("GetOverHere"));
    }

    [Fact]
    public void UserWhoDisabledStartup_DoesNotGetItBack()
    {
        WriteLegacySettings("""{ "StartWithWindows": false }""");

        _migration.Run();

        Assert.False(_startup.IsEnabled());
    }

    [Fact]
    public void BothAtOnce_AreReportedTogether()
    {
        WriteLegacySettings("{}");
        _legacyStartup.Set(true);

        Assert.Equal(LegacyMigration.Result.SettingsCopied | LegacyMigration.Result.StartupMoved, _migration.Run());
    }

    [Fact]
    public void RunningTwice_IsHarmless()
    {
        WriteLegacySettings("""{ "RestoreDelayMs": 90 }""");
        _legacyStartup.Set(true);
        _migration.Run();
        new Settings { RestoreDelayMs = 120 }.Save(_settings); // user changes something afterwards

        Assert.Equal(LegacyMigration.Result.None, _migration.Run());

        Assert.Equal(120, Settings.Load(_settings).RestoreDelayMs);
        Assert.True(_startup.IsEnabled());
    }

    [Fact]
    public void DefaultMigration_TargetsTheRealLegacyLocations()
    {
        Assert.Equal("TouchBack", AppInfo.LegacyId);
        Assert.Equal("GetOverHere", AppInfo.Id);
        Assert.Equal("Get Over Here!", AppInfo.DisplayName);
        Assert.NotNull(LegacyMigration.Default);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_runKey, throwOnMissingSubKey: false);
        _dir.Dispose();
    }
}
