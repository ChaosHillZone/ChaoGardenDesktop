using ChaoGarden.Simulation.Desktop;

namespace ChaoGarden.DesktopChao;

/// <summary>Fixed-step fruit placement on the desktop or an exposed window ledge. Handles are session-only.</summary>
public sealed class DesktopFruitSurfaceController
{
    public WindowLedge? Support { get; private set; }
    public bool Dragging { get; private set; }
    public bool Falling { get; private set; }
    private double velocity, supportFraction;
    private Rectangle previousScreen;

    public static double Center(DesktopFruitState fruit, Rectangle screen) => screen.Left + fruit.Position * screen.Width;
    public static double Foot(DesktopFruitState fruit, Rectangle screen) => screen.Bottom - fruit.Altitude * screen.Height;

    private static void Place(DesktopFruitState fruit, Rectangle screen, double width, double center, double foot)
    {
        double margin = Math.Min(width / 2, screen.Width / 2.0);
        fruit.Position = Math.Clamp((center - screen.Left) / Math.Max(1, screen.Width), margin / screen.Width, 1 - margin / screen.Width);
        fruit.Altitude = Math.Clamp((screen.Bottom - foot) / Math.Max(1, screen.Height), 0, 1);
    }

    public void PickUp()
    {
        Support = null;
        Dragging = true;
        Falling = false;
        velocity = 0;
    }

    public void MoveHeld(DesktopFruitState fruit, Rectangle screen, double width, double center, double foot)
    {
        if (!Dragging) return;
        Place(fruit, screen, width, center, foot);
        previousScreen = screen;
    }

    private static bool Fits(WindowLedge ledge, double center, double width) =>
        center - width / 2 >= ledge.Left && center + width / 2 <= ledge.Right;

    private void Land(DesktopFruitState fruit, Rectangle screen, double width, WindowLedge ledge)
    {
        double center = Center(fruit, screen);
        Support = ledge;
        supportFraction = Math.Clamp((center - ledge.Left - width / 2) / Math.Max(1, ledge.Right - ledge.Left - width), 0, 1);
        Place(fruit, screen, width, center, ledge.Top);
        Falling = false;
        velocity = 0;
    }

    public void Drop(DesktopFruitState fruit, Rectangle screen, double width, IReadOnlyList<WindowLedge> ledges)
    {
        Dragging = false;
        Support = null;
        velocity = 0;
        previousScreen = screen;
        double foot = Foot(fruit, screen);
        // A small grab-point tolerance makes placing directly on a title bar practical.
        WindowLedge? nearby = ledges.Where(l => Fits(l, Center(fruit, screen), width) && Math.Abs(l.Top - foot) <= 16)
            .OrderBy(l => Math.Abs(l.Top - foot)).Select(l => (WindowLedge?)l).FirstOrDefault();
        if (nearby is { } ledge) Land(fruit, screen, width, ledge);
        else Falling = fruit.Altitude > 0;
    }

    public void Tick(DesktopFruitState fruit, Rectangle screen, double width, double pixelsPerUnit, IReadOnlyList<WindowLedge> ledges)
    {
        if (Dragging) return;
        if (Support is { } old)
        {
            WindowLedge? current = ledges.Where(l => l.Handle == old.Handle && l.Right - l.Left >= width)
                .OrderBy(l => Math.Abs(l.Left - old.Left)).Select(l => (WindowLedge?)l).FirstOrDefault();
            if (current is { } ledge && previousScreen == screen)
            {
                double center = ledge.Left + width / 2 + supportFraction * (ledge.Right - ledge.Left - width);
                Support = ledge;
                Place(fruit, screen, width, center, ledge.Top);
                return;
            }
            Support = null;
            Falling = true;
            velocity = 0;
        }
        previousScreen = screen;
        if (fruit.Altitude <= 0) { Falling = false; velocity = 0; return; }
        Falling = true; // Also resolves restored positions without persisting an HWND.
        double previousFoot = Foot(fruit, screen);
        velocity += .055 * pixelsPerUnit;
        double foot = previousFoot + velocity;
        WindowLedge? landing = ledges.Where(l => Fits(l, Center(fruit, screen), width)
                && l.Top >= previousFoot - .5 && l.Top <= foot + .5)
            .OrderBy(l => l.Top).Select(l => (WindowLedge?)l).FirstOrDefault();
        if (landing is { } surface) Land(fruit, screen, width, surface);
        else
        {
            Place(fruit, screen, width, Center(fruit, screen), foot);
            if (fruit.Altitude == 0) { Falling = false; velocity = 0; }
        }
    }

    public bool TryProjectFood(DesktopFruitState fruit, Rectangle screen, double chaoWidth, WindowLedge? chaoSupport, out double position)
    {
        position = 0;
        if (Dragging || Falling) return false;
        Rectangle area = screen;
        if (chaoSupport is { } ledge)
        {
            if (Support is not { } support || support.Handle != ledge.Handle || support.Top != ledge.Top
                || Center(fruit, screen) < ledge.Left || Center(fruit, screen) > ledge.Right) return false;
            area = Rectangle.FromLTRB(ledge.Left, screen.Top, ledge.Right, ledge.Top);
        }
        else if (Support != null || fruit.Altitude > 0) return false;
        position = Math.Clamp((Center(fruit, screen) - area.Left - chaoWidth / 2) / Math.Max(1, area.Width - chaoWidth), 0, 1);
        return true;
    }
}
