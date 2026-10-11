using System;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.Simulation.Models.Rules;
using ChaoGarden.Simulation.Systems;

namespace ChaoGarden.Simulation.Desktop;

/// <summary>Fixed 60 Hz desktop behaviour. Care changes needs/stats, never alignment or appearance.</summary>
public static class DesktopChaoSimulation
{
    private static readonly StatGrowthTable Growth = DefaultStatGrowthTable.Create();
    public const int BiteDuration = PetFeedInteractionSystem.FeedingPhase2Ticks;
    public const int LookColumns = 7;
    public const int LookRows = 5;

    public static bool IsFeeding(DesktopChaoState state) => state.RemainingBites > 0;
    public static bool IsSleeping(DesktopChaoState state) => state.Activity is DesktopActivity.LieDown
        or DesktopActivity.SleepStart or DesktopActivity.Sleeping or DesktopActivity.RollToBelly
        or DesktopActivity.SleepBelly or DesktopActivity.RollToBack or DesktopActivity.WakeUp;
    private static bool IsSeated(DesktopChaoState state) => state.Activity is DesktopActivity.Sitting
        or DesktopActivity.SittingLook or DesktopActivity.SittingScratch or DesktopActivity.Satisfied
        || state.Activity == DesktopActivity.GameAnimation && state.CatalogReturnSeated;

    public static void Tick(DesktopChaoState state, double travelWidth, double travelHeight = 800, bool allowClimb = true)
    {
        DesktopLocomotionSystem.PreparePlayback(state);
        DesktopAnimationSystem.Advance(state);
        if (state.Pastime.Cooldown > 0) state.Pastime.Cooldown--;
        state.TotalTicks++;
        state.ActivityTicks++;
        state.Chao.SimulationFrames++;
        if (state.Activity == DesktopActivity.Held) return;
        if (state.TotalTicks % SimulationTiming.PerChaoServiceFrames == 0)
        {
            NeedsSystem.AdvanceByteEmotionService(state.Chao);
            NeedsSystem.AdvanceNeedDriveService(state.Chao, state.Random);
        }
        if (state.TotalTicks % SimulationTiming.BehaviouralFrames == 0)
        {
            NeedsSystem.UpdateBehavioralStates(state.Chao);
        }
        NeedDrives drives = state.Chao.Emotions.NeedDrives;
        if (IsSeated(state))
        {
            state.RestTicksRemaining = Math.Max(0, state.RestTicksRemaining - 1);
            if (state.TotalTicks % 60 == 0) drives.EnergyPool = (ushort)Math.Max(0, drives.EnergyPool - 80);
        }
        if (IsSleeping(state) && state.TotalTicks % 60 == 0)
        {
            drives.SleepPressure = (ushort)Math.Max(0, drives.SleepPressure - 250);
            drives.SleepDepth = (ushort)Math.Max(0, drives.SleepDepth - 250);
            drives.EnergyPool = (ushort)Math.Max(0, drives.EnergyPool - 150);
        }
        bool movementOwnsCompletion = DesktopLocomotionSystem.Update(state, travelWidth, travelHeight, allowClimb);
        switch (state.Activity)
        {
            case DesktopActivity.Eating:
            case DesktopActivity.EatFast:
                if (!state.Chao.Animation.IsBlending && ++state.BiteTicks >= BiteDuration)
                {
                    state.BiteTicks = 0;
                    state.RemainingBites--;
                    StatGrowthSystem.ApplyFruitBite(state.Chao, state.EatingFruitType,
                        state.RemainingBites == 0, Growth, state.Random);
                    drives.FoodHunger = (ushort)Math.Max(0, drives.FoodHunger - 2400);
                    if (state.RemainingBites == 0)
                    {
                        state.RestTicksRemaining = Seconds(state, 60, 120);
                        Begin(state, DesktopActivity.Satisfied, 480);
                        return;
                    }
                }
                break;
        }
        if (movementOwnsCompletion) return;
        if (DesktopPastimeSystem.Active(state))
        {
            DesktopPastimeSystem.Update(state);
            return;
        }
        if (state.Activity == DesktopActivity.GameAnimation)
        {
            if ((!state.Chao.Animation.IsPlaying || state.ActivityTicks >= state.ActivityDuration
                && state.Chao.Behaviour.Sleep.AnimationEnded) || state.ActivityTicks >= 7200)
            {
                // Finish on a native boundary, not by repeatedly restarting a one-shot clip.
                if (state.CatalogReturnSeated) SeatedIdle(state);
                else Begin(state, DesktopActivity.Idle, Seconds(state, 45, 120));
            }
            return;
        }
        if (state.ActivityTicks < state.ActivityDuration || !DesktopAnimationSystem.TransitionFinished(state)) return;
        if ((state.Activity == DesktopActivity.Idle || state.Activity == DesktopActivity.Sitting && state.RestTicksRemaining > 0)
            && DesktopPastimeSystem.TrySchedule(state, state.Activity == DesktopActivity.Sitting)) return;
        switch (state.Activity)
        {
            case DesktopActivity.SitDown:
                state.RestTicksRemaining = Seconds(state, 60, 180);
                SeatedIdle(state); return;
            case DesktopActivity.Sitting:
                if (state.RestTicksRemaining <= 0) { Stand(state, DesktopActivity.Idle); return; }
                if (DeterministicRandom.NextRange(state.Random, 0, 5) == 0 && DesktopAnimationCatalog.TryAmbient(state, true)) return;
                bool scratch = DeterministicRandom.NextRange(state.Random, 0, 3) == 0;
                Begin(state, scratch ? DesktopActivity.SittingScratch : DesktopActivity.SittingLook,
                    scratch ? DesktopMotions.All["sit-scratch"].Frames * 2 : DesktopMotions.All["sit-look"].Frames);
                return;
            case DesktopActivity.SittingLook:
            case DesktopActivity.SittingScratch:
            case DesktopActivity.Satisfied:
                SeatedIdle(state); return;
            case DesktopActivity.StandUp:
                DesktopActivity next = state.AfterStanding;
                if (DesktopLocomotionSystem.StartStandingAction(state, next)) return;
                if (next == DesktopActivity.LieDown) { StartSleep(state); return; }
                Begin(state, next, next == DesktopActivity.Petting ? 180 : Seconds(state, 40, 100)); return;
            case DesktopActivity.LieDown: Motion(state, DesktopActivity.SleepStart, "sleep-start"); return;
            case DesktopActivity.SleepStart: Begin(state, DesktopActivity.Sleeping, state.WakeRequested ? 1 : Seconds(state, 35, 60)); return;
            case DesktopActivity.Sleeping:
                if (state.WakeRequested || state.SleepCycles >= 2)
                {
                    Motion(state, DesktopActivity.WakeUp, "wake-up"); return;
                }
                Motion(state, DesktopActivity.RollToBelly, "roll-belly"); return;
            case DesktopActivity.RollToBelly:
                Begin(state, DesktopActivity.SleepBelly, state.WakeRequested ? 1 : Seconds(state, 35, 60)); return;
            case DesktopActivity.SleepBelly: Motion(state, DesktopActivity.RollToBack, "roll-back"); return;
            case DesktopActivity.RollToBack:
                state.SleepCycles++;
                if (state.WakeRequested) { Motion(state, DesktopActivity.WakeUp, "wake-up"); return; }
                Begin(state, DesktopActivity.Sleeping, state.WakeRequested ? 1 : Seconds(state, 35, 60)); return;
            case DesktopActivity.WakeUp:
                state.WakeRequested = false;
                if (IsFeeding(state)) { Motion(state, DesktopActivity.EatStart, "eat-start"); return; }
                state.RestTicksRemaining = Seconds(state, 60, 120);
                SeatedIdle(state); return;
            case DesktopActivity.Petting: Begin(state, DesktopActivity.Idle, Seconds(state, 45, 100)); return;
            case DesktopActivity.FruitPickup: Motion(state, DesktopActivity.EatSitDown, "sit-down"); return;
            case DesktopActivity.EatSitDown: Motion(state, DesktopActivity.EatStart, "eat-start"); return;
            case DesktopActivity.EatStart:
            case DesktopActivity.EatResume:
                Begin(state, DesktopActivity.Eating, DesktopMotions.All["eat-slow"].Frames * 4); return;
            case DesktopActivity.Eating:
                Begin(state, DesktopActivity.EatFast, DesktopMotions.All["eat-fast"].Frames * 2); return;
            case DesktopActivity.EatFast: Motion(state, DesktopActivity.EatLook, "eat-look"); return;
            case DesktopActivity.EatLook: Motion(state, DesktopActivity.EatResume, "eat-resume"); return;
        }
        // Long quiet sessions, with sleep a rare choice rather than a ten-second rotation.
        int choice = DeterministicRandom.NextRange(state.Random, 0, 100);
        if (choice < 4 || drives.SleepPressure > 8000) Sleep(state);
        else if (choice < 40) Sit(state);
        else if (choice < 48 && DesktopLocomotionSystem.CanFly(state)) DesktopLocomotionSystem.StartFlight(state);
        else if (choice < 68) DesktopLocomotionSystem.StartStroll(state,
            state.Position > .5 ? DesktopActivity.WalkLeft : DesktopActivity.WalkRight, Seconds(state, 35, 80));
        else if (choice < 75)
        {
            if (!DesktopAnimationCatalog.TryAmbient(state, false))
                Begin(state, DesktopActivity.Idle, Seconds(state, 45, 120));
        }
        else Begin(state, DesktopActivity.Idle, Seconds(state, 45, 120));
    }

    private static int Seconds(DesktopChaoState state, int min, int max) => DeterministicRandom.NextRange(state.Random, min, max) * 60;
    private static void SeatedIdle(DesktopChaoState state) => Begin(state, DesktopActivity.Sitting,
        Math.Max(1, Math.Min(state.RestTicksRemaining, Seconds(state, 25, 50))));
    private static void Motion(DesktopChaoState state, DesktopActivity activity, string motion) => Begin(state, activity, DesktopMotions.All[motion].Frames);
    private static void Stand(DesktopChaoState state, DesktopActivity next)
    {
        state.AfterStanding = next;
        Motion(state, DesktopActivity.StandUp, "stand-up");
    }

    public static bool Pet(DesktopChaoState state)
    {
        if (IsFeeding(state) || IsSleeping(state)
            || state.Activity == DesktopActivity.CrawlGetUp && state.AfterCrawling == DesktopActivity.Petting
            || state.AfterLanding == DesktopActivity.Petting && (DesktopLocomotionSystem.IsAirborne(state)
                || state.Activity is DesktopActivity.Landing or DesktopActivity.LandGetUp)
            || state.Activity == DesktopActivity.Petting
            || state.Activity == DesktopActivity.StandUp && state.AfterStanding == DesktopActivity.Petting) return false;
        RaisingQualitySystem.Adjust(state.Chao, RaisingQualitySystem.PetCare);
        if (DesktopPastimeSystem.InterruptForCare(state, DesktopActivity.Petting)) return true;
        if (DesktopLocomotionSystem.DeferInteraction(state, DesktopActivity.Petting)) return true;
        if (IsSeated(state)) Stand(state, DesktopActivity.Petting);
        else Begin(state, DesktopActivity.Petting, 180);
        return true;
    }
    public static bool Feed(DesktopChaoState state, FruitType fruit = FruitType.ChaoGardenFruit, int bites = 4, Guid? fruitId = null)
    {
        if (!Enum.IsDefined(fruit) || bites is < 1 or > 4) throw new ArgumentException("Invalid fruit portion.");
        if (IsFeeding(state)) return false;
        // Acquiring loose food is not a player handoff; keep the desktop's direct-offer adapter for old callers.
        if (fruitId == null) RaisingQualitySystem.Adjust(state.Chao, RaisingQualitySystem.FeedCare);
        state.EatingFruitType = fruit;
        state.LastAcquiredFruitId = fruitId;
        state.TargetFruitId = null;
        state.ForageTicks = 0;
        state.RemainingBites = bites;
        state.BiteTicks = 0;
        if (DesktopPastimeSystem.InterruptForCare(state, DesktopActivity.EatStart)) return true;
        if (DesktopLocomotionSystem.DeferInteraction(state, DesktopActivity.EatStart)) return true;
        if (IsSleeping(state)) Wake(state);
        else if (IsSeated(state)) Motion(state, DesktopActivity.EatStart, "eat-start");
        else if (fruitId != null) Motion(state, DesktopActivity.FruitPickup, "eat-pickup");
        else Motion(state, DesktopActivity.EatSitDown, "sit-down");
        return true;
    }

    public static void ApproachFood(DesktopChaoState state, DesktopActivity direction)
    {
        if (IsSeated(state)) { Stand(state, DesktopActivity.Idle); return; }
        if (state.Activity == DesktopActivity.Idle) DesktopLocomotionSystem.StartStroll(state, direction, 1800);
        else if (DesktopLocomotionSystem.IsMoving(state) && state.Activity != direction)
        {
            state.TravelDirection = direction;
            state.TravelTicksRemaining = 1800;
            Begin(state, direction == DesktopActivity.WalkLeft ? DesktopActivity.TurnLeft : DesktopActivity.TurnRight, 1);
        }
    }
    public static void Sit(DesktopChaoState state)
    {
        if (IsFeeding(state)) return;
        if (DesktopLocomotionSystem.DeferInteraction(state, DesktopActivity.Sitting)) return;
        if (IsSleeping(state)) { Wake(state); return; }
        if (IsSeated(state)) return;
        Motion(state, DesktopActivity.SitDown, "sit-down");
    }
    public static void Sleep(DesktopChaoState state)
    {
        if (IsFeeding(state) || IsSleeping(state)) return;
        if (DesktopLocomotionSystem.DeferInteraction(state, DesktopActivity.LieDown)) return;
        if (IsSeated(state)) Stand(state, DesktopActivity.LieDown);
        else StartSleep(state);
    }
    private static void StartSleep(DesktopChaoState state)
    {
        state.SleepCycles = 0;
        state.WakeRequested = false;
        Motion(state, DesktopActivity.LieDown, "lie-down");
    }
    public static void Wake(DesktopChaoState state)
    {
        if (!IsSleeping(state)) return;
        state.WakeRequested = true;
        if (state.Activity == DesktopActivity.SleepBelly) Motion(state, DesktopActivity.RollToBack, "roll-back");
        else if (state.Activity == DesktopActivity.Sleeping) Motion(state, DesktopActivity.WakeUp, "wake-up");
    }
    public static void Begin(DesktopChaoState state, DesktopActivity activity, int ticks)
    {
        DesktopPastimeSystem.Cancel(state);
        DesktopAnimationSystem.Link(state, activity);
        state.Activity = activity;
        state.ActivityTicks = 0;
        state.ActivityDuration = ticks;
    }

    public static string Animation(DesktopChaoState state)
    {
        string name = state.Activity switch
        {
            DesktopActivity.WalkLeft or DesktopActivity.WalkRight => (state.TravelGait switch
            {
                ChaoAnimation.LandCrawlStart => "crawl",
                ChaoAnimation.LandRun => "run",
                ChaoAnimation.LandRunFast => "run-fast",
                _ => "walk"
            }) + (state.Activity == DesktopActivity.WalkLeft ? "-left" : "-right"),
            DesktopActivity.TurnLeft => state.TravelGait == ChaoAnimation.LandCrawlStart ? "crawl-left" : "walk-left",
            DesktopActivity.TurnRight => state.TravelGait == ChaoAnimation.LandCrawlStart ? "crawl-right" : "walk-right",
            DesktopActivity.CrawlSitDown => "sit-down",
            DesktopActivity.CrawlStart => "crawl-start",
            DesktopActivity.CrawlGetUp => "wake-up",
            DesktopActivity.Flying => "flying",
            DesktopActivity.Climbing => "climbing",
            DesktopActivity.Falling => "falling",
            DesktopActivity.Landing => "landing",
            DesktopActivity.LandGetUp => "land-get-up",
            DesktopActivity.SitDown => "sit-down",
            DesktopActivity.EatSitDown => "eat-sit-down",
            DesktopActivity.StandUp => "stand-up",
            DesktopActivity.Sitting => "sitting",
            DesktopActivity.SittingLook => "sit-look",
            DesktopActivity.SittingScratch => "sit-scratch",
            DesktopActivity.LieDown => "lie-down",
            DesktopActivity.SleepStart => "sleep-start",
            DesktopActivity.Sleeping => "sleeping",
            DesktopActivity.RollToBelly => "roll-belly",
            DesktopActivity.SleepBelly => "sleep-belly",
            DesktopActivity.RollToBack => "roll-back",
            DesktopActivity.WakeUp => "wake-up",
            DesktopActivity.Petting => "petting",
            DesktopActivity.Satisfied => "satisfied",
            DesktopActivity.FruitPickup => "eat-pickup",
            DesktopActivity.EatStart => "eat-start",
            DesktopActivity.Eating => "eat-slow",
            DesktopActivity.EatFast => "eat-fast",
            DesktopActivity.EatLook => "eat-look",
            DesktopActivity.EatResume => "eat-resume",
            _ => "idle"
        };
        return name.StartsWith("eat-", StringComparison.Ordinal) ? name + "-" + Math.Clamp(state.RemainingBites, 1, 4) : name;
    }

    public static string LookDirection(double cursorX, double cursorY, double headX, double headY, bool follow)
    {
        if (!follow) return "center";
        int x = (int)Math.Round(Math.Clamp((cursorX - headX) / 100, -3, 3));
        int y = (int)Math.Round(Math.Clamp((cursorY - headY) / 110, -2, 2));
        return DirectionName(x, y);
    }
    public static string DirectionName(int x, int y) => x == 0 && y == 0 ? "center" : $"look-{x + 3}-{y + 2}";
}
