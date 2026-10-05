namespace GetOverHere.Tests;

public class ForegroundHistoryTests
{
    private static readonly IntPtr A = 0xA, B = 0xB, C = 0xC, Fallback = 0xF;

    [Fact]
    public void Empty_ReturnsFallback()
    {
        var h = new ForegroundHistory();

        Assert.Equal(0, h.Count);
        Assert.Equal(Fallback, h.ForegroundBefore(1000, Fallback));
    }

    [Fact]
    public void ReturnsTheLatestEntryStrictlyBeforeTheGivenTime()
    {
        var h = new ForegroundHistory();
        h.Record(100, A);
        h.Record(200, B);
        h.Record(300, C);

        Assert.Equal(A, h.ForegroundBefore(150, Fallback));
        Assert.Equal(A, h.ForegroundBefore(200, Fallback)); // strictly before: B (at 200) excluded
        Assert.Equal(B, h.ForegroundBefore(201, Fallback));
        Assert.Equal(C, h.ForegroundBefore(10_000, Fallback));
    }

    [Fact]
    public void WhenAllEntriesAreLater_ReturnsTheOldestKnown()
    {
        var h = new ForegroundHistory();
        h.Record(500, A);
        h.Record(600, B);

        Assert.Equal(A, h.ForegroundBefore(100, Fallback));
        Assert.Equal(A, h.ForegroundBefore(500, Fallback));
    }

    [Fact]
    public void KeepsOnlyTheLastCapacityEntries()
    {
        var h = new ForegroundHistory();
        int total = ForegroundHistory.Capacity + 4;
        for (int i = 1; i <= total; i++) h.Record(i * 10, i);

        Assert.Equal(ForegroundHistory.Capacity, h.Count);
        // The first 4 entries are overwritten: the oldest known one is #5.
        Assert.Equal((IntPtr)5, h.ForegroundBefore(1, Fallback));
        Assert.Equal((IntPtr)7, h.ForegroundBefore(75, Fallback));
        Assert.Equal((IntPtr)total, h.ForegroundBefore(int.MaxValue, Fallback));
    }

    [Fact]
    public void ExactlyAtCapacity_OldestIsTheFirstRecorded()
    {
        var h = new ForegroundHistory();
        for (int i = 1; i <= ForegroundHistory.Capacity; i++) h.Record(i * 10, i);

        Assert.Equal((IntPtr)1, h.ForegroundBefore(0, Fallback));
    }

    [Fact]
    public void HandlesTickCountWraparound()
    {
        var h = new ForegroundHistory();
        h.Record(int.MaxValue - 10, A);
        h.Record(unchecked(int.MaxValue + 10), B); // = int.MinValue + 9, right after wraparound

        Assert.Equal(A, h.ForegroundBefore(unchecked(int.MaxValue + 5), Fallback));
        Assert.Equal(B, h.ForegroundBefore(unchecked(int.MaxValue + 20), Fallback));
        Assert.Equal(A, h.ForegroundBefore(int.MaxValue - 5, Fallback));
    }

    [Fact]
    public void SameWindowRecordedTwice_IsHarmless()
    {
        var h = new ForegroundHistory();
        h.Record(100, A);
        h.Record(200, A);
        h.Record(300, B);

        Assert.Equal(A, h.ForegroundBefore(250, Fallback));
    }
}
