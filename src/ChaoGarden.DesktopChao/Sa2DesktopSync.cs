using System.Security.Principal;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.Core.SaveFormat;
using ChaoGarden.Simulation.Desktop;
using ChaoGarden.Tools.Migration;

namespace ChaoGarden.DesktopChao;

public sealed record Sa2DesktopSnapshot(int Slot, ChaoEntity Chao);

/// <summary>One-way snapshots of the engine importer's 24 save slots. No process write handle is ever requested.</summary>
public static class Sa2DesktopSync
{
    public static bool IsAdministrator
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
    public static List<Sa2DesktopSnapshot> Read()
    {
        if (!IsAdministrator) throw new UnauthorizedAccessException("Run DesktopChao as administrator to enable SA2 synchronization.");
        using SA2ProcessReader reader = new();
        return Parse(reader.ReadAllChaoBlocks());
    }
    public static List<Sa2DesktopSnapshot> Parse(IEnumerable<SA2ChaoBlock> blocks)
    {
        List<Sa2DesktopSnapshot> snapshots = [];
        foreach (SA2ChaoBlock block in blocks)
        {
            if (!block.IsOccupied()) continue;
            ChaoEntity chao = SA2ToChaoWorldMapper.Map(SA2ChaoParser.Parse(block));
            // Eggs have no renderable Chao body; they appear after hatching in SA2.
            if (chao.ChaoType < ChaoType.Child) continue;
            PetSaveStore.Validate(new DesktopChaoState { Chao = chao });
            snapshots.Add(new(block.SlotIndex, chao));
        }
        return snapshots;
    }
    public static void Apply(DesktopChaoState target, ChaoEntity source)
    {
        // Validate the entire incoming definition before changing a live pet.
        PetSaveStore.Validate(new DesktopChaoState { Chao = source });
        ChaoEntity chao = target.Chao;
        chao.Name = target.LocalName ?? source.Name;
        chao.ChaoType = source.ChaoType;
        chao.Alignment = source.Alignment;
        chao.RunToPower = source.RunToPower;
        chao.SwimToFly = source.SwimToFly;
        chao.TransformationMagnitude = source.TransformationMagnitude;
        chao.ColourSaturation = source.ColourSaturation;
        chao.Appearance = source.Appearance;
        chao.Stats = source.Stats;
        chao.Skills = source.Skills;
        chao.Personality = source.Personality;
        chao.Health = source.Health;
        chao.Happiness = source.Happiness;
        chao.Genetics = source.Genetics;
        chao.Lifespan1 = source.Lifespan1;
        chao.Lifespan2 = source.Lifespan2;
        chao.Reincarnations = source.Reincarnations;
        if (target.LocalType is { } type) DesktopChaoTypes.Apply(target, type);
        // Identity, RNG, desktop clocks, needs, animation, physics and local name remain desktop-owned.
    }
}
