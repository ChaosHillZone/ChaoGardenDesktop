using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ChaoGarden.Content.Loading;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Rendering;

namespace ChaoGarden.DesktopChao;

/// <summary>Versioned, compressed resources shared by all desktop Chao. No game installation is required.</summary>
public static class DesktopResourcePackage
{
    public const string FileName = "DesktopChao.dat";
    private const string ManifestName = "manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] ModelFolders = ["glb/chao/variants/", "glb/chao/parts/", "glb/chao/masks/", "glb/chao/jewels/"];

    private static readonly PropDefinition[] Props = Enum.GetValues<FruitType>()
        .Where(t => FruitModelKeys.Get(t) != null).Select(t => DesktopFruitCatalog.Prop(t))
        .Concat(DesktopAnimationCatalog.All.SelectMany(a => DesktopCatalogShots.Create(a).Props))
        .DistinctBy(p => (p.Model, p.TextureList, p.TextureDirectory)).ToArray();
    private static readonly HashSet<string> PropModels = Props.Select(p => p.Model).ToHashSet(StringComparer.Ordinal);

    public sealed record Asset(string Path, long Length, string Sha256);
    public sealed record Manifest(int Version, Asset[] Files);

    /// <summary>Builds a deterministic archive; unknown resource directories are never swept into a release.</summary>
    public static Manifest Create(string resources, string output)
    {
        string[] paths = SelectFiles(resources);
        Manifest manifest = new(1, paths.Select(path =>
        {
            string full = Path.Combine(resources, path);
            using FileStream stream = File.OpenRead(full);
            return new Asset(path, stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
        }).ToArray());
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (ZipArchive zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                Write(zip, ManifestName, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
                foreach (Asset asset in manifest.Files)
                {
                    byte[] bytes = File.ReadAllBytes(Path.Combine(resources, asset.Path));
                    if (bytes.LongLength != asset.Length || Convert.ToHexString(SHA256.HashData(bytes)) != asset.Sha256)
                        throw new IOException("Resource changed while packaging: " + asset.Path);
                    Write(zip, asset.Path, bytes);
                }
            }
            File.Move(temporary, output, overwrite: true);
            return manifest;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string[] SelectFiles(string resources)
    {
        HashSet<string> files = new(StringComparer.Ordinal);
        void Add(string path)
        {
            if (!IsAllowedPath(path)) throw new InvalidDataException("Unexpected desktop resource: " + path);
            if (!File.Exists(Path.Combine(resources, path))) throw new FileNotFoundException("Missing required desktop resource: " + path);
            files.Add(path);
        }
        void Folder(string folder, string pattern)
        {
            string[] matches = Directory.GetFiles(Path.Combine(resources, folder), pattern, SearchOption.AllDirectories);
            if (matches.Length == 0) throw new FileNotFoundException("Empty required desktop resource folder: " + folder);
            foreach (string path in matches) Add(Path.GetRelativePath(resources, path).Replace('\\', '/'));
        }
        void TextureList(string list, string folder)
        {
            Add(list);
            foreach (string name in SatexParser.Parse(Path.Combine(resources, list)).Where(n => n.Length > 0).Distinct())
            {
                string path = folder + "/" + name + ".png";
                // Lists also describe unused SA2 assets that are absent from the source checkout.
                if (File.Exists(Path.Combine(resources, path))) Add(path);
            }
        }

        foreach (string folder in ModelFolders) Folder(folder, "*.glb");
        TextureList("data/texlist_al_body.satex", "textures-hq/chao/body");
        TextureList("data/texlist_al_parts.satex", "textures-hq/chao/parts");
        // Preserve native palette masks when supplied; current HQ-only checkouts use the renderer's existing fallback.
        foreach (string name in SatexParser.Parse(Path.Combine(resources, "data/texlist_al_body.satex")).Take(18))
            if (File.Exists(Path.Combine(resources, "textures-lq/chao/body/" + name + ".png")))
                Add("textures-lq/chao/body/" + name + ".png");
        foreach (string folder in new[] { "eyes", "mouth", "jewel" }) Folder("textures-hq/chao/" + folder, "*.png");
        Folder("textures-hq/chao/icons", "al_icon*.png");
        Add("data/fruit_definitions.json");
        // Include the actual texture dependencies of food, instruments and other activity props.
        foreach (PropDefinition prop in Props)
        {
            Add(prop.Model);
            Add(prop.TextureList);
            string[] names = SatexParser.Parse(Path.Combine(resources, prop.TextureList));
            foreach (GlbMaterialData material in GlbParser.Parse(Path.Combine(resources, prop.Model)).Materials)
            {
                if (material.TextureId < 0) continue;
                if (material.TextureId >= names.Length || names[material.TextureId].Length == 0)
                    throw new InvalidDataException("Unmapped prop texture in " + prop.Model);
                Add(prop.TextureDirectory + "/" + names[material.TextureId] + ".png");
            }
        }
        return files.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Validates and extracts once per archive hash. Existing renderer file APIs use this shared cache.</summary>
    public static string Open(string packagePath, string cacheRoot)
    {
        using FileStream package = File.OpenRead(packagePath);
        string hash = Convert.ToHexString(SHA256.HashData(package));
        package.Position = 0;
        using ZipArchive zip = new(package, ZipArchiveMode.Read);
        Manifest manifest = ReadManifest(zip);
        cacheRoot = Path.GetFullPath(cacheRoot);
        Directory.CreateDirectory(cacheRoot);
        string destination = Path.Combine(cacheRoot, hash);
        using FileStream cacheLock = Lock(Path.Combine(cacheRoot, hash + ".lock"));
        if (manifest.Files.All(asset => Matches(Path.Combine(destination, asset.Path), asset))) return destination;
        string staging = Path.Combine(cacheRoot, hash + ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (Asset asset in manifest.Files)
            {
                ZipArchiveEntry entry = zip.GetEntry(asset.Path)!;
                string target = Path.Combine(staging, asset.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (Stream input = entry.Open())
                using (FileStream output = File.Create(target)) input.CopyTo(output);
                if (!Matches(target, asset)) throw new InvalidDataException("Damaged desktop resource: " + asset.Path);
            }
            // Only replace the cache directory derived from this archive's SHA-256, never the save folder.
            if (Directory.Exists(destination)) RetryFileSystem(() => Directory.Delete(destination, recursive: true));
            RetryFileSystem(() => Directory.Move(staging, destination));
            return destination;
        }
        finally { if (Directory.Exists(staging)) RetryFileSystem(() => Directory.Delete(staging, recursive: true)); }
    }

    private static Manifest ReadManifest(ZipArchive zip)
    {
        ZipArchiveEntry entry = zip.GetEntry(ManifestName) ?? throw new InvalidDataException("Missing desktop resource manifest.");
        if (entry.Length > 4 * 1024 * 1024) throw new InvalidDataException("Desktop manifest is too large.");
        using Stream stream = entry.Open();
        Manifest manifest = JsonSerializer.Deserialize<Manifest>(stream) ?? throw new InvalidDataException("Empty desktop manifest.");
        if (manifest.Version != 1 || manifest.Files == null || manifest.Files.Length is 0 or > 10000)
            throw new InvalidDataException("Unsupported desktop resource package.");
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (Asset asset in manifest.Files)
        {
            if (asset == null || !IsAllowedPath(asset.Path) || !names.Add(asset.Path)
                || asset.Length is < 0 or > 64 * 1024 * 1024 || asset.Sha256 == null
                || asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid desktop resource entry.");
            ZipArchiveEntry? file = zip.GetEntry(asset.Path);
            if (file == null || file.Length != asset.Length) throw new InvalidDataException("Missing or damaged desktop resource: " + asset.Path);
            total += asset.Length;
        }
        if (total > 1024L * 1024 * 1024 || zip.Entries.Count != names.Count + 1
            || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
            throw new InvalidDataException("Invalid desktop resource archive contents.");
        return manifest;
    }

    public static bool IsAllowedPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/')
            || path.Split('/').Any(p => p is "" or "." or ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return false;
        if (path is "data/texlist_al_body.satex" or "data/texlist_al_parts.satex" or "data/texlist_al_object.satex" or "data/fruit_definitions.json" or "data/texlist_al_toy.satex") return true;
        if (path.EndsWith(".glb", StringComparison.Ordinal))
            return ModelFolders.Any(p => path.StartsWith(p, StringComparison.Ordinal))
                || PropModels.Contains(path);
        if (!path.EndsWith(".png", StringComparison.Ordinal)) return false;
        return new[] { "textures-hq/chao/body/", "textures-lq/chao/body/", "textures-hq/chao/parts/",
            "textures-hq/chao/eyes/", "textures-hq/chao/mouth/", "textures-hq/chao/jewel/",
            "textures-hq/chao/icons/al_icon", "textures-hq/items/al_object/", "textures-hq/chao/toys/" }
            .Any(p => path.StartsWith(p, StringComparison.Ordinal));
    }

    private static bool Matches(string path, Asset asset)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != asset.Length) return false;
        using FileStream stream = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), asset.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static FileStream Lock(string path)
    {
        for (int retry = 0; ; retry++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (retry < 100) { Thread.Sleep(100); }
        }
    }

    private static void RetryFileSystem(Action action)
    {
        // Windows scanners can briefly hold newly written assets without delete sharing.
        for (int retry = 0; ; retry++)
        {
            try { action(); return; }
            catch (IOException) when (retry < 10) { Thread.Sleep(50 * (retry + 1)); }
            catch (UnauthorizedAccessException) when (retry < 10) { Thread.Sleep(50 * (retry + 1)); }
        }
    }

    private static void Write(ZipArchive zip, string path, byte[] bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.SmallestSize);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream output = entry.Open();
        output.Write(bytes);
    }
}
