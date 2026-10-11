using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;
using ChaoGarden.Simulation.Systems;

namespace ChaoGarden.DesktopChao;

/// <summary>Projects native ground/air simulation onto desktop and exposed window-top segments.</summary>
public sealed class DesktopSurfaceController
{
    public WindowLedge? Support { get; private set; }
    private Rectangle previousScreen;
    private Rectangle Area(Rectangle screen, WindowLedge? support) => support is { } ledge
        ? Rectangle.FromLTRB(ledge.Left, screen.Top, ledge.Right, ledge.Top) : screen;
    public Rectangle Area(Rectangle screen) => Area(screen, Support);
    private static double Width(Rectangle area, double bodyWidth) => Math.Max(1, area.Width - bodyWidth);
    private static double Height(Rectangle area, double spriteHeight) => Math.Max(1, area.Height - spriteHeight);
    public static double Foot(DesktopChaoState state, Rectangle area, double spriteHeight) => area.Bottom - state.Altitude * Height(area, spriteHeight);
    private static double Left(DesktopChaoState state, Rectangle area, double bodyWidth) => area.Left + state.Position * Width(area, bodyWidth);
    private static void Place(DesktopChaoState state, Rectangle area, double bodyWidth, double spriteHeight, double left, double foot)
    {
        state.Position = Math.Clamp((left - area.Left) / Width(area, bodyWidth), 0, 1);
        state.Altitude = Math.Clamp((area.Bottom - foot) / Height(area, spriteHeight), 0, 1);
    }
    private void Detach(DesktopChaoState state, Rectangle screen, double bodyWidth, double spriteHeight)
    {
        Rectangle area = Area(screen);
        double left = Left(state, area, bodyWidth), foot = Foot(state, area, spriteHeight);
        Support = null;
        Place(state, screen, bodyWidth, spriteHeight, left, foot);
    }
    public void PickUp(DesktopChaoState state, Rectangle screen, double bodyWidth, double spriteHeight)
    {
        Detach(state, screen, bodyWidth, spriteHeight);
        DesktopChaoSimulation.Begin(state, DesktopActivity.Held, 1);
    }
    public void MoveHeld(DesktopChaoState state, Rectangle screen, double bodyWidth, double spriteHeight, double left, double foot) =>
        Place(state, screen, bodyWidth, spriteHeight, left, foot);
    public void Drop(DesktopChaoState state)
    {
        state.AfterLanding = state.RemainingBites > 0 ? DesktopActivity.EatStart : DesktopActivity.Idle;
        DesktopLocomotionSystem.StartFall(state);
        // Releasing the mouse drops from rest; native ledge departures retain their launch impulse.
        state.Chao.Behaviour.Action.Phase = ChaoActionPhase.Perform;
        state.Chao.Physics.VelocityX = state.Chao.Physics.VelocityY = state.Chao.Physics.VelocityZ = 0;
    }
    public void JumpOff(DesktopChaoState state, Rectangle screen, double bodyWidth, double spriteHeight, bool left)
    {
        Detach(state, screen, bodyWidth, spriteHeight);
        // Native land reaction's Fly*3 >= 400 admission and 40% flight choice.
        if (DesktopLocomotionSystem.CanFly(state) && DeterministicRandom.NextRaw(state.Random) / 32768.0 >= .6000000238418579)
            DesktopLocomotionSystem.StartFlight(state);
        else DesktopLocomotionSystem.StartFall(state);
        state.AfterLanding = state.RemainingBites > 0 ? DesktopActivity.EatStart : DesktopActivity.Idle;
        state.Chao.Transform.Rotation = ChaoLandMovementSystem.Rotation(left ? 0xc000 : 0x4000);
    }
    public void Tick(DesktopChaoState state, Rectangle screen, double bodyWidth, double spriteHeight, IReadOnlyList<WindowLedge> ledges)
    {
        if (Support is { } old)
        {
            Rectangle oldScreen = previousScreen.IsEmpty ? screen : previousScreen;
            Rectangle oldArea = Area(oldScreen);
            double supportedLeft = Left(state, oldArea, bodyWidth), supportedFoot = Foot(state, oldArea, spriteHeight);
            WindowLedge? geometry = ledges.Where(l => l.Handle == old.Handle)
                .Select(l => (WindowLedge?)l).FirstOrDefault();
            if (geometry is { } window && oldScreen == screen)
            {
                Rectangle before = old.MotionBounds, after = window.MotionBounds;
                // Translation carries the Chao in pixels. Resizing never rescales its horizontal position.
                if (before.Size == after.Size) supportedLeft += after.Left - before.Left;
                WindowLedge? current = ledges.Where(l => l.Handle == old.Handle
                        && supportedLeft >= l.Left && supportedLeft + bodyWidth <= l.Right)
                    .Select(l => (WindowLedge?)l).FirstOrDefault();
                if (current is { } platform && platform.Top <= supportedFoot + .001)
                {
                    Support = platform;
                    Place(state, Area(screen), bodyWidth, spriteHeight, supportedLeft, platform.Top);
                }
                else
                {
                    // A descending or shrinking platform leaves the body in screen space; gravity catches it.
                    Support = null;
                    Place(state, screen, bodyWidth, spriteHeight, supportedLeft, supportedFoot);
                    Drop(state);
                }
            }
            else
            {
                Support = null;
                Place(state, screen, bodyWidth, spriteHeight, supportedLeft, supportedFoot);
                Drop(state);
            }
        }
        previousScreen = screen;
        Rectangle area = Area(screen);
        double previousFoot = Foot(state, area, spriteHeight);
        bool wasMoving = DesktopLocomotionSystem.IsMoving(state);
        DesktopChaoSimulation.Tick(state, Width(area, bodyWidth), Height(area, spriteHeight), allowClimb: Support == null);
        if (Support != null)
        {
            if (DesktopLocomotionSystem.IsAirborne(state)) Detach(state, screen, bodyWidth, spriteHeight);
            else if (wasMoving && state.Position is <= 0 or >= 1 && DeterministicRandom.NextRange(state.Random, 0, 100) < 35)
                JumpOff(state, screen, bodyWidth, spriteHeight, state.Position <= 0);
            return;
        }
        if (!DesktopLocomotionSystem.IsAirborne(state) && state.Activity != DesktopActivity.Landing) return;
        double foot = Foot(state, screen, spriteHeight);
        if (foot < previousFoot || state.Chao.Physics.VelocityY > 0) return;
        double left = Left(state, screen, bodyWidth);
        WindowLedge? landing = ledges.Where(l => l.Top >= previousFoot - .5 && l.Top <= foot + .5
                && left >= l.Left && left + bodyWidth <= l.Right)
            .OrderBy(l => l.Top).Select(l => (WindowLedge?)l).FirstOrDefault();
        if (landing is not { } surface) return;
        Support = surface;
        Place(state, Area(screen), bodyWidth, spriteHeight, left, surface.Top);
        state.Chao.Physics.IsGrounded = true;
        state.Chao.Physics.VelocityX = state.Chao.Physics.VelocityY = state.Chao.Physics.VelocityZ = 0;
        state.Chao.Behaviour.Action.Kind = ChaoActionKind.None;
        DesktopChaoSimulation.Begin(state, DesktopActivity.Landing, 1);
    }
    public DesktopChaoState SaveSnapshot(DesktopChaoState state, Rectangle screen, double bodyWidth, double spriteHeight)
    {
        if (Support == null && state.Activity != DesktopActivity.Held) return state;
        DesktopChaoState snapshot = BoothJson.Clone(state);
        Place(snapshot, screen, bodyWidth, spriteHeight, Left(state, Area(screen), bodyWidth), Foot(state, Area(screen), spriteHeight));
        Drop(snapshot); // HWNDs are session-specific. Resume safely in the air at the saved desktop position.
        return snapshot;
    }
}
