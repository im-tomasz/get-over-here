using static GetOverHere.Tests.Support.Desktop;

namespace GetOverHere.Tests.Integration;

[Collection(nameof(DesktopCollection))]
[Trait("Category", "Integration")]
public class KeyboardHookIntegrationTests
{
    private const uint Marker = 0x13572500; // recognisable among the user's real keys

    private static KeyboardHook MarkerHook(List<int> messages) => new((int msg, in Native.KBDLLHOOKSTRUCT d) =>
    {
        if (unchecked((uint)d.dwExtraInfo) == Marker) messages.Add(msg);
    });

    [Fact]
    public void Hook_SeesKeyDownThenKeyUp() => RunSta(() =>
    {
        var seen = new List<int>();
        using var hook = MarkerHook(seen);
        Assert.True(hook.Install());

        InjectHarmlessKeyPress(Marker);

        Assert.True(PumpUntil(() => seen.Count >= 2), $"Seulement {seen.Count} événements reçus");
        Assert.Equal(new[] { Native.WM_KEYDOWN, Native.WM_KEYUP }, seen);
    });

    [Fact]
    public void Install_IsIdempotent_AndAfterUninstall_NoMoreCallbacks() => RunSta(() =>
    {
        var seen = new List<int>();
        var hook = MarkerHook(seen);

        Assert.True(hook.Install());
        Assert.True(hook.Install());
        Assert.True(hook.IsInstalled);
        InjectHarmlessKeyPress(Marker);
        Assert.True(PumpUntil(() => seen.Count >= 2));
        PumpFor(50);
        Assert.Equal(2, seen.Count); // a single real hook, not two

        hook.Dispose();
        Assert.False(hook.IsInstalled);
        InjectHarmlessKeyPress(Marker);
        PumpFor(150);
        Assert.Equal(2, seen.Count);
    });

    [Fact]
    public void ExceptionInHandler_IsSwallowed_AndLaterKeysAreStillSeen() => RunSta(() =>
    {
        int calls = 0;
        using var hook = new KeyboardHook((int msg, in Native.KBDLLHOOKSTRUCT d) =>
        {
            if (unchecked((uint)d.dwExtraInfo) != Marker) return;
            calls++;
            throw new InvalidOperationException("boum");
        });
        hook.Install();

        InjectHarmlessKeyPress(Marker);

        Assert.True(PumpUntil(() => calls == 2));
    });
}
