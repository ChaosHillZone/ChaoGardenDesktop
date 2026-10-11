using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;
using ChaoGarden.Simulation.Systems;

namespace ChaoGarden.DesktopChao;

/// <summary>A GPU-rendered fruit that can rest on the desktop or exposed window tops. GPU work occurs only on creation/resize.</summary>
public sealed class DesktopFruitWindow : LayeredPetWindow
{
    private readonly PetApplicationContext host;
    private readonly bool offscreen;
    private readonly ContextMenuStrip menu = new();
    private bool rendered;
    private int renderedSize, contact, center, bodyWidth;
    private Point grabOffset;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private double previous, accumulator;
    public DesktopFruitSurfaceController Surface { get; } = new();
    private Bitmap? image;
    public DesktopFruitState Fruit { get; }
    public DesktopFruitWindow(DesktopFruitState fruit, PetApplicationContext host, bool offscreen)
    {
        Fruit = fruit; this.host = host; this.offscreen = offscreen;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        Text = "DesktopChao fruit";
        ContextMenuStrip = menu;
        menu.Items.Add("Remove fruit", null, (_, _) => host.RemoveFruit(Fruit.Id));
        menu.Opening += (_, _) => SetForegroundWindow(Handle);
        menu.Closed += (_, _) => PostMessage(Handle, 0, 0, 0);
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginDrag(Cursor.Position); };
        MouseMove += (_, _) => { if (Surface.Dragging) MoveDrag(Cursor.Position); };
        MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && Surface.Dragging) EndDrag(); };
        MouseCaptureChanged += (_, _) => { if (Surface.Dragging && !Capture) EndDrag(); };
        try { UpdateFruit(); Show(); }
        catch { Dispose(); throw; }
    }
    protected override CreateParams CreateParams
    {
        get { CreateParams value = base.CreateParams; value.ExStyle = (value.ExStyle & ~0x00040000) | 0x80; return value; }
    }
    public void UpdateFruit()
    {
        int size = Math.Max(64, (int)Math.Ceiling(host.ChaoSize * .42));
        bool resized = image == null || renderedSize != size;
        if (resized)
        {
            renderedSize = size;
            image = ResizeSurface(size, size); Size = new(size, size);
            PropDefinition prop = DesktopFruitCatalog.Prop(Fruit.Type, Fruit.Bites);
            prop.Scale *= FruitAttachmentSystem.NativeScale(Fruit.Type, Fruit.Bites);
            prop.Attachment = Attachment.World; prop.CenterModel = true;
            float height = (float)(11.04 * size / (host.ChaoSize * 416.0 / 384));
            Microsoft.Xna.Framework.Color[] pixels;
            int left, right, top, bottom;
            for (int attempt = 0; ; attempt++)
            {
                pixels = host.Graphics.Renderer.CapturePropPremultiplied(prop, size, size, height);
                left = top = size; right = bottom = -1;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (pixels[y * size + x].A > 8) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                if (bottom < 0) throw new InvalidDataException("The fruit model rendered empty.");
                if (left >= 2 && top >= 2 && right < size - 2 && bottom < size - 2) break;
                if (attempt == 8) throw new InvalidDataException("The fruit does not fit the desktop camera.");
                height *= 1.2f;
            }
            contact = bottom + 1; center = (left + right) / 2; bodyWidth = right - left + 1;
            BitmapData data = image.LockBits(new(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                byte[] bytes = new byte[data.Stride * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var pixel = pixels[y * size + x]; int i = y * data.Stride + x * 4;
                    bytes[i] = Math.Min(pixel.B, pixel.A); bytes[i + 1] = Math.Min(pixel.G, pixel.A);
                    bytes[i + 2] = Math.Min(pixel.R, pixel.A); bytes[i + 3] = pixel.A;
                }
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally { image.UnlockBits(data); }
        }
        Rectangle area = PetApplicationContext.FruitScreen(Fruit.Monitor).WorkingArea;
        double now = clock.Elapsed.TotalSeconds;
        accumulator += Math.Clamp(now - previous, 0, .25);
        previous = now;
        double oldPosition = Fruit.Position, oldAltitude = Fruit.Altitude;
        IReadOnlyList<WindowLedge> ledges = offscreen ? [] : host.WindowLedges(area);
        while (accumulator >= 1.0 / 60)
        {
            Surface.Tick(Fruit, area, bodyWidth, host.ChaoSize * (416.0 / 384) / 11.04, ledges);
            accumulator -= 1.0 / 60;
        }
        if (Fruit.Position != oldPosition || Fruit.Altitude != oldAltitude) host.ScheduleFruitSave();
        Point location = new((int)Math.Round(DesktopFruitSurfaceController.Center(Fruit, area)) - center,
            (int)Math.Round(DesktopFruitSurfaceController.Foot(Fruit, area)) - contact);
        if (offscreen) location.X = SystemInformation.VirtualScreen.Right + renderedSize;
        bool topmost = Surface.Dragging || host.Pets.Any(f => f.Visible && f.State.Settings.AlwaysOnTop);
        if (TopMost != topmost) TopMost = topmost;
        if (resized || !rendered || Location != location)
        {
            Location = location; Present(image!, location); rendered = true;
        }
    }
    public void BeginDrag(Point cursor)
    {
        grabOffset = new(cursor.X - (Left + center), cursor.Y - (Top + contact));
        Surface.PickUp(); Capture = true;
    }
    public void MoveDrag(Point cursor)
    {
        if (!Surface.Dragging) return;
        Screen screen = Screen.FromPoint(cursor);
        Fruit.Monitor = screen.DeviceName;
        Surface.MoveHeld(Fruit, screen.WorkingArea, bodyWidth, cursor.X - grabOffset.X, cursor.Y - grabOffset.Y);
        UpdateFruit();
    }
    public void EndDrag()
    {
        if (!Surface.Dragging) return;
        Rectangle area = PetApplicationContext.FruitScreen(Fruit.Monitor).WorkingArea;
        Surface.Drop(Fruit, area, bodyWidth, offscreen ? [] : host.WindowLedges(area));
        Capture = false;
        host.SaveFruit(); UpdateFruit();
    }
    protected override void Dispose(bool disposing) { if (disposing) menu.Dispose(); base.Dispose(disposing); }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
