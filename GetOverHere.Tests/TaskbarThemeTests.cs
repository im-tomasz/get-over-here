using Microsoft.Win32;

namespace GetOverHere.Tests;

/// <summary>Taskbar theme detection, tested in a throwaway registry key (never the real Personalize key).</summary>
public sealed class TaskbarThemeTests : IDisposable
{
    private readonly string _key = $@"Software\GetOverHere.Tests\{Guid.NewGuid():N}";
    private readonly TaskbarTheme _theme;

    public TaskbarThemeTests() => _theme = new TaskbarTheme(_key, TaskbarTheme.DefaultValueName);

    private void SetValue(object value, RegistryValueKind kind = RegistryValueKind.DWord)
    {
        using var k = Registry.CurrentUser.CreateSubKey(_key);
        k.SetValue(TaskbarTheme.DefaultValueName, value, kind);
    }

    [Fact]
    public void ValueZero_IsDark()
    {
        SetValue(0);
        Assert.True(_theme.IsDark());
    }

    [Fact]
    public void ValueOne_IsLight()
    {
        SetValue(1);
        Assert.False(_theme.IsDark());
    }

    [Fact]
    public void MissingKey_IsDark() => Assert.True(_theme.IsDark()); // older Windows 10: taskbar always dark

    [Fact]
    public void MissingValue_IsDark()
    {
        using (Registry.CurrentUser.CreateSubKey(_key)) { }
        Assert.True(_theme.IsDark());
    }

    [Fact]
    public void UnexpectedValueType_IsDark()
    {
        SetValue("1", RegistryValueKind.String);
        Assert.True(_theme.IsDark());
    }

    [Fact]
    public void ChangesAreSeenImmediately()
    {
        SetValue(1);
        Assert.False(_theme.IsDark());
        SetValue(0);
        Assert.True(_theme.IsDark());
    }

    [Fact]
    public void Default_ReadsTheRealWindowsSetting()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", TaskbarTheme.DefaultKey);
        Assert.Equal("SystemUsesLightTheme", TaskbarTheme.DefaultValueName);
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
}
