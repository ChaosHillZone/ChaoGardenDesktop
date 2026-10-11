using System;
using System.Collections.Generic;
using System.Linq;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Animation;
using ChaoGarden.Simulation.Models.Rules;
using ChaoGarden.Simulation.Systems;

namespace ChaoGarden.Simulation.Desktop;

public enum DesktopPastimePhase { None, Stand, Sit, Perform, Rise, RecoverStanding }

/// <summary>Saved alongside the Chao's independent RNG and native animation clock.</summary>
public sealed class DesktopPastimeState
{
    public string Id { get; set; } = "";
    public string PreviousId { get; set; } = "";
    public DesktopPastimePhase Phase { get; set; }
    public int Ticks { get; set; }
    public int Duration { get; set; }
    public int VariantTicks { get; set; }
    public int Step { get; set; }
    public int Cooldown { get; set; }
    public bool ReturnSeated { get; set; }
}

/// <summary>Native motion entry numbers refer to the embedded SA2 table, before NativeOffset.</summary>
public sealed record DesktopPastimeDefinition(string Id, IdlePosture Posture, int Entry, int[] Variants,
    ClassroomSkills Skill = ClassroomSkills.None, Toys Toy = Toys.None,
    int MinimumTicks = 900, int MaximumTicks = 1500);

/// <summary>
/// Desktop scheduling of learned activities and owned toys. Eligibility uses the saved game flags;
/// frequency/dwell are desktop tuning. Native profile speeds, ranges and chains remain unchanged.
/// </summary>
public static class DesktopPastimeSystem
{
    private const ClassroomSkills Songs = ClassroomSkills.Song1 | ClassroomSkills.Song2 | ClassroomSkills.Song3
        | ClassroomSkills.Song4 | ClassroomSkills.Song5;
    private const ClassroomSkills Drawings = ClassroomSkills.Drawing1 | ClassroomSkills.Drawing2 | ClassroomSkills.Drawing3
        | ClassroomSkills.Drawing4 | ClassroomSkills.Drawing5;

    public static IReadOnlyList<DesktopPastimeDefinition> All { get; } = Array.AsReadOnly(new DesktopPastimeDefinition[]
    {
        new("song", IdlePosture.Standing, 289, [289, 293], Skill: Songs),
        new("gogo-dance", IdlePosture.Standing, 388, [388, 389], Skill: ClassroomSkills.GoGo),
        new("shake-dance", IdlePosture.Standing, 391, [391], Skill: ClassroomSkills.Shake),
        new("step-dance", IdlePosture.Standing, 387, [387], Skill: ClassroomSkills.Step),
        new("spin-dance", IdlePosture.Lying, 321, [322], Skill: ClassroomSkills.Spin),
        new("exercise", IdlePosture.Standing, 312, [313, 314, 316, 317], Skill: ClassroomSkills.Exercise),
        new("sit-ups", IdlePosture.Lying, 310, [311], Skill: ClassroomSkills.Exercise),
        new("draw", IdlePosture.Sitting, 323, [324], Skill: Drawings, MinimumTicks: 1200, MaximumTicks: 1800),
        new("play-bell", IdlePosture.Standing, 351, [351, 352], Skill: ClassroomSkills.Bell),
        new("play-castanets", IdlePosture.Standing, 358, [357, 359], Skill: ClassroomSkills.Castanets),
        new("play-cymbals", IdlePosture.Standing, 365, [363, 364, 366], Skill: ClassroomSkills.Cymbals),
        new("play-drum", IdlePosture.Sitting, 353, [353, 354], Skill: ClassroomSkills.Drum),
        new("play-flute", IdlePosture.Standing, 355, [355, 356], Skill: ClassroomSkills.Flute),
        new("play-maracas", IdlePosture.Standing, 347, [347, 348, 349, 350], Skill: ClassroomSkills.Maracas),
        new("play-trumpet", IdlePosture.Standing, 360, [360, 361, 362], Skill: ClassroomSkills.Trumpet),
        new("play-tambourine", IdlePosture.Standing, 367, [368], Skill: ClassroomSkills.Tambourine),
        new("toy-rattle", IdlePosture.Sitting, 330, [330], Toy: Toys.Rattle),
        new("toy-car", IdlePosture.Sitting, 335, [335, 336], Toy: Toys.Car),
        new("toy-book", IdlePosture.Sitting, 337, [338, 339], Toy: Toys.PictureBook),
        new("toy-bubbles", IdlePosture.Standing, 326, [326], Toy: Toys.BubbleWand),
        new("toy-broom", IdlePosture.Standing, 334, [334], Toy: Toys.Broomstick),
        new("toy-watering-can", IdlePosture.Standing, 235, [236, 237], Toy: Toys.WateringCan)
    });

    public static DesktopPastimeDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);
    public static bool Active(DesktopChaoState state) => state.Activity == DesktopActivity.GameAnimation
        && state.Pastime.Phase != DesktopPastimePhase.None && Find(state.Pastime.Id) != null;

    public static bool Eligible(DesktopChaoState state, DesktopPastimeDefinition definition) =>
        (definition.Skill == ClassroomSkills.None || (state.Chao.Skills.ClassroomSkills & definition.Skill) != 0)
        && (definition.Toy == Toys.None || (state.Chao.Skills.Toys & definition.Toy) != 0);

    public static bool TrySchedule(DesktopChaoState state, bool seated)
    {
        if (state.Pastime.Cooldown > 0 || !CanStart(state) || !All.Any(d => Eligible(state, d))) return false;
        // Evaluate only at a quiet session boundary, never every rendered frame.
        if (DeterministicRandom.NextRange(state.Random, 0, 100) >= 35) return false;
        DesktopPastimeDefinition[] candidates = All.Where(d => Eligible(state, d) && d.Id != state.Pastime.PreviousId).ToArray();
        if (candidates.Length == 0) candidates = All.Where(d => Eligible(state, d)).ToArray();
        DesktopPastimeDefinition chosen = candidates[DeterministicRandom.NextRange(state.Random, 0, candidates.Length)];
        return TryStart(state, chosen.Id, seated);
    }

    private static bool CanStart(DesktopChaoState state) => state.Activity is DesktopActivity.Idle or DesktopActivity.Sitting
        && state.Altitude == 0 && !DesktopChaoSimulation.IsFeeding(state)
        && state.Chao.Emotions.NeedDrives.SleepPressure < 8000
        && state.Chao.Emotions.NeedDrives.FoodHunger < 8000;

    public static bool TryStart(DesktopChaoState state, string id, bool seated)
    {
        DesktopPastimeDefinition? definition = Find(id);
        if (definition == null || !CanStart(state) || !Eligible(state, definition)) return false;
        DesktopAnimationSystem.EnsureInitialized(state);
        DesktopPastimeState activity = state.Pastime;
        activity.Id = activity.PreviousId = id;
        activity.ReturnSeated = seated;
        activity.Duration = DeterministicRandom.NextRange(state.Random, definition.MinimumTicks, definition.MaximumTicks + 1);
        activity.Step = 0;
        state.ActivityTicks = 0;
        state.ActivityDuration = 7200;
        if (seated && definition.Posture != IdlePosture.Sitting)
            Link(state, (int)ChaoAnimation.LandStandFromSit, DesktopPastimePhase.Stand);
        else if (!seated && definition.Posture == IdlePosture.Sitting)
            Link(state, (int)ChaoAnimation.RestSitDown, DesktopPastimePhase.Sit);
        else Perform(state, definition);
        return true;
    }

    public static void Update(DesktopChaoState state)
    {
        DesktopPastimeState activity = state.Pastime;
        DesktopPastimeDefinition definition = Find(activity.Id)!;
        AnimationPlaybackState playback = state.Chao.Animation;
        activity.Ticks++;
        if (activity.Phase is DesktopPastimePhase.Stand or DesktopPastimePhase.Sit)
        {
            if (!playback.IsPlaying && !playback.IsBlending) Perform(state, definition);
            return;
        }
        if (activity.Phase == DesktopPastimePhase.Rise)
        {
            if (playback.CurrentEntryIndex == (int)ChaoAnimation.RestGetUpSeated && !playback.IsBlending)
                RecoverSeated(state);
            return;
        }
        if (activity.Phase == DesktopPastimePhase.RecoverStanding)
        {
            if (!playback.IsPlaying && !playback.IsBlending) Finish(state);
            return;
        }
        activity.VariantTicks++;
        bool boundary = !playback.IsPlaying || state.Chao.Behaviour.Sleep.AnimationEnded
            && playback.ActiveLoopMode is LoopProperty.PlayOnce or LoopProperty.Loop or LoopProperty.PingPong
                or LoopProperty.HoldLastFrame or LoopProperty.TransitionBlend;
        bool interrupted = !Eligible(state, definition) || state.Chao.Emotions.NeedDrives.FoodHunger >= 8000
            || state.Chao.Emotions.NeedDrives.SleepPressure >= 8000;
        if ((activity.Ticks >= activity.Duration || interrupted) && boundary || activity.Ticks >= 3600)
        {
            if (definition.Posture == IdlePosture.Lying)
                Link(state, (int)ChaoAnimation.RestGetUp, DesktopPastimePhase.Rise);
            else if (definition.Posture == IdlePosture.Sitting) RecoverSeated(state);
            else if (activity.ReturnSeated)
            {
                // Return through a real sit-down, without briefly showing the performance props again.
                Cancel(state);
                DesktopChaoSimulation.Begin(state, DesktopActivity.SitDown, 1);
            }
            else Finish(state);
            return;
        }
        if (boundary && (!playback.IsPlaying || activity.VariantTicks >= 240))
        {
            if (activity.VariantTicks >= 240)
            {
                activity.Step = (activity.Step + 1) % definition.Variants.Length;
                activity.VariantTicks = 0;
            }
            int next = DesktopAnimationCatalog.NativeOffset + definition.Variants[activity.Step];
            // Existing native loops keep their clock; held gestures link into the next phrase.
            if (!playback.IsPlaying || playback.CurrentEntryIndex != next) LinkNative(state, next);
        }
    }

    private static void Perform(DesktopChaoState state, DesktopPastimeDefinition definition)
    {
        Link(state, DesktopAnimationCatalog.NativeOffset + definition.Entry, DesktopPastimePhase.Perform);
        state.CatalogReturnSeated = definition.Posture != IdlePosture.Standing;
        state.Pastime.VariantTicks = 0;
    }

    private static void RecoverSeated(DesktopChaoState state)
    {
        if (state.Pastime.ReturnSeated) Finish(state);
        else Link(state, (int)ChaoAnimation.LandStandFromSit, DesktopPastimePhase.RecoverStanding);
    }

    private static void Finish(DesktopChaoState state)
    {
        bool seated = state.Pastime.ReturnSeated;
        Cancel(state);
        if (seated)
        {
            state.RestTicksRemaining = Math.Max(state.RestTicksRemaining, 1800);
            DesktopChaoSimulation.Begin(state, DesktopActivity.Sitting, 1800);
        }
        else DesktopChaoSimulation.Begin(state, DesktopActivity.Idle, 2700);
    }

    private static void Link(DesktopChaoState state, int entry, DesktopPastimePhase phase)
    {
        state.Activity = DesktopActivity.GameAnimation;
        state.Pastime.Phase = phase;
        state.Pastime.Ticks = 0;
        state.CatalogActivityId = state.Pastime.Id;
        LinkNative(state, entry);
    }

    private static void LinkNative(DesktopChaoState state, int entry)
    {
        DesktopAnimationSystem.EnsureInitialized(state);
        state.CatalogClip = DesktopAnimationCatalog.Table[entry].Motion;
        AnimationSystem.SetAnimation(state.Chao.Animation, DesktopAnimationCatalog.Table, entry);
        state.Chao.Animation.BlendRate = 1f / 15;
        state.Chao.Behaviour.Sleep.SelfLoopBlend = false;
        state.MotionInitialized = true;
    }

    public static bool InterruptForCare(DesktopChaoState state, DesktopActivity next)
    {
        if (!Active(state) || Find(state.Pastime.Id)!.Posture != IdlePosture.Lying
            || state.Pastime.Phase != DesktopPastimePhase.Perform) return false;
        Cancel(state);
        state.AfterCrawling = next;
        DesktopChaoSimulation.Begin(state, DesktopActivity.CrawlGetUp, 1);
        return true;
    }

    public static void Cancel(DesktopChaoState state)
    {
        if (state.Pastime.Phase == DesktopPastimePhase.None) return;
        state.Pastime.Id = "";
        state.Pastime.Phase = DesktopPastimePhase.None;
        state.Pastime.Cooldown = DeterministicRandom.NextRange(state.Random, 90, 181) * 60;
    }

    public static MouthType? Mouth(DesktopChaoState state) => Active(state)
        && state.Pastime.Phase == DesktopPastimePhase.Perform && state.Pastime.Id == "song"
        ? state.Pastime.Ticks % 30 < 18 ? MouthType.Open : MouthType.OpenSmile : null;
}
