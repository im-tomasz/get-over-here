namespace GetOverHere.Tests;

public class MonitorInfoTests
{
    [Theory]
    [InlineData("SuperDisplay Virtual Adapter")] // actual value on the user's machine
    [InlineData("superdisplay virtual adapter")]
    [InlineData("SUPERDISPLAY")]
    [InlineData("Foo SuperDisplay Bar")]
    public void SuperDisplayAdapter_IsRecognised(string description) =>
        Assert.True(MonitorInfo.IsSuperDisplayAdapter(description));

    [Theory]
    [InlineData("AMD Radeon(TM) Graphics")]   // actual main screen
    [InlineData("NVIDIA GeForce RTX 3060")]
    [InlineData("Microsoft Basic Display Adapter")]
    [InlineData("Super Display")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherAdapters_AreNotRecognised(string? description) =>
        Assert.False(MonitorInfo.IsSuperDisplayAdapter(description));

    [Fact]
    public void WhenSuperDisplayIsKnown_OnlyItsMonitorsAreTouchMonitors()
    {
        var super = new HashSet<IntPtr> { 0x7 };

        Assert.True(MonitorInfo.IsTouchMonitor(super, 0x7));
        Assert.False(MonitorInfo.IsTouchMonitor(super, 0x1));
        Assert.False(MonitorInfo.IsTouchMonitor(super, IntPtr.Zero)); // point outside every monitor
    }

    [Fact]
    public void WhenNoSuperDisplayMonitorIsFound_EveryPointIsAccepted()
    {
        // The PC has no other touch screen: with no identified tablet, every touch comes from it.
        var none = new HashSet<IntPtr>();

        Assert.True(MonitorInfo.IsTouchMonitor(none, 0x1));
        Assert.True(MonitorInfo.IsTouchMonitor(none, IntPtr.Zero));
    }
}
