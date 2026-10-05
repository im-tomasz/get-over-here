namespace GetOverHere.Tests;

/// <summary>Telling where a mouse event comes from, using dwExtraInfo.</summary>
public class ClassificationTests
{
    [Theory]
    // Values actually observed in the user's log (SuperDisplay, finger).
    [InlineData(0xFF515799u)]
    [InlineData(0xFF51578Au)]
    [InlineData(0xFF515788u)]
    // Bounds of the "touch" range (bit 0x80 set).
    [InlineData(0xFF515780u)]
    [InlineData(0xFF5157FFu)]
    public void TouchSignature_IsTouch(uint extra) =>
        Assert.Equal(TouchGuard.Source.Touch, TouchGuard.Classify(extra));

    [Theory]
    [InlineData(0xFF515700u)]
    [InlineData(0xFF515701u)]
    [InlineData(0xFF51577Fu)]
    public void PointerSignatureWithoutTouchBit_IsPen(uint extra) =>
        Assert.Equal(TouchGuard.Source.Pen, TouchGuard.Classify(extra));

    [Theory]
    [InlineData(0x00000000u)]   // real mouse / touchpad (observed in the log)
    [InlineData(0xFF515600u)]   // neighbouring signature
    [InlineData(0xFF525780u)]
    [InlineData(0x7F515780u)]
    [InlineData(0x00515780u)]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0x12345678u)]   // arbitrary value from another program
    public void AnythingElse_IsRealMouse(uint extra) =>
        Assert.Equal(TouchGuard.Source.Mouse, TouchGuard.Classify(extra));

    [Fact]
    public void OwnSignature_IsOwn() =>
        Assert.Equal(TouchGuard.Source.Own, TouchGuard.Classify(OwnInput.Signature));

    [Fact]
    public void OwnSignature_CannotBeConfusedWithAPointerSignature() =>
        Assert.NotEqual(TouchGuard.PointerSignature, OwnInput.Signature & TouchGuard.PointerSignatureMask);

    [Fact]
    public void OnlyTheLow32BitsAreConsidered()
    {
        // On x64, dwExtraInfo is 64-bit: stray high bits must not change the classification.
        Assert.Equal(TouchGuard.Source.Touch, TouchGuard.Classify(unchecked((nuint)0xFFFFFFFF_FF515785UL)));
        Assert.Equal(TouchGuard.Source.Pen, TouchGuard.Classify(unchecked((nuint)0x00000001_FF515705UL)));
        Assert.Equal(TouchGuard.Source.Own, TouchGuard.Classify(unchecked((nuint)0xABCDEF00_00000000UL | OwnInput.Signature)));
    }
}
