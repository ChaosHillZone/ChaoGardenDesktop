using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Animation;
using ChaoGarden.Simulation.Systems;
using ChaoGarden.Simulation.Models.Rules;

namespace ChaoGarden.Simulation.Desktop;

public sealed record DesktopAnimationDefinition(string Clip, string Category, string Label, int NativeIndex);

/// <summary>279 clips and all 622 original SA2 profiles. Metadata from al_motion_table/info.ini;
/// motion symbols resolved from the corresponding SAANIM names. No extracted game assets are embedded.</summary>
public static class DesktopAnimationCatalog
{
    public const int NativeOffset = 1024;
    private sealed class CatalogData
    {
        public MotionTableEntry[] Motions { get; set; } = [];
        public DesktopAnimationDefinition[] Animations { get; set; } = [];
    }
    private static readonly CatalogData Data = Load();
    public static IReadOnlyList<DesktopAnimationDefinition> All => Data.Animations;
    public static readonly MotionTableEntry[] Table = BuildTable();
    public static DesktopAnimationDefinition? Find(string clip) => All.FirstOrDefault(a => a.Clip == clip);
    private static CatalogData Load()
    {
        using var stream = typeof(DesktopAnimationCatalog).Assembly.GetManifestResourceStream(
            "ChaoGarden.Simulation.Desktop.NativeDesktopAnimations.json")!;
        return JsonSerializer.Deserialize<CatalogData>(stream)!;
    }
    private static MotionTableEntry[] BuildTable()
    {
        MotionTableEntry[] core = CoreChaoMotionTable.Build(new Dictionary<string, ObjectAnimationClip>());
        if (core.Length > NativeOffset) throw new InvalidOperationException("Desktop motion index collision.");
        MotionTableEntry[] result = new MotionTableEntry[NativeOffset + Data.Motions.Length];
        Array.Copy(core, result, core.Length);
        for (int i = core.Length; i < NativeOffset; i++) result[i] = core[0];
        for (int i = 0; i < Data.Motions.Length; i++)
        {
            MotionTableEntry entry = Data.Motions[i];
            if (entry.LoopProperty is LoopProperty.PlayThenChainHold or LoopProperty.PlayThenChainReset or LoopProperty.PlayThenChainResume)
                entry.NextAnimationIndex += NativeOffset;
            else entry.NextAnimationIndex = -1;
            result[NativeOffset + i] = entry;
        }
        return result;
    }

    public static bool Play(DesktopChaoState state, string clip, int duration = 900, bool nativeLink = false)
    {
        DesktopAnimationDefinition? animation = Find(clip);
        if (animation == null || DesktopChaoSimulation.IsFeeding(state) || DesktopChaoSimulation.IsSleeping(state)
            || DesktopLocomotionSystem.IsAirborne(state) || state.Altitude > 0) return false;
        DesktopPastimeSystem.Cancel(state);
        state.CatalogClip = clip;
        state.CatalogReturnSeated = false;
        state.CatalogActivityId = "";
        state.Activity = DesktopActivity.GameAnimation;
        state.ActivityTicks = 0;
        state.ActivityDuration = Math.Clamp(duration, 120, 3600);
        state.MotionInitialized = true;
        state.Chao.Behaviour.Sleep.SelfLoopBlend = false;
        // This explicit preview starts the requested profile; its subsequent links are native.
        if (nativeLink)
        {
            AnimationSystem.SetAnimation(state.Chao.Animation, Table, NativeOffset + animation.NativeIndex);
            state.Chao.Animation.BlendRate = 1f / 15;
        }
        else AnimationSystem.ResetAnimation(state.Chao.Animation, Table, NativeOffset + animation.NativeIndex);
        return true;
    }


    /// <summary>Reuse the engine's personality, health, learned-skill and animal behavior gates.
    /// Desktop timing remains quiet; ambient motions are occasional additions to a long rest.</summary>
    public static bool TryAmbient(DesktopChaoState state, bool seated)
    {
        var candidates = ChaoIdleCatalog.All.Where(d => d.Context != IdleContext.Learned && d.Posture == (seated ? IdlePosture.Sitting : IdlePosture.Standing))
            .Select(d => (Definition: d, Weight: ChaoIdleSystem.SelectionWeight(state.Chao, d, BehaviourType.Idle),
                Clip: Table[(int)d.Animation].Motion))
            .Where(d => d.Weight > 0 && Find(d.Clip) != null).ToArray();
        int total = candidates.Sum(d => d.Weight);
        if (total == 0) return false;
        int roll = DeterministicRandom.NextRange(state.Random, 0, total);
        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll >= 0) continue;
            ChaoIdleDefinition definition = candidate.Definition;
            if (!Play(state, candidate.Clip, DeterministicRandom.NextRange(state.Random, definition.MinimumTicks, definition.MaximumTicks + 1), nativeLink: true)) return false;
            state.CatalogReturnSeated = seated;
            state.CatalogActivityId = definition.Id;
            return true;
        }
        return false;
    }
}
