using Microsoft.Win32;

namespace GetOverHere.Tests;

/// <summary>Launch at startup, tested in a throwaway registry key (never the real Run key).</summary>
public sealed class StartupTests : IDisposable
{
    private readonly string _key = $@"Software\GetOverHere.Tests\{Guid.NewGuid():N}";
    private string _exe = @"C:\Apps\GetOverHere\GetOverHere.exe";
    private readonly Startup _startup;

    public StartupTests() => _startup = new Startup(_key, AppInfo.Id, () => _exe);

    private object? RawValue()
    {
        using var k = Registry.CurrentUser.OpenSubKey(_key);
        return k?.GetValue(AppInfo.Id);
    }

    [Fact]
    public void InitiallyDisabled() => Assert.False(_startup.IsEnabled());

    [Fact]
    public void Enable_WritesTheQuotedExePath()
    {
        _startup.Set(true);

        Assert.True(_startup.IsEnabled());
        Assert.Equal("\"C:\\Apps\\GetOverHere\\GetOverHere.exe\"", RawValue());
    }

    [Fact]
    public void PathWithSpaces_IsQuoted()
    {
        _exe = @"C:\Program Files\Get Over Here\GetOverHere.exe";
        _startup.Set(true);

        Assert.Equal("\"C:\\Program Files\\Get Over Here\\GetOverHere.exe\"", RawValue());
    }

    [Fact]
    public void Disable_RemovesTheEntry()
    {
        _startup.Set(true);
        _startup.Set(false);

        Assert.False(_startup.IsEnabled());
        Assert.Null(RawValue());
    }

    [Fact]
    public void DisableWhenAbsent_DoesNotThrow()
    {
        _startup.Set(false);
        _startup.Set(false);

        Assert.False(_startup.IsEnabled());
    }

    [Fact]
    public void RefreshPath_UpdatesAStalePath()
    {
        _startup.Set(true);
        _exe = @"D:\New\GetOverHere.exe";

        _startup.RefreshPathIfEnabled();

        Assert.Equal("\"D:\\New\\GetOverHere.exe\"", RawValue());
    }

    [Fact]
    public void RefreshPath_NeverCreatesTheEntry()
    {
        _startup.RefreshPathIfEnabled();

        Assert.False(_startup.IsEnabled());
    }

    [Fact]
    public void Default_TargetsTheCurrentUserRunKey()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", Startup.DefaultRunKey);
        Assert.Equal("GetOverHere", Startup.DefaultValueName);
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
}
