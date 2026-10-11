namespace ChaoGarden.DesktopChao;

public static class DesktopSaveMigration
{
    public static void MigrateDefaultFolder(string legacy, string destination)
    {
        if (!Directory.Exists(legacy) || new PetSaveStore(destination).Exists
            || File.Exists(Path.Combine(destination, "roster.json")) || File.Exists(Path.Combine(destination, "roster.json.bak"))) return;
        if (!new PetSaveStore(legacy).Exists && !File.Exists(Path.Combine(legacy, "roster.json"))
            && !File.Exists(Path.Combine(legacy, "roster.json.bak"))) return;
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination)
            .Any(p => !string.Equals(Path.GetFileName(p), "desktop.log", StringComparison.OrdinalIgnoreCase) || Directory.Exists(p)))
            throw new IOException("DesktopChao save folder already contains files but no readable roster. Existing data was preserved.");
        string staging = destination + ".migration-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        // Copy saves and their backups, not potentially enormous disposable sprite caches.
        CopySaves(legacy, staging);
        PetRosterStore stagedStore = new(staging);
        PetRoster roster = stagedStore.Load();
        foreach (PetRosterEntry entry in roster.Chao) stagedStore.LoadPet(roster, entry);
        // Publish only after every Chao validates. Legacy saves and failed staging remain recoverable.
        // A previous failed startup may have created only desktop.log. Preserve it and allow repair/retry.
        if (Directory.Exists(destination)) Directory.Move(destination, destination + ".before-migration-" + Guid.NewGuid().ToString("N"));
        Directory.Move(staging, destination);
    }
    private static void CopySaves(string source, string destination)
    {
        foreach (string file in Directory.EnumerateFiles(source))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith("pet.json", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("chao.json", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("roster.json", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(destination, name));
        }
        string children = Path.Combine(source, "chao");
        if (!Directory.Exists(children)) return;
        foreach (string child in Directory.EnumerateDirectories(children))
        {
            if (!Guid.TryParseExact(Path.GetFileName(child), "N", out _)) continue;
            string target = Path.Combine(destination, "chao", Path.GetFileName(child));
            Directory.CreateDirectory(target);
            CopySaves(child, target);
        }
    }
}
