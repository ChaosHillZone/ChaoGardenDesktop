using System.Security.Cryptography;
using System.Text.Json;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;

namespace ChaoGarden.DesktopChao;

public static class DesktopRecipes
{
    public const string Version = "desktop-sprites-v5";
    public static string VisualKey(ChaoEntity chao)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version,
            chao.ChaoType,
            chao.Alignment,
            chao.RunToPower,
            chao.SwimToFly,
            chao.TransformationMagnitude,
            chao.ColourSaturation,
            chao.Appearance,
            chao.Genetics
        }, BoothJson.Options);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static BoothConfig CreateBase()
    {
        BoothConfig config = Create(true);
        foreach (SheetDefinition sheet in config.Sheets.Skip(1))
            sheet.Rows.RemoveAll(row => row.Name != "idle");
        return config;
    }

    public static BoothConfig Create(bool directions)
    {
        List<ShotDefinition> motions = [];
        foreach (DesktopMotion motion in DesktopMotions.All.Values)
        {
            if (motion.Name.StartsWith("eat-", StringComparison.Ordinal))
            {
                for (int bites = 1; bites <= 4; bites++)
                {
                    ShotDefinition eat = Shot(motion);
                    eat.Name += "-" + bites;
                    // Looking around pauses chewing and restores the configured mouth.
                    if (motion.Name is not ("eat-look" or "eat-pickup" or "eat-sit-down")) eat.Mouth = MouthType.Open;
                    eat.Props.Add(new PropDefinition { RemainingBites = bites, Scale = .65f });
                    motions.Add(eat);
                }
            }
            else motions.Add(Shot(motion));
        }
        BoothConfig config = new()
        {
            CellWidth = 384,
            CellHeight = 416,
            Supersample = 1,
            Columns = 8,
            Layout = SheetLayout.PerAnimation,
            MaxSheetSize = 4096,
            SaveFrames = false,
            AutoFrame = true,
            Camera = new CameraSettings { OrthographicHeight = 8.8f, TargetHeight = 3.35f },
            Sheets = [new SheetDefinition { Name = "center", Rows = motions }]
        };
        if (directions)
        {
            for (int y = -2; y <= 2; y++)
                for (int x = -3; x <= 3; x++)
                {
                    if (x == 0 && y == 0) continue;
                    // Scripted head movement (eating, scratching, rolling) keeps its authored pose.
                    List<ShotDefinition> variants = motions.Where(row => DesktopMotions.All.TryGetValue(row.Name, out DesktopMotion? motion) && motion.Cursor)
                        .Select(BoothJson.Clone).ToList();
                    foreach (ShotDefinition row in variants)
                    {
                        row.HeadYawDegrees = [x * 10];
                        row.HeadPitchDegrees = [y * 10];
                    }
                    config.Sheets.Add(new SheetDefinition { Name = DesktopChaoSimulation.DirectionName(x, y), Rows = variants });
                }
        }
        BoothJson.Validate(config);
        return config;
    }

    private static ShotDefinition Shot(DesktopMotion motion)
    {
        ShotDefinition row = new()
        {
            Name = motion.Name,
            Animation = motion.Clip,
            StartFrame = motion.Start,
            EndFrame = motion.End,
            Frames = motion.Frames,
            FramesPerSecond = 60,
            YawDegrees = motion.Yaw,
            Playback = motion.Loop switch { DesktopLoop.Loop => Playback.Loop, DesktopLoop.PingPong => Playback.PingPong, _ => Playback.Once }
        };
        if (motion.Name is "sleep-start" or "sleeping" or "roll-belly" or "sleep-belly" or "roll-back") row.Eyes = EyeType.ClosedStraight;
        if (motion.Name is "idle" or "sitting") row.BlinkFrames = [70, 71, 72, 73];
        if (motion.Name == "petting")
        {
            row.Eyes = EyeType.ClosedHappy;
            row.Mouth = MouthType.ClosedSmile;
            row.Emote = new EmoteSettings { Kind = EmoteKind.Heart, Tint = "#ff84ac" };
        }
        return row;
    }
}
