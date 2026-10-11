using System.Security.Cryptography;
using System.Text;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;

namespace ChaoGarden.DesktopChao;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChaoWorldHD", "DesktopChao");
        try
        {
            Dictionary<string, string> options = new();
            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i];
                if (name is "--bake-only" or "--smoke-test" or "--center-only")
                {
                    if (!options.TryAdd(name, "true")) throw new ArgumentException("Duplicate option: " + name);
                }
                else if (name is "--resources" or "--data-dir" or "--pack-resources" && i + 1 < args.Length)
                {
                    if (!options.TryAdd(name, args[++i])) throw new ArgumentException("Duplicate option: " + name);
                }
                else throw new ArgumentException("Use --resources <folder>, --data-dir <folder>, --bake-only, --center-only or --smoke-test. Package assets with --resources <folder> --pack-resources <file.dat>.");
            }
            if (options.TryGetValue("--pack-resources", out string? packageOutput))
            {
                string source = options.GetValueOrDefault("--resources")
                    ?? throw new ArgumentException("--pack-resources requires --resources <folder>.");
                DesktopResourcePackage.Create(Path.GetFullPath(source), packageOutput);
                return 0;
            }
            data = Path.GetFullPath(options.GetValueOrDefault("--data-dir") ?? data);
            string lockId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToUpperInvariant())))[..24];
            using Mutex mutex = new(true, "Local\\ChaoWorldHD.DesktopChao." + lockId, out bool first);
            if (!first)
            {
                if (!options.ContainsKey("--bake-only"))
                    MessageBox.Show("This Chao is already running. Use its taskbar or tray icon to show it.", "DesktopChao");
                return 2;
            }
            string legacyData = options.ContainsKey("--data-dir") ? data
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChaoWorldHD", "DesktopPet");
            string legacyLockId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyData.ToUpperInvariant())))[..24];
            using Mutex legacyMutex = new(true, "Local\\ChaoWorldHD.DesktopPet." + legacyLockId, out bool legacyFirst);
            if (!legacyFirst)
            {
                if (!options.ContainsKey("--bake-only"))
                    MessageBox.Show("Quit the previous DesktopPet before opening DesktopChao so its saves can be migrated safely.", "DesktopChao");
                return 2;
            }
            if (!options.ContainsKey("--data-dir")) DesktopSaveMigration.MigrateDefaultFolder(legacyData, data);
            string resources = FindResources(options.GetValueOrDefault("--resources"), data);
            PetSaveStore store = new(data);
            if (options.ContainsKey("--bake-only"))
            {
                DesktopChaoState state = store.Load();
                bool directions = !options.ContainsKey("--center-only");
                string folder = Path.Combine(data, "sprites", DesktopRecipes.VisualKey(state.Chao), directions ? "full" : "center");
                using HiddenGraphicsHost graphics = new();
                ExportManifest result = BakePipeline.Bake(DesktopRecipes.Create(directions), state.Chao, graphics.Device,
                    resources, null, folder);
                return result.Warnings.Count == 0 ? 0 : 2;
            }
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            if (options.ContainsKey("--smoke-test"))
            {
                using PetForm form = new(store.Load(), store, resources, true);
                Application.Run(form);
            }
            else
            {
                using PetApplicationContext context = new(data, resources);
                Application.Run(context);
            }
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(data);
                File.AppendAllText(Path.Combine(data, "desktop.log"), DateTime.Now + " " + ex + Environment.NewLine);
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("Could not write desktop.log: " + logError.Message);
            }
            if (!args.Contains("--bake-only") && !args.Contains("--smoke-test") && !args.Contains("--pack-resources"))
                MessageBox.Show(ex.Message, "DesktopChao", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static string FindResources(string? explicitPath, string data)
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, DesktopResourcePackage.FileName);
        string? path = explicitPath ?? (File.Exists(bundled) ? bundled : Environment.GetEnvironmentVariable("CHAO_RESOURCES"));
        if (path != null)
        {
            path = Path.GetFullPath(path);
            if (File.Exists(path)) return DesktopResourcePackage.Open(path, Path.Combine(data, "assets"));
            if (!File.Exists(Path.Combine(path, "data", "texlist_al_body.satex")))
                throw new DirectoryNotFoundException("Missing Chao resources: " + path);
            return path;
        }
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory != null)
            {
                path = Path.Combine(directory.FullName, "resources");
                if (File.Exists(Path.Combine(path, "data", "texlist_al_body.satex"))) return path;
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Place DesktopChao.dat beside the executable, pass --resources <folder-or-dat>, or set CHAO_RESOURCES.");
    }
}
