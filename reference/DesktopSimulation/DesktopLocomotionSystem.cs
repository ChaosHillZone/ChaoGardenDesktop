using System;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Shared;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.Simulation.Systems;

namespace ChaoGarden.Simulation.Desktop;

/// <summary>Adapts native gait/air/climb rules to a flat desktop floor and two screen walls.</summary>
public static class DesktopLocomotionSystem
{
    // Garden climbing is contact-driven; this Power gate is a desktop policy.
    public const int ClimbPowerThreshold = 50;
    public const float WallTurnDegreesPerTick = 364 * 360f / 65536;
    public static bool CanFly(DesktopChaoState state) => (uint)state.Chao.Stats.Fly.ProgressPoints * 3u >= 400u;
    public static bool CanClimb(DesktopChaoState state) => state.Chao.Stats.Power.ProgressPoints >= ClimbPowerThreshold;
    public static bool IsMoving(DesktopChaoState state) => state.Activity is DesktopActivity.WalkLeft or DesktopActivity.WalkRight;
    public static bool IsTurning(DesktopChaoState state) => state.Activity is DesktopActivity.TurnLeft or DesktopActivity.TurnRight;
    public static bool IsAirborne(DesktopChaoState state) => state.Activity is DesktopActivity.Flying or DesktopActivity.Falling or DesktopActivity.Climbing;
    public static bool IsCrawling(DesktopChaoState state) => state.Activity is DesktopActivity.CrawlStart or DesktopActivity.CrawlGetUp
        || state.TravelGait == ChaoAnimation.LandCrawlStart && (IsMoving(state) || IsTurning(state));

    public static double PixelsPerUnit(DesktopChaoState state) => state.Settings.Size * (416.0 / 384) / 11.04;

    public static void StartStroll(DesktopChaoState state, DesktopActivity direction, int ticks)
    {
        state.TravelGait = SelectGait(state);
        state.TravelDirection = direction;
        state.TravelTicksRemaining = ticks;
        ResetVelocity(state);
        if (state.TravelGait == ChaoAnimation.LandCrawlStart)
            DesktopChaoSimulation.Begin(state, DesktopActivity.CrawlSitDown, 1);
        else DesktopChaoSimulation.Begin(state, direction, ticks);
    }

    private static ChaoAnimation SelectGait(DesktopChaoState state)
    {
        ChaoAnimation gait = ChaoLandRecoverySystem.SelectGait(state.Chao);
        return gait == ChaoAnimation.LandCrawlStart && state.Chao.ChaoType != ChaoType.Child
            ? ChaoAnimation.LandWalk : gait;
    }

    public static void PreparePlayback(DesktopChaoState state)
    {
        // Type edits, imports/sync and old saves can leave an adult in a child gait.
        // Recover the active stroll on the next fixed tick; keep queued interactions intact.
        if (state.Chao.ChaoType != ChaoType.Child && (state.TravelGait == ChaoAnimation.LandCrawlStart
            || state.Activity is DesktopActivity.CrawlSitDown or DesktopActivity.CrawlStart))
        {
            state.TravelGait = SelectGait(state);
            if (state.Activity is DesktopActivity.CrawlSitDown or DesktopActivity.CrawlStart)
                StartStroll(state, state.TravelDirection, state.TravelTicksRemaining);
            else if (IsMoving(state) || IsTurning(state))
                DesktopChaoSimulation.Begin(state, state.Activity, Math.Max(1, state.ActivityDuration - state.ActivityTicks));
        }
        if (IsTurning(state)) state.Chao.Animation.PlaybackRate = IsCrawling(state) ? 1f : 1.6f;
        else if (IsMoving(state) && !state.Chao.Animation.IsBlending)
        {
            state.Chao.Behaviour.Action.RequestedAnimation = (int)state.TravelGait;
            ChaoLandMovementSystem.UpdatePlaybackRate(state.Chao);
        }
        else state.Chao.Animation.PlaybackRate = 1f;
    }

    /// <summary>Returns true when movement owns the current activity's completion.</summary>
    public static bool Update(DesktopChaoState state, double travelWidth, double travelHeight, bool allowClimb = true)
    {
        double scale = PixelsPerUnit(state);
        float targetYaw = state.Activity switch
        {
            DesktopActivity.WalkLeft or DesktopActivity.TurnLeft => -65,
            DesktopActivity.WalkRight or DesktopActivity.TurnRight => 65,
            DesktopActivity.CrawlSitDown or DesktopActivity.CrawlStart => state.TravelDirection == DesktopActivity.WalkLeft ? -65 : 65,
            DesktopActivity.Climbing => state.Position < .5 ? -90 : 90,
            DesktopActivity.Flying or DesktopActivity.Falling => state.Chao.Physics.VelocityX < 0 ? -65 : 65,
            _ => DesktopChaoSimulation.IsSleeping(state) ? 30 : 0
        };
        float turnRate = IsTurning(state) ? WallTurnDegreesPerTick : 288 * 360f / 65536;
        state.FacingYaw += Math.Clamp(targetYaw - state.FacingYaw, -turnRate, turnRate);
        if (IsMoving(state))
        {
            if (state.ActivityTicks >= state.ActivityDuration)
            {
                if (IsCrawling(state)) RecoverCrawl(state, DesktopActivity.Sitting);
                else DesktopChaoSimulation.Begin(state, DesktopActivity.Idle, QuietTicks(state));
                ResetVelocity(state);
                return true;
            }
            if (state.Chao.Animation.IsBlending) return true;
            PhysicsState physics = state.Chao.Physics;
            state.Chao.Behaviour.Action.RequestedAnimation = (int)state.TravelGait;
            physics.VelocityX += ChaoLandMovementSystem.Acceleration(state.Chao);
            physics.VelocityY += ChaoLandMovementSystem.Gravity;
            double direction = state.Activity == DesktopActivity.WalkLeft ? -1 : 1;
            state.Position = Math.Clamp(state.Position + direction * physics.VelocityX * scale / Math.Max(1, travelWidth), 0, 1);
            ChaoLandMovementSystem.ApplyFloorResponse(physics);
            if (state.Position is <= 0 or >= 1)
            {
                state.TravelTicksRemaining = Math.Max(60, state.ActivityDuration - state.ActivityTicks);
                state.TravelDirection = state.Position <= 0 ? DesktopActivity.WalkRight : DesktopActivity.WalkLeft;
                DesktopActivity next = allowClimb && CanClimb(state) && DeterministicRandom.NextRange(state.Random, 0, 100) < 35
                    ? DesktopActivity.Climbing
                    : state.Position <= 0 ? DesktopActivity.TurnRight : DesktopActivity.TurnLeft;
                ResetVelocity(state);
                // A turn changes heading, not posture. Only climbing needs the crawler to stand.
                if (next == DesktopActivity.Climbing && IsCrawling(state)) RecoverCrawl(state, next);
                else StartStandingAction(state, next);
            }
            return true;
        }
        if (IsTurning(state))
        {
            if (state.Chao.Animation.IsBlending || Math.Abs(targetYaw - state.FacingYaw) > .01f) return true;
            // Resume the existing gait directly; StartStroll would sit and lower a crawler again.
            DesktopChaoSimulation.Begin(state, state.TravelDirection, state.TravelTicksRemaining);
            return true;
        }
        switch (state.Activity)
        {
            case DesktopActivity.CrawlSitDown:
                if (DesktopAnimationSystem.TransitionFinished(state))
                    DesktopChaoSimulation.Begin(state, DesktopActivity.CrawlStart, 1);
                return true;
            case DesktopActivity.CrawlStart:
                if (DesktopAnimationSystem.TransitionFinished(state))
                    DesktopChaoSimulation.Begin(state, state.TravelDirection, state.TravelTicksRemaining);
                return true;
            case DesktopActivity.CrawlGetUp:
                if (!DesktopAnimationSystem.TransitionFinished(state)) return true;
                if (state.AfterCrawling == DesktopActivity.Sitting)
                {
                    state.RestTicksRemaining = QuietTicks(state);
                    DesktopChaoSimulation.Begin(state, DesktopActivity.Sitting, 1800);
                }
                else if (state.AfterCrawling == DesktopActivity.EatStart)
                    DesktopChaoSimulation.Begin(state, DesktopActivity.EatStart, 1);
                else
                {
                    state.AfterStanding = state.AfterCrawling;
                    DesktopChaoSimulation.Begin(state, DesktopActivity.StandUp, 1);
                }
                return true;
            case DesktopActivity.Climbing:
                UpdateClimb(state, travelHeight, scale);
                return true;
            case DesktopActivity.Flying:
            case DesktopActivity.Falling:
                UpdateAir(state, travelWidth, travelHeight, scale);
                return true;
            // Native wing-assisted fall/flight hands ground contact to ordinary land recovery,
            // not the unrelated 5a24f0 trip callback. Old saves may still finish LandGetUp.
            case DesktopActivity.Landing:
            case DesktopActivity.LandGetUp:
                if (!DesktopAnimationSystem.TransitionFinished(state)) return true;
                DesktopActivity after = state.AfterLanding;
                state.AfterLanding = DesktopActivity.Idle;
                if (after == DesktopActivity.EatStart)
                    DesktopChaoSimulation.Begin(state, DesktopActivity.EatSitDown, 1);
                else if (after == DesktopActivity.LieDown)
                {
                    state.SleepCycles = 0;
                    state.WakeRequested = false;
                    DesktopChaoSimulation.Begin(state, DesktopActivity.LieDown, 1);
                }
                else if (after == DesktopActivity.Sitting)
                    DesktopChaoSimulation.Begin(state, DesktopActivity.SitDown, 1);
                else DesktopChaoSimulation.Begin(state, after, after == DesktopActivity.Petting ? 180 : QuietTicks(state));
                return true;
            default: return false;
        }
    }

    public static bool StartStandingAction(DesktopChaoState state, DesktopActivity activity)
    {
        if (activity is DesktopActivity.TurnLeft or DesktopActivity.TurnRight)
        {
            DesktopChaoSimulation.Begin(state, activity, 1);
            return true;
        }
        if (activity == DesktopActivity.Climbing)
        {
            StartClimb(state);
            return true;
        }
        return false;
    }

    private static int QuietTicks(DesktopChaoState state) => DeterministicRandom.NextRange(state.Random, 45, 100) * 60;

    public static bool DeferInteraction(DesktopChaoState state, DesktopActivity after)
    {
        if (IsAirborne(state) || state.Activity is DesktopActivity.Landing or DesktopActivity.LandGetUp)
        {
            state.AfterLanding = after;
            if (state.Activity is DesktopActivity.Flying or DesktopActivity.Climbing) StartFall(state);
            return true;
        }
        if (IsCrawling(state))
        {
            RecoverCrawl(state, after);
            return true;
        }
        return false;
    }

    private static void RecoverCrawl(DesktopChaoState state, DesktopActivity after)
    {
        state.AfterCrawling = after;
        if (state.Activity != DesktopActivity.CrawlGetUp)
            DesktopChaoSimulation.Begin(state, DesktopActivity.CrawlGetUp, 1);
        ResetVelocity(state);
    }

    public static void StartFlight(DesktopChaoState state)
    {
        if (!CanFly(state)) return;
        state.AfterLanding = DesktopActivity.Idle;
        StartAir(state, true);
    }

    public static void StartFall(DesktopChaoState state) => StartAir(state, false);

    private static void StartAir(DesktopChaoState state, bool flying)
    {
        state.Chao.Transform.Rotation = ChaoLandMovementSystem.Rotation(state.Position < .5 ? 0x4000 : 0xc000);
        state.Chao.Physics.IsGrounded = false;
        ChaoActionSystem.Begin(state.Chao, flying ? ChaoActionKind.LandFlight : ChaoActionKind.LandFall, ChaoActionPhase.Enter);
        DesktopChaoSimulation.Begin(state, flying ? DesktopActivity.Flying : DesktopActivity.Falling, 1);
    }

    public static void StartClimb(DesktopChaoState state)
    {
        if (!CanClimb(state)) return;
        state.AfterLanding = DesktopActivity.Idle;
        ResetVelocity(state);
        ChaoActionSystem.Begin(state.Chao, ChaoActionKind.LandClimb, ChaoActionPhase.Enter);
        DesktopChaoSimulation.Begin(state, DesktopActivity.Climbing, 1);
    }

    private static void UpdateClimb(DesktopChaoState state, double height, double scale)
    {
        state.Chao.Navigation.Height = (float)(state.Altitude * height / scale);
        state.Chao.Navigation.LandWallFlags = 0x4800;
        state.Chao.Navigation.LandFrontWallNormalX = state.Position < .5 ? 1 : -1;
        state.Chao.Navigation.LandFrontWallNormalZ = 0;
        ChaoLandClimbSystem.Update(state.Chao, null);
        state.Altitude = Math.Clamp(state.Chao.Navigation.Height * scale / Math.Max(1, height), 0, 1);
        if (state.Altitude >= 1 || state.Chao.Behaviour.Action.Phase == ChaoActionPhase.Recover)
        {
            if (CanFly(state)) StartFlight(state);
            else StartFall(state);
        }
    }

    private static void UpdateAir(DesktopChaoState state, double width, double height, double scale)
    {
        PhysicsState physics = state.Chao.Physics;
        state.Chao.Transform.X = (float)(state.Position * width / scale);
        state.Chao.Transform.Y = 0;
        state.Chao.Navigation.Height = (float)(state.Altitude * height / scale);
        state.Chao.Navigation.TargetX = (float)((physics.VelocityX < 0 ? 0 : width) / scale);
        state.Chao.Navigation.TargetY = 0;
        float lift = ChaoLandReactionSystem.Update(state.Chao, null, state.Random);
        physics.VelocityY += ChaoLandMovementSystem.Gravity + lift;
        state.Position = Math.Clamp(state.Position + physics.VelocityX * scale / Math.Max(1, width), 0, 1);
        double altitude = state.Altitude + physics.VelocityY * scale / Math.Max(1, height);
        state.Altitude = Math.Clamp(altitude, 0, 1);
        if (state.Position is <= 0 or >= 1)
        {
            physics.VelocityX = Math.Abs(physics.VelocityX) * (state.Position <= 0 ? 1 : -1);
            state.Chao.Transform.Rotation = ChaoLandMovementSystem.Rotation(state.Position <= 0 ? 0x4000 : 0xc000);
        }
        if (altitude > 1) physics.VelocityY = Math.Min(0, physics.VelocityY);
        if (altitude <= 0 && physics.VelocityY < 0)
        {
            state.Altitude = 0;
            ResetVelocity(state);
            state.Chao.Behaviour.Action.Kind = ChaoActionKind.None;
            DesktopChaoSimulation.Begin(state, DesktopActivity.Landing, 1);
        }
    }

    private static void ResetVelocity(DesktopChaoState state)
    {
        state.Chao.Physics.VelocityX = 0;
        state.Chao.Physics.VelocityY = 0;
        state.Chao.Physics.VelocityZ = 0;
        state.Chao.Physics.IsGrounded = true;
    }
}
