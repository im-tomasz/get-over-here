using GetOverHere.Tests.Support;

namespace GetOverHere.Tests;

public class SettingsTests
{
    private static void AssertDefaults(Settings s)
    {
        Assert.True(s.Enabled);
        Assert.True(s.RestoreFocus);
        Assert.False(s.DelayFocusReturn);
        Assert.Equal(3000, s.FocusReturnDelayMs);
        Assert.False(s.ZeroMoveMode);
        Assert.True(s.StartWithWindows);
        Assert.False(s.DiagnosticLog);
        Assert.Equal(60, s.RestoreDelayMs);
        Assert.Equal(4000, s.HeldTimeoutMs);
        Assert.Equal(250, s.RawQuietMs);
        Assert.Equal(1500, s.FocusGuardMs);
    }

    [Fact]
    public void NewSettings_HaveTheDocumentedDefaults() => AssertDefaults(new Settings());

    [Fact]
    public void MissingFile_GivesDefaults_AndIsFirstRun()
    {
        using var dir = new TempDirectory();

        var s = Settings.Load(dir.File("absent.json"));

        AssertDefaults(s);
        Assert.True(s.IsFirstRun);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryValue_AndIsNotFirstRun()
    {
        using var dir = new TempDirectory();
        var path = dir.File("settings.json");
        new Settings
        {
            Enabled = false,
            RestoreFocus = false,
            DelayFocusReturn = true,
            FocusReturnDelayMs = 12_000,
            ZeroMoveMode = true,
            StartWithWindows = false,
            DiagnosticLog = true,
            RestoreDelayMs = 90,
            HeldTimeoutMs = 3000,
            RawQuietMs = 400,
            FocusGuardMs = 2500,
        }.Save(path);

        var s = Settings.Load(path);

        Assert.False(s.IsFirstRun);
        Assert.False(s.Enabled);
        Assert.False(s.RestoreFocus);
        Assert.True(s.DelayFocusReturn);
        Assert.Equal(12_000, s.FocusReturnDelayMs);
        Assert.True(s.ZeroMoveMode);
        Assert.False(s.StartWithWindows);
        Assert.True(s.DiagnosticLog);
        Assert.Equal(90, s.RestoreDelayMs);
        Assert.Equal(3000, s.HeldTimeoutMs);
        Assert.Equal(400, s.RawQuietMs);
        Assert.Equal(2500, s.FocusGuardMs);
    }

    [Fact]
    public void IsFirstRun_IsNeverWrittenToDisk()
    {
        using var dir = new TempDirectory();
        var path = dir.File("settings.json");

        Settings.Load(path).Save(path); // IsFirstRun = true in memory

        Assert.DoesNotContain("IsFirstRun", File.ReadAllText(path));
        Assert.False(Settings.Load(path).IsFirstRun);
    }

    [Fact]
    public void PartialFile_KeepsDefaultsForMissingValues()
    {
        // E.g. a file written by an older version that did not know RawQuietMs / FocusGuardMs.
        using var dir = new TempDirectory();
        var path = dir.File("settings.json");
        File.WriteAllText(path, """{ "RestoreDelayMs": 90, "Enabled": false }""");

        var s = Settings.Load(path);

        Assert.Equal(90, s.RestoreDelayMs);
        Assert.False(s.Enabled);
        Assert.True(s.RestoreFocus);
        Assert.Equal(250, s.RawQuietMs);
        Assert.Equal(1500, s.FocusGuardMs);
        Assert.False(s.IsFirstRun);
    }

    [Fact]
    public void UnknownProperties_AreIgnored()
    {
        using var dir = new TempDirectory();
        var path = dir.File("settings.json");
        File.WriteAllText(path, """{ "Enabled": false, "FutureOption": 42 }""");

        Assert.False(Settings.Load(path).Enabled);
    }

    [Theory]
    [InlineData("{ ceci n'est pas du json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{ "RestoreDelayMs": "soixante" }""")]
    public void InvalidFile_GivesDefaults_AndIsFirstRun(string content)
    {
        using var dir = new TempDirectory();
        var path = dir.File("settings.json");
        File.WriteAllText(path, content);

        var s = Settings.Load(path);

        AssertDefaults(s);
        Assert.True(s.IsFirstRun);
    }

    [Fact]
    public void Save_CreatesMissingFolders()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "a", "b", "settings.json");

        new Settings().Save(path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void DefaultLocation_IsInRoamingAppData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GetOverHere", "settings.json");

        Assert.Equal(expected, Settings.DefaultFilePath);
    }
}
