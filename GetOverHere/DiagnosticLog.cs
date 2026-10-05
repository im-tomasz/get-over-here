using System.Collections.Concurrent;
using System.Text;

namespace GetOverHere;

/// <summary>
/// Optional diagnostic log. Write() only enqueues in memory; disk writes happen on a separate timer
/// so the mouse hook is never slowed down.
/// </summary>
internal static class DiagnosticLog
{
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly object FlushLock = new();
    private static System.Threading.Timer? _flushTimer;

    public static volatile bool Enabled;

    /// <summary>Log file (settable for tests).</summary>
    public static string FilePath { get; set; } = Path.Combine(Settings.Folder, "diag.log");

    public static void Start()
    {
        _flushTimer ??= new System.Threading.Timer(_ => Flush(), null, 500, 500);
    }

    public static void Write(string message)
    {
        if (!Enabled) return;
        Queue.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {message}");
    }

    public static void Flush()
    {
        lock (FlushLock)
        {
            if (Queue.IsEmpty) return;
            var sb = new StringBuilder();
            while (Queue.TryDequeue(out var line)) sb.AppendLine(line);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, sb.ToString());
            }
            catch (IOException) { /* file locked elsewhere: these lines are lost, it is only a log */ }
        }
    }

    /// <summary>Empties the queue without writing anything.</summary>
    internal static void Discard()
    {
        while (Queue.TryDequeue(out _)) { }
    }

    public static void Stop()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
        Flush();
    }
}
