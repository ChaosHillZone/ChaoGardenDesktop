using ChaoGarden.Simulation.Desktop;
using ChaoGarden.Simulation.Systems;

namespace ChaoGarden.DesktopChao;

/// <summary>Horizontal world origin at the frame centre, with the native collision radius.
/// Never depends on a rendered silhouette, head pose, body yaw or attached geometry.</summary>
public static class DesktopPlacement
{
    public static double BodyWidth(DesktopChaoState state) => 2 * ChaoClimbContactSystem.BodyRadius * DesktopLocomotionSystem.PixelsPerUnit(state);
    public static double FrameInset(DesktopChaoState state) => (state.Settings.Size - BodyWidth(state)) / 2;
    public static int FrameLeft(DesktopChaoState state, Rectangle area) => (int)Math.Round(
        area.Left + state.Position * Math.Max(1, area.Width - BodyWidth(state)) - FrameInset(state));
}
