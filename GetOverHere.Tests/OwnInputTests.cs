using static GetOverHere.Tests.Support.TouchRig;

namespace GetOverHere.Tests;

/// <summary>
/// Pixel → SendInput absolute coordinates conversion (virtual desktop, 0..65535).
/// Windows' inverse formula, measured on the target machine: pixel = left + floor(n × width / 65536).
/// </summary>
public class OwnInputTests
{
    // The user's actual virtual desktop: 2560x1600 tablet on the left (negative X), 1920x1080 screen on the right.
    private const int Left = -2560, Top = -266, Width = 4480, Height = 1600;

    private static int WindowsPixel(int n, int origin, int size) => origin + (int)((long)n * size / 65536);

    [Fact]
    public void EveryColumn_RoundTripsExactly()
    {
        for (int x = Left; x < Left + Width; x++)
        {
            var (nx, _) = OwnInput.ToAbsolute(P(x, 0), Left, Top, Width, Height);
            Assert.True(WindowsPixel(nx, Left, Width) == x, $"x={x} -> n={nx} -> {WindowsPixel(nx, Left, Width)}");
        }
    }

    [Fact]
    public void EveryRow_RoundTripsExactly()
    {
        for (int y = Top; y < Top + Height; y++)
        {
            var (_, ny) = OwnInput.ToAbsolute(P(0, y), Left, Top, Width, Height);
            Assert.True(WindowsPixel(ny, Top, Height) == y, $"y={y} -> n={ny} -> {WindowsPixel(ny, Top, Height)}");
        }
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1536, 864)]
    [InlineData(2560, 1600)]
    [InlineData(7680, 4320)]
    [InlineData(3, 2)]
    public void RoundTripIsExact_ForOtherDesktopSizes(int width, int height)
    {
        foreach (int x in new[] { 0, 1, width / 3, width / 2, width - 2, width - 1 }.Where(v => v >= 0))
        {
            var (nx, _) = OwnInput.ToAbsolute(P(x, 0), 0, 0, width, height);
            Assert.Equal(x, WindowsPixel(nx, 0, width));
        }
    }

    [Fact]
    public void Mapping_IsStrictlyIncreasing()
    {
        int previous = -1;
        for (int x = Left; x < Left + Width; x++)
        {
            var (nx, _) = OwnInput.ToAbsolute(P(x, 0), Left, Top, Width, Height);
            Assert.True(nx > previous, $"Non strictement croissant en x={x}");
            previous = nx;
        }
    }

    [Fact]
    public void ResultsStayInTheValidRange()
    {
        foreach (var pt in new[] { P(Left, Top), P(Left + Width - 1, Top + Height - 1), P(0, 0) })
        {
            var (nx, ny) = OwnInput.ToAbsolute(pt, Left, Top, Width, Height);
            Assert.InRange(nx, 0, 65535);
            Assert.InRange(ny, 0, 65535);
        }
    }

    [Fact]
    public void PointsOutsideTheDesktop_AreClampedToTheEdgePixels()
    {
        var (nx0, ny0) = OwnInput.ToAbsolute(P(Left - 500, Top - 500), Left, Top, Width, Height);
        var (nx1, ny1) = OwnInput.ToAbsolute(P(Left + Width + 500, Top + Height + 500), Left, Top, Width, Height);

        Assert.Equal(Left, WindowsPixel(nx0, Left, Width));
        Assert.Equal(Top, WindowsPixel(ny0, Top, Height));
        Assert.Equal(Left + Width - 1, WindowsPixel(nx1, Left, Width));
        Assert.Equal(Top + Height - 1, WindowsPixel(ny1, Top, Height));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-5, -5)]
    public void DegenerateDesktopSize_DoesNotThrowOrOverflow(int width, int height)
    {
        var (nx, ny) = OwnInput.ToAbsolute(P(0, 0), 0, 0, width, height);

        Assert.InRange(nx, 0, 65535);
        Assert.InRange(ny, 0, 65535);
    }

    [Theory]
    // Actual measurements on the user's machine (n sent -> pixel obtained).
    [InlineData(0, -2560)]
    [InlineData(14, -2560)]
    [InlineData(15, -2559)]
    [InlineData(30, -2558)]
    [InlineData(32768, -320)]
    [InlineData(43000, 379)]
    [InlineData(65535, 1919)]
    public void InverseFormula_MatchesMeasurementsOnTheTargetMachine(int n, int expectedPixel) =>
        Assert.Equal(expectedPixel, WindowsPixel(n, Left, Width));
}
