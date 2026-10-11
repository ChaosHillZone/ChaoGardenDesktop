using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Animation;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;

namespace ChaoGarden.DesktopChao;

/// <summary>UI-thread renderer using the shared ChaoPresentation model/material pipeline, without exporting sheets.</summary>
public sealed class LiveChaoRenderer : IDisposable
{
    private readonly SharedPetGraphics graphics;
    private readonly bool ownsGraphics;
    private readonly ChaoRenderSession renderer;
    private readonly BoothConfig config = DesktopRecipes.Create(false);
    private readonly Dictionary<string, ShotDefinition> shots;
    private readonly Dictionary<string, EmoteKind> recipeEmotes;
    private ChaoEntity? chao;
    private EyeType eyes;
    private MouthType mouth;
    private string appearanceKey = "";
    private byte[] pixels = [];
    public Rectangle FrameBounds { get; private set; }
    public Rectangle BodyBounds { get; private set; }
    public float GroundRatio => CameraFraming.GroundY(config) / config.CellHeight;

    public LiveChaoRenderer(string resources, SharedPetGraphics? shared = null)
    {
        graphics = shared ?? new SharedPetGraphics(resources);
        ownsGraphics = shared == null;
        renderer = graphics.Renderer;
        shots = config.Sheets[0].Rows.ToDictionary(row => row.Name);
        foreach (DesktopAnimationDefinition animation in DesktopAnimationCatalog.All)
            shots.Add(animation.Clip, DesktopCatalogShots.Create(animation));
        recipeEmotes = shots.ToDictionary(pair => pair.Key, pair => pair.Value.Emote.Kind);
        config.Camera.OrthographicHeight = 11.04f;
    }

    public void SetAppearance(ChaoEntity source, string key)
    {
        if (appearanceKey == key) return;
        chao = BoothJson.Clone(source);
        eyes = chao.Appearance.Eyes;
        mouth = chao.Appearance.Mouth;
        appearanceKey = key;
        config.Camera.OrthographicHeight = 11.04f;
    }

    public int Draw(Bitmap target, DesktopChaoState state, float yaw, float pitch)
    {
        DesktopAnimationSystem.EnsureInitialized(state);
        string animation = state.Activity == DesktopActivity.GameAnimation ? state.CatalogClip : DesktopChaoSimulation.Animation(state);
        if (animation.StartsWith("eat-", StringComparison.Ordinal))
        {
            ShotDefinition shot = shots[animation];
            shot.Props.Clear();
            shot.Props.Add(DesktopFruitCatalog.Prop(state.EatingFruitType, state.RemainingBites));
        }
        return Draw(target, animation, state.ActivityTicks, yaw, pitch, state.Chao.Animation, state.FacingYaw, DesktopPastimeSystem.Mouth(state));
    }

    public int Draw(Bitmap target, string animation, int ticks, float yaw, float pitch,
        AnimationPlaybackState? playback = null, float bodyYaw = 0, MouthType? activityMouth = null)
    {
        if (chao == null) throw new InvalidOperationException("Set the Chao appearance before rendering.");
        ShotDefinition shot = shots[animation];
        // Resolve from the original recipe every draw: cached shots must not retain another type's shape.
        shot.Emote.Kind = DesktopEmotes.Resolve(chao, recipeEmotes[animation]);
        renderer.MotionTableOverride = DesktopAnimationCatalog.Table;
        int frame = shot.Playback == Playback.Once ? Math.Min(ticks, shot.Frames - 1) : ticks % shot.Frames;
        bool cursor = DesktopMotions.All.TryGetValue(animation, out DesktopMotion? motion) && motion.Cursor;
        if (playback != null)
        {
            chao.Animation = playback;
            shot.YawDegrees = bodyYaw;
        }
        shot.HeadYawDegrees[0] = cursor ? yaw : 0;
        shot.HeadPitchDegrees[0] = cursor ? pitch : 0;
        chao.Appearance.Eyes = shot.Eyes ?? eyes;
        chao.Appearance.Mouth = activityMouth ?? shot.Mouth ?? mouth;
        chao.Expression.BlinkTicksRemaining = ChaoGarden.Core.Rendering.ChaoFaceExpression.SupportsBlink(chao.ChaoType) && shot.BlinkFrames.Contains(frame) ? 3 : 0;
        config.CellWidth = target.Width;
        config.CellHeight = target.Height;
        Microsoft.Xna.Framework.Color[] image;
        for (int attempt = 0; ; attempt++)
        {
            image = renderer.CapturePremultiplied(chao, shot, frame, config, playback != null);
            int left = target.Width, top = target.Height, right = -1, bottom = -1;
            for (int y = 0; y < target.Height; y++)
            for (int x = 0; x < target.Width; x++)
                if (image[y * target.Width + x].A > 8)
                {
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
            if (right < 0) throw new InvalidDataException("Live Chao frame is transparent.");
            FrameBounds = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            var body = renderer.BodyBounds;
            BodyBounds = new Rectangle(body.X, body.Y, body.Width, body.Height);
            if (left >= 3 && top >= 3 && right < target.Width - 3 && bottom < target.Height - 3) break;
            if (attempt >= 8) throw new InvalidDataException("The selected appearance does not fit the live camera.");
            config.Camera.OrthographicHeight *= 1.12f;
        }
        BitmapData data = target.LockBits(new Rectangle(0, 0, target.Width, target.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            int length = data.Stride * data.Height;
            if (pixels.Length != length) pixels = new byte[length];
            for (int y = 0; y < target.Height; y++)
                for (int x = 0; x < target.Width; x++)
                {
                    Microsoft.Xna.Framework.Color colour = image[y * target.Width + x];
                    int index = y * data.Stride + x * 4;
                    pixels[index] = Math.Min(colour.B, colour.A);
                    pixels[index + 1] = Math.Min(colour.G, colour.A);
                    pixels[index + 2] = Math.Min(colour.R, colour.A);
                    pixels[index + 3] = colour.A;
                }
            Marshal.Copy(pixels, 0, data.Scan0, length);
        }
        finally { target.UnlockBits(data); }
        if (BodyBounds.IsEmpty) throw new InvalidDataException("The Chao has no base-body contact geometry.");
        return BodyBounds.Bottom;
    }

    public void Dispose()
    {
        if (ownsGraphics) graphics.Dispose();
    }
}
