using GetOverHere.Tests.Support;
using static GetOverHere.Tests.Support.TouchRig;

namespace GetOverHere.Tests;

/// <summary>Giving the keyboard focus back to the window the user was working in before touching the tablet.</summary>
public class TouchGuardFocusTests
{
    // ───────────── Remembering the right window ─────────────

    [Fact]
    public void TabletWindowActivatedAtContact_WithRawInput_FocusReturnsToOriginalWindow()
    {
        using var rig = new TouchRig();

        rig.TapWithRaw(OnTablet, holdMs: 80, activatesOnContact: WinB);
        Assert.Equal(WinA, rig.Guard.SavedForeground);
        Assert.Empty(rig.Platform.Activations); // nothing during the touch

        rig.Advance(60);
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
        Assert.Equal(WinA, rig.Platform.Foreground);
    }

    [Fact]
    public void TabletWindowActivatedBeforeTheBurst_WithoutRawInput_FocusStillReturnsToOriginalWindow()
    {
        // Regression from the real log: Firefox (tablet) comes to the foreground 63 ms BEFORE the release burst.
        using var rig = new TouchRig();
        rig.Advance(5000);

        rig.SystemActivates(WinB);
        rig.Advance(63);
        rig.TapBurst(OnTablet);
        Assert.Equal(WinA, rig.Guard.SavedForeground);

        rig.Advance(60);
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
        Assert.Equal(WinA, rig.Platform.Foreground);
    }

    [Fact]
    public void WindowActivatedByARealClick_JustBeforeTheTap_IsTheOneRestored()
    {
        // The user clicks in C, then touches the tablet: C (not A) must get the focus back.
        using var rig = new TouchRig();
        rig.Advance(5000);

        rig.RealClick();
        rig.Advance(5);
        rig.SystemActivates(WinC);
        rig.Advance(195);
        rig.SystemActivates(WinB);                // finger lands on the tablet
        rig.Advance(90);
        rig.TapBurst(OnTablet);                   // release 290 ms after the click
        Assert.Equal(WinC, rig.Guard.SavedForeground);

        rig.Advance(60);
        Assert.Equal(new[] { WinC }, rig.Platform.Activations);
    }

    [Fact]
    public void TabletWindowActivatedLate_AfterCursorRestore_IsStillReverted()
    {
        using var rig = new TouchRig();

        rig.TapBurst(OnTablet);
        rig.Advance(60);
        Assert.Empty(rig.Platform.Activations); // nothing to fix yet

        rig.Advance(300);
        rig.SystemActivates(WinB);              // the touched app handles the click late

        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
        Assert.Equal(WinA, rig.Platform.Foreground);
    }

    [Fact]
    public void ForegroundChange_DuringTheTouch_IsCorrectedOnlyAtTheEnd()
    {
        using var rig = new TouchRig();

        rig.Raw();
        rig.Advance(10);
        rig.SystemActivates(WinB);
        Assert.Empty(rig.Platform.Activations);

        rig.Advance(rig.Settings.RawQuietMs);
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
    }

    [Fact]
    public void RawOnlySession_StillRestoresFocus()
    {
        // Finger scrolling with no mouse event: the focus must still come back.
        using var rig = new TouchRig();

        rig.Raw();
        rig.Advance(5);
        rig.SystemActivates(WinB);
        for (int i = 0; i < 5; i++) { rig.Advance(50); rig.Raw(); }
        rig.Advance(rig.Settings.RawQuietMs);

        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
        Assert.Empty(rig.Platform.CursorMoves);
    }

    // ───────────── Focus guard ─────────────

    [Fact]
    public void FocusGuard_ExpiresAfterConfiguredDelay()
    {
        using var rig = new TouchRig(s => s.FocusGuardMs = 1500);
        rig.TapBurst(OnTablet);
        rig.Advance(60);

        rig.Advance(1499);
        Assert.True(rig.Guard.IsFocusGuardActive);
        rig.Advance(1);
        Assert.False(rig.Guard.IsFocusGuardActive);

        rig.SystemActivates(WinB);
        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void RealClick_AfterRestore_EndsTheGuard_SoTheUsersOwnWindowSwitchIsRespected()
    {
        using var rig = new TouchRig();
        rig.TapBurst(OnTablet);
        rig.Advance(60);

        rig.RealClick();
        rig.SystemActivates(WinC);

        Assert.Empty(rig.Platform.Activations);
        Assert.Equal(WinC, rig.Platform.Foreground);
    }

    [Fact]
    public void PenUse_AfterRestore_EndsTheGuard()
    {
        using var rig = new TouchRig();
        rig.TapBurst(OnTablet);
        rig.Advance(60);

        rig.PenMove(P(-400, 400));
        rig.SystemActivates(WinB);

        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void ForegroundChanges_WithoutAnyTouch_AreNeverReverted()
    {
        using var rig = new TouchRig();

        rig.SystemActivates(WinB);
        rig.Advance(100);
        rig.SystemActivates(WinC);
        rig.Advance(10_000);

        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void AppFightingForForeground_IsRevertedAtMostThreeTimesPerTouch()
    {
        using var rig = new TouchRig();
        // The tablet app grabs the foreground back every time it is taken away.
        rig.Platform.OnActivated = _ => rig.Dispatcher.Post(() => rig.SystemActivates(WinB));

        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);
        rig.Advance(1000);

        Assert.Equal(TouchGuard.MaxFocusRestoresPerTouch, rig.Platform.Activations.Count);
        Assert.All(rig.Platform.Activations, h => Assert.Equal(WinA, h));
    }

    [Fact]
    public void FocusRestoreAttempts_AreCountedAgainPerNewTouch()
    {
        using var rig = new TouchRig();

        for (int i = 0; i < 5; i++)
        {
            rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
            rig.Advance(60);
            rig.Advance(2000); // guard expired: each touch is independent
        }

        Assert.Equal(5, rig.Platform.Activations.Count);
    }

    [Fact]
    public void ChainedTouch_WithinGuard_KeepsTheOriginalWindow_EvenIfTheTabletWindowStayedActive()
    {
        using var rig = new TouchRig();
        rig.Platform.ActivationSucceeds = false; // e.g. original window running as administrator

        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);
        Assert.Equal(WinB, rig.Platform.Foreground);

        rig.Advance(300);                        // new touch during the guard
        rig.TapWithRaw(P(-600, 300));
        Assert.Equal(WinA, rig.Guard.SavedForeground);
        rig.Advance(60);

        Assert.Equal(new[] { WinA, WinA }, rig.Platform.Activations);
    }

    // ───────────── Cases where nothing must happen ─────────────

    [Fact]
    public void ForegroundUnchanged_NoActivationAttempt()
    {
        using var rig = new TouchRig();

        rig.TapWithRaw(OnTablet);
        rig.Advance(2000);

        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void RestoreFocusDisabled_NeverActivatesAnything_ButCursorStillReturns()
    {
        using var rig = new TouchRig(s => s.RestoreFocus = false);

        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);
        rig.SystemActivates(WinB);
        rig.Advance(2000);

        Assert.Empty(rig.Platform.Activations);
        Assert.Equal(new[] { Start }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void OriginalWindowClosedMeanwhile_NoActivationAttempt()
    {
        using var rig = new TouchRig();

        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Platform.DeadWindows.Add(WinA);
        rig.Advance(60);

        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void NoForegroundWindow_NoActivationAttempt()
    {
        using var rig = new TouchRig();

        rig.TapWithRaw(OnTablet);
        rig.Platform.Foreground = IntPtr.Zero; // e.g. desktop / transition
        rig.Advance(60);

        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void Disabled_NeverTouchesFocus()
    {
        using var rig = new TouchRig(s => s.Enabled = false);

        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(2000);

        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void DisablingAfterRestore_StopsTheGuardFromActing()
    {
        using var rig = new TouchRig();
        rig.TapBurst(OnTablet);
        rig.Advance(60);

        rig.Settings.Enabled = false;
        rig.SystemActivates(WinB);

        Assert.Empty(rig.Platform.Activations);
    }

    // ───────────── Estimating the contact time (without raw reports) ─────────────

    [Fact]
    public void EstimateContactTime_WithoutRecentClick_LooksBackTapLookback()
    {
        using var rig = new TouchRig();

        Assert.Equal(100_000 - TouchGuard.TapLookbackMs, rig.Guard.EstimateContactTime(100_000));
    }

    [Fact]
    public void EstimateContactTime_IsBoundedByLastRealClickPlusGrace()
    {
        using var rig = new TouchRig();
        rig.RealClick();
        int click = rig.Now;

        Assert.Equal(click + TouchGuard.ClickActivationGraceMs, rig.Guard.EstimateContactTime(click + 300));
    }

    [Fact]
    public void EstimateContactTime_IgnoresAnOldClick()
    {
        using var rig = new TouchRig();
        rig.RealClick();
        int click = rig.Now;

        Assert.Equal(click + 5000 - TouchGuard.TapLookbackMs, rig.Guard.EstimateContactTime(click + 5000));
    }

    [Fact]
    public void EstimateContactTime_NeverGoesAfterTheRelease()
    {
        using var rig = new TouchRig();
        rig.RealClick();
        int click = rig.Now;

        Assert.Equal(click + 50, rig.Guard.EstimateContactTime(click + 50));
    }

    [Fact]
    public void EstimateContactTime_HandlesTickCountWraparound()
    {
        using var rig = new TouchRig();
        rig.Clock.Now = (long)int.MaxValue - 100; // the 32-bit clock is about to wrap
        rig.RealClick();
        int click = rig.Now;
        int release = unchecked(click + 400);      // negative after wraparound

        Assert.True(release < 0);
        Assert.Equal(unchecked(click + TouchGuard.ClickActivationGraceMs), rig.Guard.EstimateContactTime(release));
    }
}
