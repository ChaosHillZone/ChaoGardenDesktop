using System.Runtime.InteropServices;
using System.Text;

namespace ChaoGarden.DesktopChao;

public readonly record struct DesktopWindow(nint Handle, Rectangle Bounds);
public readonly record struct WindowLedge(nint Handle, int Left, int Right, int Top)
{
    // Keep full window geometry: occlusion and monitor clipping can change a span without moving its window.
    public Rectangle WindowBounds { get; init; }
    public Rectangle MotionBounds => WindowBounds.IsEmpty ? new(Left, Top, Right - Left, 0) : WindowBounds;
}

/// <summary>Read-only window geometry; never moves, focuses or changes another application's windows.</summary>
public static class DesktopWindowSurfaces
{
    public static IReadOnlyList<WindowLedge> Read(Rectangle screen, int excludedProcess = -1)
    {
        if (excludedProcess == -1) excludedProcess = Environment.ProcessId;
        List<DesktopWindow> windows = [];
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint process);
            if (process == excludedProcess || !IsWindowVisible(window) || IsIconic(window)) return true;
            DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int));
            if (cloaked != 0) return true;
            StringBuilder name = new(128); GetClassName(window, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if ((GetWindowLong(window, -20) & 0x80) != 0) return true; // Tool windows / menus are not platforms.
            if (DwmGetWindowAttribute(window, 9, out NativeRect rect, Marshal.SizeOf<NativeRect>()) != 0
                && !GetWindowRect(window, out rect)) return true;
            Rectangle bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width >= 64 && bounds.Height >= 32 && bounds.IntersectsWith(screen)) windows.Add(new(window, bounds));
            return true;
        }, 0);
        return ExposedTops(windows, screen);
    }

    // EnumWindows supplies front-to-back order. A covered portion of a rear window is not a ledge.
    public static IReadOnlyList<WindowLedge> ExposedTops(IEnumerable<DesktopWindow> frontToBack, Rectangle screen)
    {
        List<WindowLedge> ledges = [];
        List<Rectangle> blockers = [];
        foreach (DesktopWindow window in frontToBack)
        {
            Rectangle bounds = window.Bounds;
            if (bounds.Top >= screen.Top + 64 && bounds.Top < screen.Bottom)
            {
                List<(int Left, int Right)> spans = [(Math.Max(bounds.Left, screen.Left), Math.Min(bounds.Right, screen.Right))];
                foreach (Rectangle blocker in blockers.Where(b => b.Top <= bounds.Top && b.Bottom > bounds.Top))
                {
                    List<(int Left, int Right)> exposed = [];
                    foreach (var span in spans)
                    {
                        if (blocker.Right <= span.Left || blocker.Left >= span.Right) { exposed.Add(span); continue; }
                        if (blocker.Left > span.Left) exposed.Add((span.Left, blocker.Left));
                        if (blocker.Right < span.Right) exposed.Add((blocker.Right, span.Right));
                    }
                    spans = exposed;
                }
                ledges.AddRange(spans.Where(s => s.Right - s.Left >= 32).Select(s => new WindowLedge(window.Handle, s.Left, s.Right, bounds.Top) { WindowBounds = bounds }));
            }
            blockers.Add(bounds);
        }
        return ledges;
    }

    private delegate bool EnumCallback(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);
}
