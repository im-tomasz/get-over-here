namespace GetOverHere.Tests;

/// <summary>Embedded app icons: present, every size, transparent background, faithful to the pixel art.</summary>
public class AppIconsTests
{
    // Notification-area sizes for 100 %, 125 %, 150 % and 200 % scaling, plus larger shell sizes.
    public static TheoryData<int> Sizes => new() { 16, 20, 24, 32, 40, 48, 64 };

    private static readonly (bool Active, bool Dark)[] Variants = { (true, false), (false, false), (true, true), (false, true) };

    private static Bitmap Frame(bool active, bool dark, int size)
    {
        using var icon = AppIcons.Load(active, dark, new Size(size, size));
        return icon.ToBitmap();
    }

    private static string AssetsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "GetOverHere", "Assets")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "GetOverHere", "Assets");
    }

    private static IEnumerable<Color> Opaque(Bitmap bmp)
    {
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                if (p.A > 0) yield return p;
            }
    }

    /// <summary>The navy outline: darkest colour of the pixel art.</summary>
    private static Color OutlineColor()
    {
        using var art = new Bitmap(Path.Combine(AssetsFolder(), "icon-pixels.png"));
        return Opaque(art).MinBy(p => p.GetBrightness());
    }

    // ───────────── Every variant ─────────────

    [Theory]
    [MemberData(nameof(Sizes))]
    public void EveryVariant_ContainsEveryNotificationAreaSize(int size)
    {
        foreach (var (active, dark) in Variants)
        {
            using var bmp = Frame(active, dark, size);
            Assert.Equal(new Size(size, size), bmp.Size);
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void EveryVariant_HasATransparentBackground(int size)
    {
        foreach (var (active, dark) in Variants)
        {
            using var bmp = Frame(active, dark, size);
            foreach (var (x, y) in new[] { (0, 0), (size - 1, 0), (0, size - 1), (size - 1, size - 1), (size / 2, 0), (size / 2, size - 1) })
                Assert.True(bmp.GetPixel(x, y).A == 0, $"active={active} dark={dark} {size}px: pixel ({x},{y}) is not transparent");
        }
    }

    [Theory]
    [InlineData(true, false, "GetOverHere.ico")]
    [InlineData(false, false, "GetOverHere-paused.ico")]
    [InlineData(true, true, "GetOverHere-dark.ico")]
    [InlineData(false, true, "GetOverHere-dark-paused.ico")]
    public void EmbeddedIcons_AreTheGeneratedFiles(bool active, bool dark, string file)
    {
        using var embedded = typeof(AppIcons).Assembly.GetManifestResourceStream(AppIcons.ResourceName(active, dark))!;
        using var copy = new MemoryStream();
        embedded.CopyTo(copy);

        Assert.Equal(File.ReadAllBytes(Path.Combine(AssetsFolder(), file)), copy.ToArray());
    }

    // ───────────── Light taskbar: navy harpoon ─────────────

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Light_ArtworkIsVisible_AtEverySize(int size)
    {
        using var bmp = Frame(true, false, size);
        var opaque = Opaque(bmp).ToList();

        Assert.InRange(opaque.Count, size * size / 10, size * size / 2); // a harpoon, neither empty nor a filled square
        Assert.Contains(opaque, p => p.R < 60 && p.G < 80 && p.B < 110); // the dark navy outline is there
    }

    [Fact]
    public void Light_Frame32_IsThePixelArtExactly_PlusTheWhiteInterior()
    {
        using var art = new Bitmap(Path.Combine(AssetsFolder(), "icon-pixels.png"));
        using var frame = Frame(true, false, 32);
        int ox = (32 - art.Width) / 2, oy = (32 - art.Height) / 2;

        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var actual = frame.GetPixel(x, y);
                bool inArt = x >= ox && y >= oy && x < ox + art.Width && y < oy + art.Height;
                var expected = inArt ? art.GetPixel(x - ox, y - oy) : Color.Transparent;

                if (expected.A > 0)
                    Assert.True(expected.ToArgb() == actual.ToArgb(), $"({x},{y}): expected {expected}, got {actual}");
                else
                    Assert.True(actual.A == 0 || actual.ToArgb() == Color.White.ToArgb(),
                                $"({x},{y}): an empty cell must be transparent or white (interior), got {actual}");
            }
    }

    [Fact]
    public void Light_Frame32_HasAWhiteInterior()
    {
        using var frame = Frame(true, false, 32);

        int white = Opaque(frame).Count(p => p.ToArgb() == Color.White.ToArgb());
        Assert.True(white >= 10, $"Only {white} white interior pixels");
    }

    // ───────────── Dark taskbar: inverted colours, white harpoon ─────────────

    [Fact]
    public void Dark_Frame32_IsTheLightOneWithInvertedColours()
    {
        using var light = Frame(true, false, 32);
        using var dark = Frame(true, true, 32);
        var outline = OutlineColor();

        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                Color l = light.GetPixel(x, y), d = dark.GetPixel(x, y);
                Assert.True(l.A == d.A, $"({x},{y}): same shape expected, alpha {l.A} vs {d.A}");
                if (l.A == 0) continue;

                if (l.ToArgb() == outline.ToArgb())
                    Assert.True(d.ToArgb() == Color.White.ToArgb(), $"({x},{y}): the navy outline must become white, got {d}");
                else if (l.ToArgb() == Color.White.ToArgb())
                    Assert.True(d.ToArgb() == outline.ToArgb(), $"({x},{y}): the white interior must become navy, got {d}");
                else
                {
                    // Other colours: lightness inverted, hue kept.
                    Assert.InRange(d.GetBrightness(), 1 - l.GetBrightness() - 0.02f, 1 - l.GetBrightness() + 0.02f);
                    if (l.GetSaturation() > 0.1f) Assert.InRange(d.GetHue(), l.GetHue() - 3, l.GetHue() + 3);
                }
            }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Dark_HarpoonIsWhite_AtEverySize(int size)
    {
        using var bmp = Frame(true, true, size);
        var opaque = Opaque(bmp).ToList();

        int bright = opaque.Count(p => p.R > 200 && p.G > 200 && p.B > 200);
        int dark = opaque.Count(p => p.GetBrightness() < 0.3f);
        Assert.True(bright > dark, $"{size}px: {bright} light pixels vs {dark} dark ones: the harpoon should read as white");
    }

    // ───────────── Paused ─────────────

    [Theory]
    [MemberData(nameof(Sizes))]
    public void PausedVariants_AreGreyAndSemiTransparent(int size)
    {
        foreach (bool dark in new[] { false, true })
        {
            using var bmp = Frame(false, dark, size);
            var opaque = Opaque(bmp).ToList();

            Assert.NotEmpty(opaque);
            Assert.All(opaque, p => Assert.True(Math.Abs(p.R - p.G) <= 2 && Math.Abs(p.G - p.B) <= 2, $"dark={dark}: not grey: {p}"));
            Assert.All(opaque, p => Assert.True(p.A < 255, $"dark={dark}: not semi-transparent: {p}"));
        }
    }

    [Fact]
    public void DarkPaused_IsLighterThanLightPaused()
    {
        using var light = Frame(false, false, 32);
        using var dark = Frame(false, true, 32);

        Assert.True(Opaque(dark).Average(p => p.GetBrightness()) > Opaque(light).Average(p => p.GetBrightness()));
    }

    // ───────────── Source ─────────────

    [Fact]
    public void PixelArtSource_HasTheExpectedGrid()
    {
        using var art = new Bitmap(Path.Combine(AssetsFolder(), "icon-pixels.png"));

        Assert.Equal(new Size(32, 13), art.Size);
        Assert.Equal(0, art.GetPixel(0, 0).A); // background removed
    }

    [Fact]
    public void ResourceNames_AreDistinctPerVariant() =>
        Assert.Equal(4, Variants.Select(v => AppIcons.ResourceName(v.Active, v.Dark)).Distinct().Count());
}
