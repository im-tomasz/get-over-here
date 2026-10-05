using System.Xml.Linq;

namespace GetOverHere.Tests;

/// <summary>Project settings the app relies on (physical pixel coordinates).</summary>
public class ProjectConfigurationTests
{
    private static XDocument Csproj()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GetOverHere", "GetOverHere.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir!.FullName, "GetOverHere", "GetOverHere.csproj"));
    }

    private static string? Property(string name) =>
        Csproj().Descendants(name).Select(e => e.Value.Trim()).FirstOrDefault();

    [Fact]
    public void App_IsPerMonitorV2DpiAware()
    {
        // Without this, with a Windows scale factor ≠ 100 %, every cursor position is wrong.
        Assert.Equal("PerMonitorV2", Property("ApplicationHighDpiMode"));
    }

    [Fact]
    public void App_IsAWindowlessWinFormsExe()
    {
        Assert.Equal("WinExe", Property("OutputType"));   // no console window at startup
        Assert.Equal("true", Property("UseWindowsForms"));
    }

    [Fact]
    public void TestProcess_RunsWithTheSameDpiMode() =>
        Assert.Equal(HighDpiMode.PerMonitorV2, Application.HighDpiMode);
}
