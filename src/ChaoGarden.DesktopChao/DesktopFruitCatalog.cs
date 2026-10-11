using ChaoGarden.Content.Loading;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Rendering;
using ChaoGarden.ChaoPresentation;

namespace ChaoGarden.DesktopChao;

public static class DesktopFruitCatalog
{
    public static IReadOnlyList<FruitDefinition> Available(string resources) =>
        FruitDefinitionLoader.Load(File.ReadAllText(Path.Combine(resources, "data", "fruit_definitions.json")))
            .Where(f => FruitModelKeys.Get(f.FruitType) is { } model && File.Exists(Path.Combine(resources, model))).ToArray();

    public static PropDefinition Prop(FruitType type, int bites = 4) => new()
    {
        Model = FruitModelKeys.Get(type) ?? throw new InvalidDataException("This fruit has no model in the current resources."),
        FruitType = type, RemainingBites = bites,
        // Desktop presentation keeps square fruit comparable to the other full-size foods.
        // Cancel its extra native 0.8 factor in both loose and feeding renders, leaving the
        // common portion shrink intact. Garden physics/native display rules are unchanged.
        Scale = type == FruitType.SquareFruit ? .65f / .8f : .65f
    };
}
