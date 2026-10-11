using ChaoGarden.ChaoPresentation;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.Simulation.Systems;

namespace ChaoGarden.DesktopChao;

/// <summary>Desktop recipes use Ball for the persistent orb, and explicit icons for reactions.</summary>
public static class DesktopEmotes
{
    public static EmoteKind Resolve(ChaoEntity chao, EmoteKind recipe)
    {
        if (recipe != EmoteKind.Ball) return recipe;
        if (chao.Appearance.EmoteBall == Emotiball.None) return EmoteKind.None;
        return EmotionDisplaySystem.GetDefaultBallType(chao) switch
        {
            EmotionBallType.Halo => EmoteKind.Halo,
            EmotionBallType.SpikyBall => EmoteKind.Spiky,
            _ => EmoteKind.Ball
        };
    }
}
