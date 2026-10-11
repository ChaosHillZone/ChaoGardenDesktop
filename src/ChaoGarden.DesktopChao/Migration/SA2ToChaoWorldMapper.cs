using System;
using ChaoGarden.Core.Models.Entities;

namespace ChaoGarden.Tools.Migration;

/// <summary>
/// Maps a <see cref="ChaoEntity"/> parsed from SA2B memory into a form ready
/// for import into Chao World HD. Handles any value adjustments, clamping,
/// or default-filling needed for fields that exist in Chao World HD but not in SA2B.
/// </summary>
public static class SA2ToChaoWorldMapper
{
    /// <summary>
    /// Maps an SA2B-parsed ChaoEntity to a Chao World HD ChaoEntity.
    /// Assigns a new unique Id and fills in any fields that SA2B does not track.
    /// </summary>
    public static ChaoEntity Map(ChaoEntity sa2Chao)
    {
        // The parser already produces a ChaoEntity with correct field values.
        // The mapper's job is to:
        // 1. Assign a fresh Guid (SA2B has no persistent unique ID)
        // 2. Clamp any values that might be out of expected range
        // 3. Fill defaults for fields that SA2B doesn't have

        sa2Chao.Id = Guid.NewGuid();

        // Clamp alignment to [-1, 1] range (SA2B stores as float, usually in this range)
        sa2Chao.Alignment = Math.Clamp(sa2Chao.Alignment, -1.0f, 1.0f);
        sa2Chao.RunToPower = Math.Clamp(sa2Chao.RunToPower, -1.0f, 1.0f);
        sa2Chao.SwimToFly = Math.Clamp(sa2Chao.SwimToFly, -1.0f, 1.0f);
        sa2Chao.TransformationMagnitude = Math.Clamp(sa2Chao.TransformationMagnitude, 0.0f, 1.0f);

        // Clamp stat values to valid ranges
        ClampStats(sa2Chao.Stats.Swim);
        ClampStats(sa2Chao.Stats.Fly);
        ClampStats(sa2Chao.Stats.Run);
        ClampStats(sa2Chao.Stats.Power);
        ClampStats(sa2Chao.Stats.Stamina);
        ClampStats(sa2Chao.Stats.Luck);
        ClampStats(sa2Chao.Stats.Intelligence);
        ClampStats(sa2Chao.Stats.Unknown);

        // Happiness range: -128 to 127 (sbyte), no clamping needed
        // Reincarnations: ushort, no clamping needed

        return sa2Chao;
    }

    private static void ClampStats(StatBlock stat)
    {
        // Level: 0-99
        if (stat.Level > 99)
        {
            stat.Level = 99;
        }

        // Bar: 0-99
        if (stat.Bar > 99)
        {
            stat.Bar = 99;
        }

        // Points: 0-9999
        if (stat.Points > 9999)
        {
            stat.Points = 9999;
        }
    }
}
