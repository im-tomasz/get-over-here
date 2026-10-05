using GetOverHere.Tests.Support;
using static GetOverHere.Tests.Support.TouchRig;

namespace GetOverHere.Tests;

/// <summary>Bringing the cursor back: when, where, how many times, and never wrongly.</summary>
public class TouchGuardCursorTests
{
    // ───────────── Nominal case ─────────────

    [Fact]
    public void Tap_RestoresCursorToItsPreviousPosition_ExactlyAfterRestoreDelay()
    {
        using var rig = new TouchRig();

        rig.TapBurst(OnTablet);
        Assert.Equal(OnTablet, rig.Platform.CursorPos); // Windows moved the cursor onto the tablet

        rig.Advance(59);
        Assert.Empty(rig.Platform.CursorMoves);
        Assert.True(rig.Guard.IsSessionActive);

        rig.Advance(1);
        Assert.Equal(new[] { Start }, rig.Platform.CursorMoves);
        Assert.Equal(Start, rig.Platform.CursorPos);
        Assert.False(rig.Guard.IsSessionActive);
    }

    [Fact]
    public void Tap_RestoresOnlyOnce_EvenLongAfter()
    {
        using var rig = new TouchRig();

        rig.TapBurst(OnTablet);
        rig.Advance(10_000);

        Assert.Single(rig.Platform.CursorMoves);
        Assert.Null(rig.Timer.DueAt);
    }

    [Fact]
    public void Tap_UsesConfiguredRestoreDelay()
    {
        using var rig = new TouchRig(s => s.RestoreDelayMs = 120);

        rig.TapBurst(OnTablet);
        Assert.Equal(120, rig.Timer.LastInterval);

        rig.Advance(119);
        Assert.Empty(rig.Platform.CursorMoves);
        rig.Advance(1);
        Assert.Single(rig.Platform.CursorMoves);
    }

    [Fact]
    public void TouchEvents_AreNeverBlocked_ByDefault()
    {
        using var rig = new TouchRig();

        Assert.False(rig.TouchEvent(Native.WM_MOUSEMOVE, OnTablet));
        Assert.False(rig.TouchEvent(Native.WM_LBUTTONDOWN, OnTablet));
        Assert.False(rig.TouchEvent(Native.WM_LBUTTONUP, OnTablet));
        Assert.False(rig.TouchEvent(Native.WM_RBUTTONDOWN, OnTablet));
        Assert.False(rig.TouchEvent(Native.WM_RBUTTONUP, OnTablet));
        Assert.False(rig.TouchEvent(Native.WM_MOUSEWHEEL, OnTablet));
    }

    [Fact]
    public void Restore_IsNeverPerformedInsideTheHookCallback()
    {
        // Regression: a SendInput/SetCursorPos inside the hook stalled Windows for ~300 ms and lost the release.
        using var rig = new TouchRig();

        rig.TapBurst(OnTablet);
        rig.RealMove(5, 0);
        rig.RealMove(5, 0);

        Assert.Empty(rig.Platform.CursorMoves);
        Assert.Empty(rig.Platform.Activations);
    }

    // ───────────── The real mouse during / after a touch ─────────────

    [Fact]
    public void RealMouseMove_WhileCursorHijacked_IsBlocked_AndRestoreIsPostedThenExecuted()
    {
        using var rig = new TouchRig();
        rig.TapBurst(OnTablet);

        bool blocked = rig.RealMove(5, 0);

        Assert.True(blocked);
        Assert.Equal(OnTablet, rig.Platform.CursorPos);   // the move did not happen from the tablet
        Assert.Equal(1, rig.Dispatcher.Pending);

        rig.Pump();
        Assert.Equal(new[] { P(805, 450) }, rig.Platform.CursorMoves); // move applied to the original position
        Assert.False(rig.Guard.IsSessionActive);
    }

    [Fact]
    public void SeveralRealMoves_BeforeRestoreRuns_AreAccumulated_AndRestoreIsPostedOnce()
    {
        using var rig = new TouchRig();
        rig.TapBurst(OnTablet);

        Assert.True(rig.RealMove(5, 0));
        Assert.True(rig.RealMove(3, -2));
        Assert.True(rig.RealMove(-1, 7));
        Assert.Equal(1, rig.Dispatcher.Pending);

        rig.Pump();
        Assert.Equal(new[] { P(807, 455) }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void AfterEarlyRestore_TheRestoreTimerNoLongerFires()
    {
        using var rig = new TouchRig();
        rig.TapBurst(OnTablet);
        rig.RealMove(5, 0);
        rig.Pump();

        rig.Advance(10_000);

        Assert.Single(rig.Platform.CursorMoves);
    }

    [Fact]
    public void AfterRestore_RealMouseMovesPassThrough_FromTheRestoredPosition()
    {
        using var rig = new TouchRig();
        rig.TapBurst(OnTablet);
        rig.Advance(60);

        Assert.False(rig.RealMove(1, 1));
        Assert.False(rig.RealMove(10, -4));
        Assert.Equal(P(811, 447), rig.Platform.CursorPos);
        Assert.Single(rig.Platform.CursorMoves);
    }

    [Fact]
    public void RealMouseMove_RacingTheTouchRelease_NeverLeavesCursorOnTablet()
    {
        // Scenario from the log: the touchpad moves 1 to 5 ms after the release, several times in a row.
        using var rig = new TouchRig();

        for (int i = 0; i < 20; i++)
        {
            rig.TouchEvent(Native.WM_MOUSEMOVE, OnTablet);
            rig.TouchEvent(Native.WM_LBUTTONDOWN, OnTablet);
            rig.RealMove(3, 0);                     // during the burst
            rig.TouchEvent(Native.WM_LBUTTONUP, OnTablet);
            rig.Advance(2);
            rig.RealMove(3, 0);                     // right after
            rig.Advance(100);

            Assert.True(rig.Platform.CursorPos.X >= 0, $"Itération {i} : curseur resté sur la tablette en {rig.Platform.CursorPos}");
            Assert.False(rig.Guard.IsSessionActive);
        }
    }

    [Fact]
    public void RealButtonDown_WhileHijacked_IsNotBlocked_ButTriggersRestore()
    {
        using var rig = new TouchRig();
        rig.TapBurst(OnTablet);

        Assert.False(rig.RealButton(Native.WM_LBUTTONDOWN));
        Assert.Equal(1, rig.Dispatcher.Pending);

        rig.Pump();
        Assert.Equal(new[] { Start }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void RealMouse_WithoutTouch_IsNeverBlocked_AndNothingIsRestored()
    {
        using var rig = new TouchRig();

        Assert.False(rig.RealMove(-2000, 100)); // including on the tablet
        Assert.False(rig.RealButton(Native.WM_LBUTTONDOWN));
        Assert.False(rig.RealButton(Native.WM_LBUTTONUP));
        Assert.False(rig.Hook(Native.WM_MOUSEWHEEL, rig.Platform.CursorPos, 0));
        rig.Advance(10_000);

        Assert.Empty(rig.Platform.CursorMoves);
        Assert.Empty(rig.Platform.Activations);
        Assert.Equal(0, rig.Dispatcher.Pending);
    }

    [Fact]
    public void Anchor_FollowsTheRealMouse_SoRestoreTargetsItsLatestPosition()
    {
        using var rig = new TouchRig();
        rig.RealMove(100, 50);
        rig.RealMove(-30, 0);

        rig.TapBurst(OnTablet);
        rig.Advance(60);

        Assert.Equal(new[] { P(870, 500) }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void Anchor_OnTabletFromRealMouse_IsRestoredThere()
    {
        // If the user was working with the mouse on the tablet, that is where the cursor must come back.
        using var rig = new TouchRig();
        rig.RealMove(-1500, 0);

        rig.TapBurst(P(-200, 300));
        rig.Advance(60);

        Assert.Equal(new[] { P(-700, 450) }, rig.Platform.CursorMoves);
    }

    // ───────────── Gestures ─────────────

    [Fact]
    public void HeldFinger_UsesHeldTimeout_InsteadOfRestoreDelay()
    {
        using var rig = new TouchRig();

        rig.TouchEvent(Native.WM_MOUSEMOVE, OnTablet);
        rig.TouchEvent(Native.WM_LBUTTONDOWN, OnTablet);
        Assert.Equal(rig.Settings.HeldTimeoutMs, rig.Timer.LastInterval);

        rig.Advance(rig.Settings.HeldTimeoutMs - 1);
        Assert.Empty(rig.Platform.CursorMoves);

        rig.Advance(1);
        Assert.Equal(new[] { Start }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void Drag_KeepsSessionAlive_ThenRestoresAfterRelease()
    {
        using var rig = new TouchRig();

        rig.TouchEvent(Native.WM_LBUTTONDOWN, OnTablet);
        for (int i = 0; i < 50; i++)
        {
            rig.Advance(100);
            rig.TouchEvent(Native.WM_MOUSEMOVE, P(OnTablet.X + i, OnTablet.Y));
        }
        Assert.Empty(rig.Platform.CursorMoves);

        rig.TouchEvent(Native.WM_LBUTTONUP, P(-900, 600));
        rig.Advance(59);
        Assert.Empty(rig.Platform.CursorMoves);
        rig.Advance(1);
        Assert.Equal(new[] { Start }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void LongPress_RightClick_IsHandledLikeATap()
    {
        using var rig = new TouchRig();

        rig.TouchEvent(Native.WM_MOUSEMOVE, OnTablet);
        rig.TouchEvent(Native.WM_RBUTTONDOWN, OnTablet);
        rig.TouchEvent(Native.WM_RBUTTONUP, OnTablet);
        rig.Advance(60);

        Assert.Equal(new[] { Start }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void SecondTap_BeforeRestore_ExtendsTheSession_WithASingleRestoreToTheOriginalPosition()
    {
        using var rig = new TouchRig();

        rig.TapBurst(OnTablet);
        rig.Advance(30);
        rig.TapBurst(P(-300, 200));
        rig.Advance(59);
        Assert.Empty(rig.Platform.CursorMoves);

        rig.Advance(1);
        Assert.Equal(new[] { Start }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void ManyRapidTaps_EachRestoreGoesBackToTheSameOriginalPosition()
    {
        using var rig = new TouchRig();

        for (int i = 0; i < 10; i++)
        {
            rig.TapBurst(P(-100 * (i + 1), 300 + i));
            rig.Advance(200);
        }

        Assert.Equal(10, rig.Platform.CursorMoves.Count);
        Assert.All(rig.Platform.CursorMoves, p => Assert.Equal(Start, p));
    }

    // ───────────── Raw touch-screen reports ─────────────

    [Fact]
    public void RawReport_StartsSession_WithoutMovingCursor()
    {
        using var rig = new TouchRig();

        rig.Raw();

        Assert.True(rig.Guard.IsSessionActive);
        Assert.False(rig.Guard.IsHijacked);
        Assert.Equal(rig.Settings.RawQuietMs, rig.Timer.LastInterval);
    }

    [Fact]
    public void RawOnlySession_EndsAfterQuietDelay_WithoutAnyCursorMove()
    {
        // E.g. finger scrolling in a touch-aware app: no mouse event, cursor never moved.
        using var rig = new TouchRig();

        rig.Raw();
        rig.Advance(rig.Settings.RawQuietMs - 1);
        Assert.True(rig.Guard.IsSessionActive);

        rig.Advance(1);
        Assert.False(rig.Guard.IsSessionActive);
        Assert.Empty(rig.Platform.CursorMoves);
    }

    [Fact]
    public void RawReports_KeepTheSessionAlive_UntilTheyStop()
    {
        using var rig = new TouchRig();

        for (int i = 0; i < 20; i++)
        {
            rig.Raw();
            rig.Advance(100);
        }
        Assert.True(rig.Guard.IsSessionActive);

        rig.Advance(rig.Settings.RawQuietMs - 100);
        Assert.False(rig.Guard.IsSessionActive);
    }

    [Fact]
    public void RawThenBurst_SwitchesToRestoreDelay_AndRestores()
    {
        using var rig = new TouchRig();

        rig.TapWithRaw(OnTablet, holdMs: 90);
        Assert.Equal(rig.Settings.RestoreDelayMs, rig.Timer.LastInterval);

        rig.Advance(rig.Settings.RestoreDelayMs);
        Assert.Equal(new[] { Start }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void RealMove_WhileFingerDownButBeforeBurst_PassesThrough_AndBecomesRestoreTarget()
    {
        using var rig = new TouchRig();
        rig.Raw();

        Assert.False(rig.RealMove(10, 0));
        Assert.False(rig.RealMove(0, 5));
        rig.TapBurst(OnTablet);
        rig.Advance(60);

        Assert.Equal(new[] { P(810, 455) }, rig.Platform.CursorMoves);
    }

    // ───────────── What must NOT trigger a restore ─────────────

    [Fact]
    public void Disabled_IgnoresTouchCompletely()
    {
        using var rig = new TouchRig(s => s.Enabled = false);

        rig.Raw();
        bool blocked = rig.TapBurst(OnTablet);
        rig.Advance(10_000);

        Assert.False(blocked);
        Assert.False(rig.Guard.IsSessionActive);
        Assert.Empty(rig.Platform.CursorMoves);
        Assert.Null(rig.Timer.LastInterval);
    }

    [Fact]
    public void Disabled_DoesNotBlockTouch_EvenInZeroMoveMode()
    {
        using var rig = new TouchRig(s => { s.Enabled = false; s.ZeroMoveMode = true; });

        Assert.False(rig.TapBurst(OnTablet));
    }

    [Fact]
    public void TouchSignatureOnNonTouchMonitor_IsIgnored()
    {
        using var rig = new TouchRig();

        rig.TapBurst(P(300, 300));
        rig.Advance(10_000);

        Assert.False(rig.Guard.IsSessionActive);
        Assert.Empty(rig.Platform.CursorMoves);
    }

    [Fact]
    public void Pen_NeverStartsASession()
    {
        using var rig = new TouchRig();

        Assert.False(rig.PenMove(OnTablet));
        Assert.False(rig.Hook(Native.WM_LBUTTONDOWN, OnTablet, PenExtra));
        Assert.False(rig.Hook(Native.WM_LBUTTONUP, OnTablet, PenExtra));
        rig.Advance(10_000);

        Assert.False(rig.Guard.IsSessionActive);
        Assert.Empty(rig.Platform.CursorMoves);
        Assert.Equal(OnTablet, rig.Guard.Anchor); // the pen positions the cursor on purpose
    }

    [Fact]
    public void Pen_DuringTouchSession_CancelsRestore_AndBecomesTheNewAnchor()
    {
        using var rig = new TouchRig();
        rig.TapBurst(OnTablet);

        rig.PenMove(P(-500, 500));
        rig.Advance(1000);
        Assert.Empty(rig.Platform.CursorMoves);

        rig.TapBurst(P(-1200, 700));
        rig.Advance(60);
        Assert.Equal(new[] { P(-500, 500) }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void OwnInjectedInput_IsIgnored_AndDoesNotMoveTheAnchor()
    {
        using var rig = new TouchRig();

        Assert.False(rig.Hook(Native.WM_MOUSEMOVE, P(5, 5), OwnInput.Signature));
        Assert.Equal(Start, rig.Guard.Anchor);

        rig.TapBurst(OnTablet);
        Assert.False(rig.Hook(Native.WM_MOUSEMOVE, P(5, 5), OwnInput.Signature)); // must neither cancel nor restore
        Assert.Equal(0, rig.Dispatcher.Pending);
        rig.Advance(60);

        Assert.Equal(new[] { Start }, rig.Platform.CursorMoves);
    }

    [Fact]
    public void Reset_DuringSession_CancelsTheRestore()
    {
        using var rig = new TouchRig();
        rig.TapBurst(OnTablet);

        rig.Guard.Reset();
        rig.Advance(10_000);

        Assert.False(rig.Guard.IsSessionActive);
        Assert.Empty(rig.Platform.CursorMoves);
    }

    // ───────────── Zero-move mode ─────────────

    [Fact]
    public void ZeroMoveMode_BlocksEveryTouchEvent_SoTheCursorNeverLeaves()
    {
        using var rig = new TouchRig(s => s.ZeroMoveMode = true);

        Assert.True(rig.TouchEvent(Native.WM_MOUSEMOVE, OnTablet));
        Assert.True(rig.TouchEvent(Native.WM_LBUTTONDOWN, OnTablet));
        Assert.True(rig.TouchEvent(Native.WM_LBUTTONUP, OnTablet));
        Assert.Equal(Start, rig.Platform.CursorPos);

        rig.Advance(60);
        Assert.Equal(Start, rig.Platform.CursorPos);
        Assert.False(rig.Guard.IsSessionActive);
    }

    [Fact]
    public void ZeroMoveMode_DoesNotBlockRealMouseOrPen()
    {
        using var rig = new TouchRig(s => s.ZeroMoveMode = true);

        Assert.False(rig.RealMove(10, 10));
        Assert.False(rig.PenMove(OnTablet));
    }

    // ───────────── Lifecycle ─────────────

    [Fact]
    public void Dispose_DisposesTheTimer()
    {
        var rig = new TouchRig();
        rig.Dispose();

        Assert.True(rig.Timer.Disposed);
    }
}
