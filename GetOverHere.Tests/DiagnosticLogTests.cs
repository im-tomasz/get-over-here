using System.Text.RegularExpressions;
using GetOverHere.Tests.Support;
using static GetOverHere.Tests.Support.TouchRig;

namespace GetOverHere.Tests;

/// <summary>DiagnosticLog is static: these tests never run in parallel with the others.</summary>
[CollectionDefinition(nameof(StaticStateCollection), DisableParallelization = true)]
public class StaticStateCollection;

[Collection(nameof(StaticStateCollection))]
public sealed class DiagnosticLogTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly string _originalPath = DiagnosticLog.FilePath;
    private readonly bool _originalEnabled = DiagnosticLog.Enabled;

    public DiagnosticLogTests()
    {
        DiagnosticLog.Discard();
        DiagnosticLog.FilePath = _dir.File("diag.log");
    }

    private string[] Lines() => File.ReadAllLines(DiagnosticLog.FilePath);

    [Fact]
    public void Disabled_WritesNothing()
    {
        DiagnosticLog.Enabled = false;

        DiagnosticLog.Write("secret");
        DiagnosticLog.Flush();

        Assert.False(File.Exists(DiagnosticLog.FilePath));
    }

    [Fact]
    public void Enabled_WritesTimestampedLines_InOrder()
    {
        DiagnosticLog.Enabled = true;

        DiagnosticLog.Write("premier");
        DiagnosticLog.Write("second");
        DiagnosticLog.Flush();

        var lines = Lines();
        Assert.Equal(2, lines.Length);
        Assert.Matches(new Regex(@"^\d{2}:\d{2}:\d{2}\.\d{3}  premier$"), lines[0]);
        Assert.Matches(new Regex(@"^\d{2}:\d{2}:\d{2}\.\d{3}  second$"), lines[1]);
    }

    [Fact]
    public void SuccessiveFlushes_Append()
    {
        DiagnosticLog.Enabled = true;

        DiagnosticLog.Write("a");
        DiagnosticLog.Flush();
        DiagnosticLog.Write("b");
        DiagnosticLog.Flush();

        Assert.Equal(new[] { "a", "b" }, Lines().Select(l => l[14..]));
    }

    [Fact]
    public void FlushWithNothingQueued_DoesNotCreateTheFile()
    {
        DiagnosticLog.Enabled = true;

        DiagnosticLog.Flush();

        Assert.False(File.Exists(DiagnosticLog.FilePath));
    }

    [Fact]
    public void LockedFile_DoesNotThrow()
    {
        DiagnosticLog.Enabled = true;
        using (File.Open(DiagnosticLog.FilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            DiagnosticLog.Write("perdu");
            var ex = Record.Exception(DiagnosticLog.Flush);
            Assert.Null(ex);
        }
    }

    [Fact]
    public void ConcurrentWrites_AreAllKept()
    {
        DiagnosticLog.Enabled = true;

        Parallel.For(0, 1000, i => DiagnosticLog.Write($"ligne {i}"));
        DiagnosticLog.Flush();

        var payloads = Lines().Select(l => l[14..]).ToHashSet();
        Assert.Equal(1000, payloads.Count);
        Assert.All(Enumerable.Range(0, 1000), i => Assert.Contains($"ligne {i}", payloads));
    }

    [Fact]
    public void FullTouchScenario_WithLoggingEnabled_LogsTheKeySteps()
    {
        // Covers all the log formatting code (no exception tolerated) and the content useful for diagnosis.
        DiagnosticLog.Enabled = true;
        using (var rig = new TouchRig())
        {
            rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
            rig.RealMove(3, 0);
            rig.Pump();
            rig.Advance(2000);
        }
        DiagnosticLog.Flush();

        var text = File.ReadAllText(DiagnosticLog.FilePath);
        Assert.Contains("début toucher (lecture directe)", text);
        Assert.Contains("L_DOWN", text);
        Assert.Contains("Touch", text);
        Assert.Contains("retour (souris utilisée", text);
        Assert.Contains("PREMIER PLAN", text);
        Assert.Contains("fenêtre 0xB0 -> fenêtre 0xA0 OK", text);
    }

    public void Dispose()
    {
        DiagnosticLog.Discard();
        DiagnosticLog.Enabled = _originalEnabled;
        DiagnosticLog.FilePath = _originalPath;
        _dir.Dispose();
    }
}
