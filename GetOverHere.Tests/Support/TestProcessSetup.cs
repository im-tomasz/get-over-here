using System.Runtime.CompilerServices;

namespace GetOverHere.Tests.Support;

internal static class TestProcessSetup
{
    /// <summary>
    /// The test process must handle DPI like the app (PerMonitorV2, see GetOverHere.csproj):
    /// otherwise the hidden windows behind timers / BeginInvoke work in scaled coordinates
    /// (at 125 %, SetCursorPos(960,540) puts the cursor at 1200,675).
    /// </summary>
    [ModuleInitializer]
    internal static void Init() => Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
}
