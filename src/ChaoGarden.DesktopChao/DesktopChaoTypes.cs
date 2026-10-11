using ChaoGarden.Core.Enums;
using ChaoGarden.ModelBuilder.Chao;
using ChaoGarden.Simulation.Desktop;

namespace ChaoGarden.DesktopChao;

public static class DesktopChaoTypes
{
    public static bool IsSelectable(ChaoType type) => type == ChaoType.Child
        || type is >= ChaoType.NeutralNormal and <= ChaoType.Amy;
    public static IEnumerable<ChaoType> All => Enum.GetValues<ChaoType>().Where(IsSelectable);
    public static string Label(ChaoType type) => type == ChaoType.Child ? "Neutral child"
        : System.Text.RegularExpressions.Regex.Replace(type.ToString(), "([a-z])([A-Z])", "$1 $2");
    public static void Apply(DesktopChaoState state, ChaoType type)
    {
        if (!IsSelectable(type)) throw new ArgumentException("Select a supported Chao body type.", nameof(type));
        state.Chao.ChaoType = type;
        state.Chao.Alignment = ChaoVariantModelKeys.GetAlignmentCode(type) switch { 'h' => 1, 'd' => -1, _ => 0 };
        // Choose the base evolution form, not a leftover secondary morph from the previous type.
        state.Chao.RunToPower = state.Chao.SwimToFly = state.Chao.TransformationMagnitude = 0;
        state.Chao.ColourSaturation = type == ChaoType.Child ? 0 : 1;
    }
}
