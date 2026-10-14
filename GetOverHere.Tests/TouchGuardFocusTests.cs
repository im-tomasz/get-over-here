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

    // ───────────── Cooldown before the focus returns ─────────────

    private static TouchRig CooldownRig(int delayMs = 3000) => new(s =>
    {
        s.DelayFocusReturn = true;
        s.FocusReturnDelayMs = delayMs;
    });

    [Fact]
    public void Cooldown_CursorReturnsAtOnce_FocusOnlyWhenTheDelayEnds()
    {
        using var rig = CooldownRig(3000);

        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);
        Assert.Equal(new[] { Start }, rig.Platform.CursorMoves);
        Assert.Empty(rig.Platform.Activations);
        Assert.True(rig.Guard.IsFocusReturnPending);

        rig.Advance(2999);
        Assert.Empty(rig.Platform.Activations);
        Assert.Equal(WinB, rig.Platform.Foreground);

        rig.Advance(1);
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
        Assert.Equal(WinA, rig.Platform.Foreground);
        Assert.False(rig.Guard.IsFocusReturnPending);
    }

    [Fact]
    public void Cooldown_LateGrabDuringTheDelay_IsLeftAlone_ThenRevertedAtTheEnd()
    {
        using var rig = CooldownRig(3000);
        rig.TapBurst(OnTablet);
        rig.Advance(60);

        rig.Advance(300);
        rig.SystemActivates(WinB);          // the touched app handles the click late
        Assert.Empty(rig.Platform.Activations);

        rig.Advance(2700);
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
    }

    [Fact]
    public void Cooldown_GuardStillCoversLateGrabs_AfterTheFocusCameBack()
    {
        using var rig = new TouchRig(s =>
        {
            s.DelayFocusReturn = true;
            s.FocusReturnDelayMs = 2000;
            s.FocusGuardMs = 1500;
        });
        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);
        rig.Advance(2000);                   // focus back to A

        rig.Advance(1000);
        rig.SystemActivates(WinB);           // grab within the guard: reverted
        Assert.Equal(new[] { WinA, WinA }, rig.Platform.Activations);

        rig.Advance(600);                    // guard over
        Assert.False(rig.Guard.IsFocusGuardActive);
        rig.SystemActivates(WinB);
        Assert.Equal(2, rig.Platform.Activations.Count);
    }

    [Fact]
    public void Cooldown_TypingInTheTabletWindow_CancelsTheReturn_HoweverLongTheUserThought()
    {
        // Tap Discord's message box, think for a few seconds, start typing: the focus must stay on the tablet.
        using var rig = CooldownRig(3000);
        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);

        rig.Advance(2900);                   // thinking, just before the end of the delay
        rig.KeyPress();
        Assert.False(rig.Guard.IsFocusReturnPending);
        Assert.False(rig.Guard.IsFocusGuardActive);

        rig.Advance(10_000);
        rig.SystemActivates(WinB);           // even a late grab is the user's choice now
        Assert.Empty(rig.Platform.Activations);
        Assert.Equal(WinB, rig.Platform.Foreground);
    }

    [Fact]
    public void Cooldown_SystemKeyInTheTabletWindow_AlsoCancels_ButKeyReleasesDoNot()
    {
        using var rig = CooldownRig(3000);
        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);

        rig.Advance(1000);
        rig.Guard.OnKeyboardEvent(Native.WM_KEYUP);      // release of a key pressed before the touch
        rig.Guard.OnKeyboardEvent(Native.WM_SYSKEYUP);
        Assert.True(rig.Guard.IsFocusReturnPending);

        rig.Guard.OnKeyboardEvent(Native.WM_SYSKEYDOWN); // e.g. Alt+Tab from the tablet window
        Assert.False(rig.Guard.IsFocusReturnPending);

        rig.Advance(5000);
        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void Cooldown_TypingInTheOriginalWindow_DoesNotCancel()
    {
        // The touched app did not take the focus: the user types in A. A late grab must still be reverted.
        using var rig = CooldownRig(3000);
        rig.TapBurst(OnTablet);
        rig.Advance(60);

        rig.Advance(1000);
        rig.KeyPress();
        Assert.True(rig.Guard.IsFocusReturnPending);

        rig.Advance(500);
        rig.SystemActivates(WinB);           // late grab during the cooldown
        rig.Advance(3000);                   // 3 s after the key
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
    }

    [Fact]
    public void Cooldown_TypingInAMainScreenWindow_StartsTheDelayOverFromTheLastKey()
    {
        using var rig = CooldownRig(3000);
        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);
        rig.Advance(500);
        rig.SystemActivates(WinC);           // a window on the main screen took the focus by itself

        for (int i = 0; i < 5; i++)          // types for 10 s, a key every 2 s
        {
            rig.Advance(2000);
            rig.KeyPress();
        }
        Assert.Empty(rig.Platform.Activations);

        rig.Advance(2999);
        Assert.Empty(rig.Platform.Activations);
        rig.Advance(1);
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
    }

    [Fact]
    public void Cooldown_TypingInAMainScreenWindow_AlsoExtendsTheGuard()
    {
        using var rig = new TouchRig(s =>
        {
            s.DelayFocusReturn = true;
            s.FocusReturnDelayMs = 2000;
            s.FocusGuardMs = 1500;
        });
        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);
        rig.SystemActivates(WinC);
        rig.Advance(1500);
        rig.KeyPress();
        rig.Advance(2000);                   // focus back, 2 s after the key
        Assert.Single(rig.Platform.Activations);

        rig.Advance(1400);
        rig.SystemActivates(WinB);           // still within the guard counted from the return
        Assert.Equal(2, rig.Platform.Activations.Count);
    }

    [Fact]
    public void Typing_OutsideACooldown_ChangesNothing()
    {
        using var rig = CooldownRig(3000);

        rig.KeyPress();                      // no touch yet
        Assert.Null(rig.Timer.DueAt);

        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.KeyPress();                      // during the touch itself: end of touch unchanged
        rig.Advance(60);
        Assert.True(rig.Guard.IsFocusReturnPending);

        rig.Advance(3000);
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
        rig.KeyPress();                      // after the return
        rig.Advance(10_000);
        Assert.Single(rig.Platform.Activations);
    }

    [Fact]
    public void Typing_WithTheDelayTurnedOffMeanwhile_DoesNotExtend()
    {
        using var rig = CooldownRig(3000);
        rig.TapBurst(OnTablet);
        rig.Advance(60);
        rig.SystemActivates(WinC);

        rig.Settings.DelayFocusReturn = false;
        rig.Advance(2000);
        rig.KeyPress();
        rig.Advance(1000);

        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
    }

    [Fact]
    public void FocusReturnPendingChanged_IsRaisedWhenTheCooldownStartsAndEnds()
    {
        using var rig = CooldownRig(3000);
        var states = new List<bool>();
        rig.Guard.FocusReturnPendingChanged += () => states.Add(rig.Guard.IsFocusReturnPending);

        rig.TapBurst(OnTablet);
        rig.Advance(60);
        rig.KeyPress();                      // extends, no state change
        rig.Advance(3000);
        Assert.Equal(new[] { true, false }, states);

        rig.TapBurst(OnTablet);
        rig.Advance(60);
        rig.RealClick();                     // cancelled
        Assert.Equal(new[] { true, false, true, false }, states);
    }

    [Fact]
    public void FocusReturnPendingChanged_IsNeverRaised_WithoutTheCooldown()
    {
        using var rig = new TouchRig();
        int raised = 0;
        rig.Guard.FocusReturnPendingChanged += () => raised++;

        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(2000);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Cooldown_RealClickDuringTheDelay_CancelsTheReturn()
    {
        using var rig = CooldownRig(3000);
        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);

        rig.Advance(1000);
        rig.RealClick();                     // the user picks a window themselves
        rig.SystemActivates(WinC);
        rig.Advance(5000);

        Assert.Empty(rig.Platform.Activations);
        Assert.Equal(WinC, rig.Platform.Foreground);
        Assert.False(rig.Guard.IsFocusReturnPending);
    }

    [Fact]
    public void Cooldown_PenDuringTheDelay_CancelsTheReturn()
    {
        using var rig = CooldownRig(3000);
        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);

        rig.PenMove(P(-400, 400));
        rig.Advance(5000);

        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void Cooldown_ChainedTouch_KeepsTheOriginalWindow_AndRestartsTheDelay()
    {
        using var rig = CooldownRig(3000);
        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);

        rig.Advance(2000);                   // second tap before the focus came back
        rig.TapWithRaw(P(-600, 300));
        Assert.Equal(WinA, rig.Guard.SavedForeground);
        rig.Advance(60);

        rig.Advance(2999);                   // the first cooldown would have ended long ago
        Assert.Empty(rig.Platform.Activations);
        rig.Advance(1);
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
    }

    [Fact]
    public void Cooldown_RestoreAlreadyPostedByTheMouse_DoesNotCancelTheDelay()
    {
        // The timer ends the touch, then the restore posted from the hook by a real mouse move runs late.
        using var rig = CooldownRig(3000);
        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.RealMove(5, 0);                  // posts a restore, not run yet

        rig.Advance(60);
        Assert.True(rig.Guard.IsFocusReturnPending);

        rig.Advance(3000);
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
    }

    [Fact]
    public void Cooldown_RawOnlySession_AlsoWaits()
    {
        using var rig = CooldownRig(2000);

        rig.Raw();
        rig.Advance(5);
        rig.SystemActivates(WinB);
        rig.Advance(rig.Settings.RawQuietMs);
        Assert.Empty(rig.Platform.Activations);

        rig.Advance(2000);
        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
    }

    [Fact]
    public void Cooldown_DisablingTheAppDuringTheDelay_CancelsTheReturn()
    {
        using var rig = CooldownRig(3000);
        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);

        rig.Settings.Enabled = false;
        rig.Guard.Reset();
        rig.Advance(5000);

        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void Cooldown_IsIgnored_WhenFocusRestoreIsOff()
    {
        using var rig = new TouchRig(s =>
        {
            s.RestoreFocus = false;
            s.DelayFocusReturn = true;
        });

        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);
        Assert.False(rig.Guard.IsFocusReturnPending);
        rig.Advance(10_000);

        Assert.Empty(rig.Platform.Activations);
    }

    [Fact]
    public void Cooldown_Off_FocusReturnsImmediately_WhateverTheSliderValue()
    {
        using var rig = new TouchRig(s => s.FocusReturnDelayMs = 10_000);

        rig.TapWithRaw(OnTablet, activatesOnContact: WinB);
        rig.Advance(60);

        Assert.Equal(new[] { WinA }, rig.Platform.Activations);
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
