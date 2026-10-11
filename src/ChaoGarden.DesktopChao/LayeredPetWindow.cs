using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ChaoGarden.DesktopChao;

/// <summary>A per-pixel-alpha window. Zero-alpha pixels pass clicks through to other apps.</summary>
public class LayeredPetWindow : Form
{
    private IntPtr memory, pixels, previousBitmap;
    private Bitmap? surface;

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams value = base.CreateParams;
            value.ExStyle |= 0x00080000 | 0x08000000 | 0x00040000; // Layered, no activate, taskbar entry.
            return value;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x21) // WM_MOUSEACTIVATE: interacting with the pet doesn't steal typing focus.
        {
            message.Result = new IntPtr(3);
            return;
        }
        base.WndProc(ref message);
    }

    // Keep one DIB/DC for the window's lifetime (recreate only on size changes).
    // The renderer writes directly into this surface; GetHbitmap otherwise allocates
    // and copies the entire image on every frame.
    protected Bitmap ResizeSurface(int width, int height)
    {
        if (surface?.Width == width && surface.Height == height) return surface;
        ReleaseSurface();
        try
        {
            memory = CreateCompatibleDC(IntPtr.Zero);
            if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            BitmapInfo info = new()
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = width, Height = -height,
                Planes = 1, BitCount = 32
            };
            pixels = CreateDIBSection(memory, ref info, 0, out IntPtr address, IntPtr.Zero, 0);
            if (pixels == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            previousBitmap = SelectObject(memory, pixels);
            surface = new Bitmap(width, height, width * 4,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb, address);
            return surface;
        }
        catch { ReleaseSurface(); throw; }
    }

    protected void Present(Bitmap bitmap, Point location)
    {
        if (!ReferenceEquals(bitmap, surface)) throw new ArgumentException("Use the window's presentation surface.", nameof(bitmap));
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            NativePoint target = new(location.X, location.Y);
            NativePoint source = new(0, 0);
            NativeSize size = new(bitmap.Width, bitmap.Height);
            BlendFunction blend = new() { SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(Handle, screen, ref target, ref size, memory, ref source, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot present the transparent Chao window.");
        }
        finally { if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen); }
    }

    private void ReleaseSurface()
    {
        surface?.Dispose();
        surface = null;
        if (previousBitmap != IntPtr.Zero) SelectObject(memory, previousBitmap);
        if (pixels != IntPtr.Zero) DeleteObject(pixels);
        if (memory != IntPtr.Zero) DeleteDC(memory);
        memory = pixels = previousBitmap = IntPtr.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseSurface();
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x; public int Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int width, int height) { public int Width = width; public int Height = height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage,
        out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref NativePoint position,
        ref NativeSize size, IntPtr source, ref NativePoint sourcePosition, uint colour, ref BlendFunction blend, uint flags);
}
