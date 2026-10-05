using GetOverHere.Tests.Support;
using static GetOverHere.Tests.Support.Desktop;

namespace GetOverHere.Tests.Integration;

[Collection(nameof(DesktopCollection))]
[Trait("Category", "Integration")]
public class MouseHookIntegrationTests
{
    private const uint Marker = 0x13572400; // classified as "mouse", recognisable in the stream

    [Fact]
    public void Hook_SeesInjectedEvents_WithTheirExtraInfoIntact_AndClassifiesThem() => RunSta(() =>
    {
        using var keeper = new StateKeeper();
        var expected = new (uint Extra, TouchGuard.Source Source)[]
        {
            (Marker, TouchGuard.Source.Mouse),
            (TouchRig.TouchExtra, TouchGuard.Source.Touch),
            (TouchRig.PenExtra, TouchGuard.Source.Pen),
            (OwnInput.Signature, TouchGuard.Source.Own),
        };
        var seen = new List<(uint Extra, TouchGuard.Source Source)>();
        using var hook = new MouseHook((int msg, in Native.MSLLHOOKSTRUCT d) =>
        {
            uint extra = unchecked((uint)d.dwExtraInfo);
            if (msg == Native.WM_MOUSEMOVE && expected.Any(e => e.Extra == extra))
                seen.Add((extra, TouchGuard.Classify(d.dwExtraInfo)));
            return false;
        });
        hook.Install();

        var c = PrimaryCenter();
        for (int i = 0; i < expected.Length; i++)
            InjectAbsoluteMove(P(c.X + 10 * i, c.Y), expected[i].Extra);

        Assert.True(PumpUntil(() => seen.Count >= expected.Length), $"Seulement {seen.Count} événements reçus");
        Assert.Equal(expected, seen);
    });

    [Fact]
    public void Hook_ReturningTrue_BlocksTheEvent_SoTheCursorDoesNotMove() => RunSta(() =>
    {
        using var keeper = new StateKeeper();
        var c = PrimaryCenter();
        Native.SetCursorPos(c.X, c.Y);
        int blocked = 0;
        using var hook = new MouseHook((int msg, in Native.MSLLHOOKSTRUCT d) =>
        {
            if (unchecked((uint)d.dwExtraInfo) != Marker) return false;
            blocked++;
            return true;
        });
        hook.Install();

        InjectRelativeMove(40, 25, Marker);
        Assert.True(PumpUntil(() => blocked == 1));
        PumpFor(50);

        Assert.Equal(c, CursorPos);
    });

    [Fact]
    public void Install_IsIdempotent_AndReinstall_KeepsReceivingEvents() => RunSta(() =>
    {
        using var keeper = new StateKeeper();
        int count = 0;
        using var hook = new MouseHook((int msg, in Native.MSLLHOOKSTRUCT d) =>
        {
            if (unchecked((uint)d.dwExtraInfo) == Marker) count++;
            return false;
        });

        hook.Install();
        hook.Install();
        Assert.True(hook.IsInstalled);
        InjectRelativeMove(2, 0, Marker);
        Assert.True(PumpUntil(() => count >= 1));
        PumpFor(50);
        Assert.Equal(1, count); // a single real hook, not two

        hook.Reinstall();
        InjectRelativeMove(-2, 0, Marker);
        Assert.True(PumpUntil(() => count >= 2));
        PumpFor(50);
        Assert.Equal(2, count);
    });

    [Fact]
    public void AfterUninstall_NoMoreCallbacks() => RunSta(() =>
    {
        using var keeper = new StateKeeper();
        int count = 0;
        var hook = new MouseHook((int msg, in Native.MSLLHOOKSTRUCT d) =>
        {
            if (unchecked((uint)d.dwExtraInfo) == Marker) count++;
            return false;
        });
        hook.Install();
        InjectRelativeMove(2, 0, Marker);
        Assert.True(PumpUntil(() => count == 1), "Témoin : l'événement doit être vu tant que le hook est installé");

        hook.Dispose();
        Assert.False(hook.IsInstalled);
        InjectRelativeMove(-2, 0, Marker);
        PumpFor(150);
        Assert.Equal(1, count);
    });

    [Fact]
    public void ExceptionInHandler_IsSwallowed_AndTheEventStillPasses() => RunSta(() =>
    {
        using var keeper = new StateKeeper();
        var c = PrimaryCenter();
        Native.SetCursorPos(c.X, c.Y);
        int calls = 0;
        using var hook = new MouseHook((int msg, in Native.MSLLHOOKSTRUCT d) =>
        {
            if (unchecked((uint)d.dwExtraInfo) != Marker) return false;
            calls++;
            throw new InvalidOperationException("boum");
        });
        hook.Install();

        var target = P(c.X + 30, c.Y + 30);
        InjectAbsoluteMove(target, Marker);

        Assert.True(PumpUntil(() => calls == 1));
        Assert.True(PumpUntil(() => Math.Abs(CursorPos.X - target.X) <= 1 && Math.Abs(CursorPos.Y - target.Y) <= 1),
                    $"Le curseur devait atteindre {target}, il est en {CursorPos}");
    });
}
