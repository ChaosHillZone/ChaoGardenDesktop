using System.Text.Json;
using ChaoGarden.Core.Enums;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;

namespace ChaoGarden.DesktopChao;

public sealed class PetSaveStore
{
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "chao.json");
    private string LegacyPath => Path.Combine(DirectoryPath, "pet.json");
    public bool Exists => File.Exists(FilePath) || File.Exists(FilePath + ".bak")
        || File.Exists(LegacyPath) || File.Exists(LegacyPath + ".bak");
    public string? RecoveryMessage { get; private set; }

    public PetSaveStore(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
    }

    public DesktopChaoState Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak")
            && (File.Exists(LegacyPath) || File.Exists(LegacyPath + ".bak")))
        {
            DesktopChaoState migrated;
            try { migrated = Read(File.Exists(LegacyPath) ? LegacyPath : LegacyPath + ".bak"); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                if (!File.Exists(LegacyPath + ".bak")) throw;
                migrated = Read(LegacyPath + ".bak");
            }
            // Preserve the old files as migration backups; never overwrite a new-format save.
            Save(migrated);
            return migrated;
        }
        if (!File.Exists(FilePath))
        {
            if (File.Exists(FilePath + ".bak"))
            {
                RecoveryMessage = "Recovered your Chao from its backup.";
                return Read(FilePath + ".bak");
            }
            DesktopChaoState fresh = PetRosterStore.NewChao([]);
            Save(fresh);
            return fresh;
        }
        try
        {
            return Read(FilePath);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            // Never replace an unreadable Chao with a new child.
            if (!File.Exists(FilePath + ".bak"))
            {
                throw new InvalidDataException($"Cannot read {FilePath}. Your save has been preserved; repair it or restore a backup.", ex);
            }
            DesktopChaoState recovered = Read(FilePath + ".bak");
            File.Copy(FilePath, FilePath + ".corrupt-" + Guid.NewGuid().ToString("N"));
            RecoveryMessage = "Recovered your Chao from its backup; the damaged file was preserved.";
            return recovered;
        }
    }

    private static DesktopChaoState Read(string path)
    {
        DesktopChaoState state = JsonSerializer.Deserialize<DesktopChaoState>(File.ReadAllText(path), BoothJson.Options)
            ?? throw new InvalidDataException("Empty pet save.");
        if (state.SchemaVersion == 1)
        {
            // v1 desktop care incorrectly saturated an unevolved neutral child's palette.
            // Preserve identity, parts, stats and user-authored adult/morphed appearances.
            File.Copy(path, path + ".v1-backup-" + Guid.NewGuid().ToString("N"));
            if (state.Chao != null && state.Chao.ChaoType == ChaoType.Child && state.Chao.Alignment == 0
                && state.Chao.TransformationMagnitude == 0 && state.Chao.RunToPower == 0 && state.Chao.SwimToFly == 0)
                state.Chao.ColourSaturation = 0;
            state.SchemaVersion = 2;
            DesktopChaoSimulation.Begin(state, state.RemainingBites > 0 ? DesktopActivity.EatStart : DesktopActivity.Idle,
                state.RemainingBites > 0 ? DesktopMotions.All["eat-start"].Frames : 5400);
        }
        Validate(state);
        if (state.Activity == DesktopActivity.Held) new DesktopSurfaceController().Drop(state);
        // Pause is a session control; restarting always resumes the saved Chao.
        state.Settings.Paused = false;
        state.Settings.RenderingMode = DesktopRenderingMode.Live3D;
        return state;
    }

    public static void Validate(DesktopChaoState state)
    {
        if (!Enum.IsDefined(state.EatingFruitType) || state.ForageTicks is < 0 or > 1801
            || state.LastAcquiredFruitId == Guid.Empty || state.TargetFruitId == Guid.Empty
            || state.LocalType is { } localType && !DesktopChaoTypes.IsSelectable(localType)
            || state.Sa2Slot is < 0 or > 23 || state.Activity == DesktopActivity.GameAnimation && DesktopAnimationCatalog.Find(state.CatalogClip) == null
            || state.SchemaVersion != 2 || state.Chao == null || !Enum.IsDefined(state.Chao.ChaoType) || state.Settings == null || state.Random == null
            || state.Chao.Stats == null || state.Chao.Emotions?.NeedDrives == null || state.Chao.Emotions.ByteEmotions == null
            || state.Chao.Emotions.BehavioralStates == null || state.Chao.Emotions.PersonalityBytes == null
            || state.Chao.Genetics == null || state.Chao.Bonds == null || state.Chao.Animation == null
            || state.Chao.Physics == null || state.Chao.Behaviour == null || state.Chao.Navigation == null
            || !double.IsFinite(state.Altitude) || state.Altitude is < 0 or > 1 || !float.IsFinite(state.FacingYaw)
            || state.TravelGait is not (ChaoAnimation.LandCrawlStart or ChaoAnimation.LandWalk or ChaoAnimation.LandRun or ChaoAnimation.LandRunFast)
            || state.TravelDirection is not (DesktopActivity.WalkLeft or DesktopActivity.WalkRight) || state.TravelTicksRemaining < 1
            || !Enum.IsDefined(state.AfterLanding) || !Enum.IsDefined(state.AfterCrawling)
            || !Enum.IsDefined(state.Activity) || !double.IsFinite(state.Position) || state.Position is < 0 or > 1
            || !Enum.IsDefined(state.AfterStanding) || state.RestTicksRemaining < 0 || state.SleepCycles < 0
            || state.ActivityTicks < 0 || state.ActivityDuration < 1 || state.TotalTicks < 0
            || state.RemainingBites is < 0 or > 4 || state.BiteTicks is < 0 or >= DesktopChaoSimulation.BiteDuration
            || state.Activity is DesktopActivity.Eating or DesktopActivity.EatFast or DesktopActivity.EatStart
                or DesktopActivity.EatLook or DesktopActivity.EatResume or DesktopActivity.EatSitDown or DesktopActivity.FruitPickup && state.RemainingBites == 0
            || state.Settings.Size is < 128 or > 480 || !Enum.IsDefined(state.Settings.RenderingMode)
            || state.Random.GenerationCount < 0)
        {
            throw new InvalidDataException("Invalid desktop pet state.");
        }
        if (state.Pastime == null || state.Pastime.Id == null || state.Pastime.PreviousId == null
            || !Enum.IsDefined(state.Pastime.Phase) || state.Pastime.Ticks < 0 || state.Pastime.Duration < 0
            || state.Pastime.Cooldown < 0 || state.Pastime.VariantTicks < 0 || state.Pastime.Step < 0
            || state.Pastime.Phase != DesktopPastimePhase.None && (state.Activity != DesktopActivity.GameAnimation
                || DesktopPastimeSystem.Find(state.Pastime.Id) is not { } pastime || state.Pastime.Step >= pastime.Variants.Length))
            throw new InvalidDataException("Invalid desktop pastime state.");
        BoothJson.Validate(new BoothConfig
        {
            Chao = state.Chao,
            Sheets = [new SheetDefinition { Rows = [new ShotDefinition()] }]
        });
        if (!Enum.IsDefined(state.Chao.Appearance.Hat))
        {
            throw new InvalidDataException("Unknown Chao hat.");
        }
    }

    public void Save(DesktopChaoState state)
    {
        Validate(state);
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".pending";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state, BoothJson.Options);
        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        if (File.Exists(FilePath))
        {
            File.Replace(temporary, FilePath, FilePath + ".bak");
        }
        else
        {
            File.Move(temporary, FilePath);
        }
    }
}
