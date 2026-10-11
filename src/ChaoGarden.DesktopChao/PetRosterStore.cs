using System.Security.Cryptography;
using System.Text.Json;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;

namespace ChaoGarden.DesktopChao;

public sealed class PetRoster
{
    public int SchemaVersion { get; set; } = 1;
    public Guid PrimaryId { get; set; }
    public bool SyncSa2 { get; set; }
    public int? Size { get; set; }
    public List<int> ExcludedSa2Slots { get; set; } = [];
    public List<DesktopFruitState> Fruit { get; set; } = [];
    public List<PetRosterEntry> Chao { get; set; } = [];
}
public sealed class PetRosterEntry
{
    public Guid Id { get; set; }
    public bool Visible { get; set; } = true;
}

/// <summary>The original chao.json remains the primary Chao. Other saves are keyed by identity, never name.</summary>
public sealed class PetRosterStore
{
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "roster.json");
    public PetRosterStore(string directory) => DirectoryPath = Path.GetFullPath(directory);
    public PetSaveStore PetStore(PetRoster roster, Guid id) => new(id == roster.PrimaryId
        ? DirectoryPath : Path.Combine(DirectoryPath, "chao", id.ToString("N")));
    public PetRoster Load()
    {
        if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak"))
        {
            DesktopChaoState first = new PetSaveStore(DirectoryPath).Load();
            PetRoster roster = new() { PrimaryId = first.Chao.Id, Chao = [new() { Id = first.Chao.Id }] };
            Save(roster);
            return roster;
        }
        try { return Read(File.Exists(FilePath) ? FilePath : FilePath + ".bak"); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            if (!File.Exists(FilePath + ".bak")) throw;
            PetRoster backup = Read(FilePath + ".bak");
            File.Copy(FilePath, FilePath + ".corrupt-" + Guid.NewGuid().ToString("N"));
            return backup;
        }
    }
    private static PetRoster Read(string path)
    {
        PetRoster roster = JsonSerializer.Deserialize<PetRoster>(File.ReadAllText(path), BoothJson.Options)
            ?? throw new InvalidDataException("Empty Chao roster.");
        Validate(roster);
        return roster;
    }
    public static void Validate(PetRoster roster)
    {
        if (roster.SchemaVersion != 1 || roster.Chao == null || roster.Fruit == null || roster.Fruit.Count > 40
            || roster.Fruit.Any(f => f == null || f.Id == Guid.Empty || !Enum.IsDefined(f.Type) || f.Monitor == null
                || !double.IsFinite(f.Position) || f.Position is < 0 or > 1
                || !double.IsFinite(f.Altitude) || f.Altitude is < 0 or > 1 || f.Bites is < 1 or > 4)
            || roster.Fruit.Select(f => f.Id).Distinct().Count() != roster.Fruit.Count || roster.Size is < 128 or > 480
            || roster.ExcludedSa2Slots == null || roster.ExcludedSa2Slots.Any(s => s is < 0 or > 23)
            || roster.Chao.Any(e => e == null || e.Id == Guid.Empty)
            || roster.Chao.Select(e => e.Id).Distinct().Count() != roster.Chao.Count) throw new InvalidDataException("Invalid Chao roster.");
    }
    public DesktopChaoState LoadPet(PetRoster roster, PetRosterEntry entry)
    {
        PetSaveStore store = PetStore(roster, entry.Id);
        if (!store.Exists)
            throw new InvalidDataException($"Saved Chao {entry.Id} is missing. The roster was preserved.");
        DesktopChaoState state = store.Load();
        if (state.Chao.Id != entry.Id) throw new InvalidDataException("Chao identity does not match its roster entry.");
        return state;
    }
    public void Remove(PetRoster roster, DesktopChaoState state)
    {
        PetRosterEntry entry = roster.Chao.Single(e => e.Id == state.Chao.Id);
        // Keep an identity-keyed recovery copy. Publish roster removal before disposing the window.
        new PetSaveStore(Path.Combine(DirectoryPath, "deleted", entry.Id.ToString("N"))).Save(state);
        int index = roster.Chao.IndexOf(entry);
        bool excluded = state.Sa2Slot is int slot && !roster.ExcludedSa2Slots.Contains(slot);
        if (excluded) roster.ExcludedSa2Slots.Add(state.Sa2Slot!.Value);
        roster.Chao.RemoveAt(index);
        try { Save(roster); }
        catch
        {
            roster.Chao.Insert(index, entry);
            if (excluded) roster.ExcludedSa2Slots.Remove(state.Sa2Slot!.Value);
            throw;
        }
        // Original save stays recoverable too. Only roster members are loaded, even when empty.
    }
    public static DesktopChaoState NewChao(IEnumerable<int> existingSeeds)
    {
        HashSet<int> used = existingSeeds.ToHashSet();
        int seed;
        do { seed = BitConverter.ToInt32(RandomNumberGenerator.GetBytes(4)); } while (used.Contains(seed));
        return new DesktopChaoState { Random = new() { Seed = seed }, Position = RandomNumberGenerator.GetInt32(15, 86) / 100.0 };
    }
    public void Save(PetRoster roster)
    {
        Validate(roster);
        Directory.CreateDirectory(DirectoryPath);
        using (FileStream stream = new(FilePath + ".pending", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, roster, BoothJson.Options);
            stream.Flush(true);
        }
        if (File.Exists(FilePath)) File.Replace(FilePath + ".pending", FilePath, FilePath + ".bak");
        else File.Move(FilePath + ".pending", FilePath);
    }
    public static void Rename(DesktopChaoState state, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 32 || name.Any(char.IsControl))
            throw new ArgumentException("Use 1–32 characters without control characters.");
        state.Chao.Name = name;
        state.LocalName = name;
    }
}
