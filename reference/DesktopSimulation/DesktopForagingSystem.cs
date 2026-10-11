using System;
using System.Collections.Generic;
using System.Linq;
using ChaoGarden.Core.Enums;
using ChaoGarden.Simulation.Systems;

namespace ChaoGarden.Simulation.Desktop;

public sealed class DesktopFruitState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public FruitType Type { get; set; }
    public string Monitor { get; set; } = "";
    public double Position { get; set; } = .5;
    // Normalized height above the desktop floor. Runtime window handles are never saved.
    public double Altitude { get; set; }
    public int Bites { get; set; } = 4;
}

/// <summary>Food positions projected by the host onto the Chao's reachable floor segment.</summary>
public readonly record struct DesktopFoodCandidate(DesktopFruitState Fruit, double Position);

/// <summary>Desktop food seeking: engine hunger/radius/gait rules on one horizontal surface.
/// No renderer, wall clock or window APIs. The host arbitrates pickup exactly once.</summary>
public static class DesktopForagingSystem
{
    public static Guid? Update(DesktopChaoState state, IReadOnlyList<DesktopFoodCandidate> foods, double travelWidth, bool available)
    {
        bool interrupted = !available || DesktopChaoSimulation.IsFeeding(state) || DesktopChaoSimulation.IsSleeping(state)
            || DesktopLocomotionSystem.IsAirborne(state) || state.Activity is DesktopActivity.Held or DesktopActivity.Petting
                or DesktopActivity.Landing or DesktopActivity.LandGetUp;
        if (interrupted || !NeedsSystem.ShouldSeekFood(state.Chao))
        {
            state.TargetFruitId = null; state.ForageTicks = 0;
            return null;
        }
        bool canStart = state.Activity is DesktopActivity.Idle or DesktopActivity.WalkLeft or DesktopActivity.WalkRight
            or DesktopActivity.Sitting or DesktopActivity.SittingLook or DesktopActivity.SittingScratch;
        double scale = DesktopLocomotionSystem.PixelsPerUnit(state);
        DesktopFoodCandidate? target = foods.Where(f => f.Fruit.Id == state.TargetFruitId).Select(f => (DesktopFoodCandidate?)f).FirstOrDefault();
        if (state.TargetFruitId != null && (target == null || ++state.ForageTicks > 1800))
        {
            state.TargetFruitId = null; state.ForageTicks = 0;
            if (DesktopLocomotionSystem.IsMoving(state)) DesktopChaoSimulation.Sit(state);
            return null;
        }
        if (target == null)
        {
            // A bounded once-per-second decision, independently phased per Chao.
            if (!canStart || (state.TotalTicks + (uint)state.Random.Seed % 60) % 60 != 0) return null;
            target = foods.Where(f => f.Fruit.Bites > 0 && Math.Abs(f.Position - state.Position) * travelWidth < ChaoFruitPerceptionSystem.SearchRadius * scale)
                .OrderBy(f => Math.Abs(f.Position - state.Position)).Select(f => (DesktopFoodCandidate?)f).FirstOrDefault();
            if (target == null) return null;
            state.TargetFruitId = target.Value.Fruit.Id;
            state.ForageTicks = 0;
        }
        if (!canStart) return null; // Finish stand-up/crawl/turn preparation before steering again.
        double distance = (target.Value.Position - state.Position) * travelWidth;
        if (Math.Abs(distance) <= Math.Max(8, scale * .8)) return target.Value.Fruit.Id;
        DesktopChaoSimulation.ApproachFood(state, distance < 0 ? DesktopActivity.WalkLeft : DesktopActivity.WalkRight);
        return null;
    }
}
