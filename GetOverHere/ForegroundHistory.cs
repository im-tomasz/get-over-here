namespace GetOverHere;

/// <summary>
/// Circular, timestamped history of the windows that came to the foreground.
/// Times are 32-bit GetTickCount values: comparisons tolerate wraparound (~49.7 days).
/// </summary>
internal sealed class ForegroundHistory
{
    public const int Capacity = 16;

    private readonly (int Time, IntPtr Hwnd)[] _entries = new (int, IntPtr)[Capacity];
    private int _count;

    public int Count => Math.Min(_count, Capacity);

    public void Record(int time, IntPtr hwnd)
    {
        _entries[_count % Capacity] = (time, hwnd);
        _count++;
    }

    /// <summary>
    /// Window that was in the foreground just before <paramref name="time"/> (strictly before).
    /// If every entry is later, returns the oldest known entry; if the history is empty, <paramref name="fallback"/>.
    /// </summary>
    public IntPtr ForegroundBefore(int time, IntPtr fallback)
    {
        if (_count == 0) return fallback;

        for (int i = 0; i < Count; i++)
        {
            var (t, hwnd) = _entries[(_count - 1 - i) % Capacity];
            if (unchecked(t - time) < 0) return hwnd;
        }
        return _entries[_count <= Capacity ? 0 : _count % Capacity].Hwnd;
    }
}
