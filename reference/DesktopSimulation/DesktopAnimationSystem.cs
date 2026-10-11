using System;
using System.Collections.Generic;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Animation;
using ChaoGarden.Simulation.Systems;

namespace ChaoGarden.Simulation.Desktop;

/// <summary>Native motion-table playback for the desktop's smaller behaviour graph.</summary>
public static class DesktopAnimationSystem
{
    // Native pet comfort entry252 is an inclusive forward loop (mode1), not ping-pong.
    private static MotionTableEntry[] Table => DesktopAnimationCatalog.Table;

    public static void EnsureInitialized(DesktopChaoState state)
    {
        if (state.MotionInitialized && !(state.Activity == DesktopActivity.Petting
            && state.Chao.Animation.CurrentEntryIndex == (int)ChaoAnimation.PetHappy)) return;
        AnimationSystem.ResetAnimation(state.Chao.Animation, Table, state.Activity == DesktopActivity.GameAnimation
            ? DesktopAnimationCatalog.NativeOffset + (DesktopAnimationCatalog.Find(state.CatalogClip)?.NativeIndex ?? 0)
            : (int)Motion(state, state.Activity));
        state.MotionInitialized = true;
    }

    public static void Link(DesktopChaoState state, DesktopActivity next)
    {
        EnsureInitialized(state);
        AnimationPlaybackState playback = state.Chao.Animation;
        ChaoAnimation motion = Motion(state, next);
        // Crawling turns keep their gait clock; native upright turns restart the walk profile.
        if (playback.CurrentEntryIndex == (int)motion && (motion == ChaoAnimation.LandCrawlLoop
            || next is not DesktopActivity.TurnLeft and not DesktopActivity.TurnRight)) return;
        AnimationSystem.SetAnimation(playback, Table, (int)motion);
        // Native ordinary SetMotion links hold the two poses for 15 ticks. Rest's
        // forced links use 40; eating's satisfied rest explicitly requests 60.
        int duration = next switch
        {
            DesktopActivity.CrawlSitDown or DesktopActivity.CrawlGetUp or DesktopActivity.SitDown or DesktopActivity.EatSitDown or DesktopActivity.Sitting
                or DesktopActivity.SittingLook or DesktopActivity.SittingScratch or DesktopActivity.WakeUp => 40,
            DesktopActivity.Satisfied => 60,
            _ => 15
        };
        playback.BlendRate = 1f / duration;
        state.Chao.Behaviour.Sleep.SelfLoopBlend = false;
    }

    public static void Advance(DesktopChaoState state)
    {
        EnsureInitialized(state);
        // This implements the native held-pose links, signed rates and mode 0-7
        // chaining, including sleep, wake-up and eating-resume successors.
        ChaoSleepSystem.Advance(state.Chao, Table);
    }

    public static bool TransitionFinished(DesktopChaoState state)
    {
        AnimationPlaybackState playback = state.Chao.Animation;
        if (playback.IsBlending) return false;
        return state.Activity switch
        {
            DesktopActivity.FruitPickup => playback.CurrentEntryIndex == (int)ChaoAnimation.PickupFruit190,
            DesktopActivity.Landing => true,
            DesktopActivity.CrawlStart => playback.CurrentEntryIndex == (int)ChaoAnimation.LandCrawlLoop,
            DesktopActivity.SleepStart => playback.CurrentEntryIndex == (int)ChaoAnimation.SleepPose19,
            DesktopActivity.RollToBelly => playback.CurrentEntryIndex == (int)ChaoAnimation.SleepPose53,
            DesktopActivity.WakeUp or DesktopActivity.CrawlGetUp => playback.CurrentEntryIndex == (int)ChaoAnimation.RestGetUpSeated,
            DesktopActivity.EatResume => playback.CurrentEntryIndex == (int)ChaoAnimation.EatResumeLoop,
            DesktopActivity.CrawlSitDown or DesktopActivity.LandGetUp
                or DesktopActivity.SitDown or DesktopActivity.EatSitDown or DesktopActivity.StandUp
                or DesktopActivity.LieDown or DesktopActivity.RollToBack or DesktopActivity.EatStart => !playback.IsPlaying,
            _ => true
        };
    }

    private static ChaoAnimation Motion(DesktopChaoState state, DesktopActivity activity) => activity switch
    {
        DesktopActivity.WalkLeft or DesktopActivity.WalkRight => state.TravelGait == ChaoAnimation.LandCrawlStart
            ? ChaoAnimation.LandCrawlLoop : state.TravelGait,
        DesktopActivity.TurnLeft or DesktopActivity.TurnRight => state.TravelGait == ChaoAnimation.LandCrawlStart
            ? ChaoAnimation.LandCrawlLoop : ChaoAnimation.LandWalk,
        DesktopActivity.CrawlSitDown => ChaoAnimation.RestSitDown,
        DesktopActivity.CrawlStart => ChaoAnimation.LandCrawlStart,
        DesktopActivity.CrawlGetUp => ChaoAnimation.RestGetUp,
        DesktopActivity.Held => ChaoAnimation.HeldPose257,
        DesktopActivity.Flying => ChaoAnimation.LandFlight,
        DesktopActivity.Climbing => ChaoAnimation.Climb,
        DesktopActivity.Falling => ChaoAnimation.LandFallStart,
        DesktopActivity.Landing => ChaoAnimation.DrowsyPose0,
        DesktopActivity.FruitPickup => ChaoAnimation.PickupFruit188,
        DesktopActivity.LandGetUp => ChaoAnimation.LandTripGetUp,
        DesktopActivity.SitDown or DesktopActivity.EatSitDown => ChaoAnimation.RestSitDown,
        DesktopActivity.StandUp => ChaoAnimation.LandStandFromSit,
        DesktopActivity.Sitting => ChaoAnimation.RestPose62,
        DesktopActivity.SittingLook => ChaoAnimation.RestPose67,
        DesktopActivity.SittingScratch => ChaoAnimation.RestPose71,
        DesktopActivity.LieDown => ChaoAnimation.SleepPose37,
        DesktopActivity.SleepStart => ChaoAnimation.SleepPose18,
        DesktopActivity.Sleeping => ChaoAnimation.SleepPose19,
        DesktopActivity.RollToBelly => ChaoAnimation.SleepPose52,
        DesktopActivity.SleepBelly => ChaoAnimation.SleepPose53,
        DesktopActivity.RollToBack => ChaoAnimation.SleepPose54,
        DesktopActivity.WakeUp => ChaoAnimation.RestGetUp,
        DesktopActivity.Petting => ChaoAnimation.PetComfort252,
        DesktopActivity.Satisfied => ChaoAnimation.SitEatEnd,
        DesktopActivity.EatStart => ChaoAnimation.SitEatStart,
        DesktopActivity.Eating => ChaoAnimation.SitEatLoop,
        DesktopActivity.EatFast => ChaoAnimation.EatLoop,
        DesktopActivity.EatLook => ChaoAnimation.EatLookLeft,
        DesktopActivity.EatResume => ChaoAnimation.EatResume,
        _ => ChaoAnimation.DrowsyPose0
    };
}
