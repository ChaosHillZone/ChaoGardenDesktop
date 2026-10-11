using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using ChaoGarden.ChaoPresentation;

namespace ChaoGarden.DesktopChao;

public sealed class SpriteBank : IDisposable
{
    private sealed record Frame(string File, FrameMetadata Rect, int Index);
    private sealed class Animation
    {
        public string Playback = "";
        public float Fps;
        public int Count;
        public List<Frame> Frames = [];
    }
    private readonly Dictionary<string, Animation> animations = new();
    private readonly Dictionary<string, Bitmap> pages = new();
    private readonly Queue<string> pageOrder = new();
    private readonly string root;
    public int Width { get; }
    public int Height { get; }
    public float GroundRatio { get; }
    public bool HasDirections => animations.ContainsKey("look-0-0/idle");

    public SpriteBank(string root)
    {
        this.root = root;
        ExportManifest manifest = JsonSerializer.Deserialize<ExportManifest>(
            File.ReadAllText(Path.Combine(root, "manifest.json")), BoothJson.Options)
            ?? throw new InvalidDataException("Empty sprite manifest.");
        if (manifest.SchemaVersion != 2 || manifest.Warnings.Count > 0)
        {
            throw new InvalidDataException("Unsupported or clipped sprite export.");
        }
        Width = manifest.CellWidth;
        Height = manifest.CellHeight;
        GroundRatio = (manifest.GroundY ?? Height * .88f) / Height;
        foreach (SheetMetadata sheet in manifest.Sheets)
        {
            string path = BoothJson.ResourcePath(root, sheet.File);
            if (!File.Exists(path)) throw new FileNotFoundException("Missing sprite page.", path);
            foreach (RowMetadata row in sheet.Rows)
            {
                string key = row.Group + "/" + row.Name;
                if (!animations.TryGetValue(key, out Animation? animation))
                {
                    animations[key] = animation = new Animation { Playback = row.Playback, Fps = row.FramesPerSecond, Count = row.TotalFrames };
                }
                animation.Frames.AddRange(row.Frames.Select((frame, index) => new Frame(sheet.File, frame, row.StartFrame + index)));
            }
        }
        foreach (Animation animation in animations.Values)
        {
            animation.Frames.Sort((a, b) => a.Index.CompareTo(b.Index));
            if (animation.Fps <= 0 || animation.Count != animation.Frames.Count
                || animation.Frames.Where((f, i) => f.Index != i).Any())
            {
                throw new InvalidDataException("Incomplete animation pages.");
            }
        }
        if (!animations.ContainsKey("center/idle")) throw new InvalidDataException("Missing idle animation.");
    }

    public bool HasDirection(string name, string direction) => animations.ContainsKey(direction + "/" + name);

    private Frame FrameAt(string name, string direction, double seconds)
    {
        if (!animations.TryGetValue(direction + "/" + name, out Animation? animation)
            && !animations.TryGetValue("center/" + name, out animation)) animation = animations["center/idle"];
        int index = (int)(Math.Max(0, seconds) * animation.Fps);
        index = animation.Playback == "Once" ? Math.Min(index, animation.Count - 1) : index % animation.Count;
        return animation.Frames[index];
    }

    public float GroundRatioFor(string name, string direction, double seconds)
    {
        // Original clips and attached parts can extend below their model-space floor.
        // Use the exported silhouette's foot/bottom contact, not transparent cell padding.
        int[]? bounds = FrameAt(name, direction, seconds).Rect.Bounds;
        return bounds is { Length: 4 } ? bounds[3] / (float)Height : GroundRatio;
    }

    public void Draw(Graphics graphics, string name, string direction, double seconds, Rectangle destination)
    {
        Frame frame = FrameAt(name, direction, seconds);
        Bitmap page = Page(frame.File);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        FrameMetadata r = frame.Rect;
        // Texture brush wrapping isn't involved: every frame is drawn from its own atlas rectangle.
        using ImageAttributes attributes = new();
        attributes.SetWrapMode(WrapMode.Clamp);
        graphics.DrawImage(page, destination, r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, attributes);
    }

    private Bitmap Page(string file)
    {
        if (pages.TryGetValue(file, out Bitmap? bitmap)) return bitmap;
        using MemoryStream bytes = new(File.ReadAllBytes(BoothJson.ResourcePath(root, file)));
        using Bitmap decoded = new(bytes);
        bitmap = decoded.Clone(new Rectangle(0, 0, decoded.Width, decoded.Height), PixelFormat.Format32bppPArgb);
        pages.Add(file, bitmap);
        pageOrder.Enqueue(file);
        // Bound GPU-independent playback memory even with hundreds of exported pages.
        while (pages.Count > 3)
        {
            string old = pageOrder.Dequeue();
            pages[old].Dispose();
            pages.Remove(old);
        }
        return bitmap;
    }

    public void Dispose()
    {
        foreach (Bitmap page in pages.Values) page.Dispose();
        pages.Clear();
    }
}
